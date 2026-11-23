// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures;

public static partial class DocumentCaptureFieldSchemas
{
    public static IReadOnlyList<string> SupportedKinds { get; } =
        ["expense-receipt", "purchase-invoice", "sales-invoice", "bank", "assistant-attachment"];

    public const int CurrentSchemaVersion = 1;
    public const int MaxFinancialLines = 100;
    public const int MaxAssistantFields = 64;
    public const int MaxProviderJsonLength = 65_536;
    public const int MaxTextLength = 2_000;
    public const int MaxLineDescriptionLength = 1_000;
    public const int MaxRawTextLength = 8_000;
    public const int MaxNestingDepth = 4;
    public const int MaxDecimalSignificantDigits = 18;
    public const int MaxDecimalScale = 4;

    public const string EmptyObjectJson = "{}";
    public const string EmptyAssistantFieldsJson = "{\"fields\":[]}";

    public static string EmptyFieldsJsonForKind(string kind) =>
        string.Equals(kind, "assistant-attachment", StringComparison.Ordinal)
            ? EmptyAssistantFieldsJson
            : EmptyObjectJson;

    private static readonly HashSet<string> VendorFields = new(StringComparer.Ordinal)
    {
        "vendorName", "vendorInvoiceNo", "date", "dueDate", "referenceNo", "currency", "notes",
        "lines", "sourceTotals", "paymentHints"
    };

    private static readonly HashSet<string> SalesFields = new(StringComparer.Ordinal)
    {
        "customerName", "sourceInvoiceNo", "date", "dueDate", "referenceNo", "currency", "notes",
        "lines", "sourceTotals"
    };

    private static readonly HashSet<string> BankFields = new(StringComparer.Ordinal)
    {
        "name", "bankName", "accountNumber", "routingNumber", "iban", "swift"
    };

    private static readonly HashSet<string> LineFields = new(StringComparer.Ordinal)
    {
        "description", "quantity", "unitAmount", "sourceLineTotal", "taxHint", "discount", "priceBasis",
        "missingQuantityDefaulted"
    };

    private static readonly HashSet<string> TotalFields = new(StringComparer.Ordinal)
    {
        "subtotal", "taxTotal", "grandTotal", "discountTotal", "additionalCharges", "priceBasis"
    };

    private static readonly HashSet<string> PaymentHintFields = new(StringComparer.Ordinal)
    {
        "paymentStatus", "amountPaid", "paymentMethod"
    };

    private static readonly HashSet<string> AssistantFieldProperties = new(StringComparer.Ordinal)
    {
        "label", "value", "confidence", "box"
    };

    private static readonly HashSet<string> BoxProperties = new(StringComparer.Ordinal)
    {
        "page", "x", "y", "width", "height"
    };

    public static bool TryValidate(
        string kind,
        int schemaVersion,
        JsonElement fields,
        out string canonicalJson,
        out string? errorCode)
    {
        canonicalJson = string.Empty;
        errorCode = null;

        if (schemaVersion != CurrentSchemaVersion || !IsKnownKind(kind))
        {
            errorCode = "DOCUMENT_KIND_UNKNOWN";
            return false;
        }

        if (fields.ValueKind != JsonValueKind.Object || GetMaximumNesting(fields, 0) > MaxNestingDepth)
            return Invalid(out errorCode);

        var allowed = kind switch
        {
            "expense-receipt" or "purchase-invoice" => VendorFields,
            "sales-invoice" => SalesFields,
            "bank" => BankFields,
            "assistant-attachment" => new HashSet<string>(StringComparer.Ordinal) { "fields" },
            _ => throw new InvalidOperationException("Known document kind dispatch is incomplete.")
        };

        var count = 0;
        foreach (var property in fields.EnumerateObject())
        {
            if (++count > 32 || !allowed.Contains(property.Name))
                return Invalid(out errorCode);
        }

        if (kind is "expense-receipt" or "purchase-invoice" or "sales-invoice")
        {
            var partyName = kind == "sales-invoice" ? "customerName" : "vendorName";
            if (!OptionalText(fields, partyName, MaxTextLength)
                || !OptionalText(fields, kind == "sales-invoice" ? "sourceInvoiceNo" : "vendorInvoiceNo", 200)
                || !OptionalText(fields, "referenceNo", 200)
                || !OptionalText(fields, "notes", MaxTextLength)
                || !OptionalDate(fields, "date")
                || !OptionalDate(fields, "dueDate")
                || !OptionalCurrency(fields, "currency")
                || !OptionalLines(fields)
                || !OptionalObject(fields, "sourceTotals", TotalFields, ValidateTotal)
                || (kind != "sales-invoice" && !OptionalObject(fields, "paymentHints", PaymentHintFields, ValidatePaymentHint)))
                return Invalid(out errorCode);
        }
        else if (kind == "bank")
        {
            if (!OptionalText(fields, "name", MaxTextLength)
                || !OptionalText(fields, "bankName", MaxTextLength)
                || !OptionalText(fields, "accountNumber", 64)
                || !OptionalText(fields, "routingNumber", 32)
                || !OptionalText(fields, "iban", 64)
                || !OptionalText(fields, "swift", 32))
                return Invalid(out errorCode);
        }
        else if (!ValidateAssistantFields(fields))
        {
            return Invalid(out errorCode);
        }

        using (var canonicalBuffer = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(canonicalBuffer))
                WriteCanonical(fields, writer);
            canonicalJson = Encoding.UTF8.GetString(canonicalBuffer.ToArray());
        }
        return true;
    }

    private static bool IsKnownKind(string kind) => kind is
        "expense-receipt" or "purchase-invoice" or "sales-invoice" or "bank" or "assistant-attachment";

    public static bool IsSupportedKind(string kind) => IsKnownKind(kind);

    public static bool TryValidateAcceptedFields(
        string kind,
        int schemaVersion,
        JsonElement fields,
        out string canonicalJson,
        out string? errorCode)
    {
        if (!TryValidate(kind, schemaVersion, fields, out canonicalJson, out errorCode))
            return false;
        if (kind is not ("expense-receipt" or "purchase-invoice" or "sales-invoice"))
            return true;

        if (!fields.TryGetProperty("date", out var date)
            || date.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Invalid(out errorCode);
        if (!fields.TryGetProperty("lines", out var lines))
            return true;
        foreach (var line in lines.EnumerateArray())
        {
            var hasContent = line.EnumerateObject().Any(property => property.Value.ValueKind switch
            {
                JsonValueKind.Null => false,
                JsonValueKind.String => !string.IsNullOrWhiteSpace(property.Value.GetString()),
                _ => true
            });
            if (!hasContent)
                continue;
            if (!line.TryGetProperty("unitAmount", out var unitAmount) || unitAmount.ValueKind != JsonValueKind.String)
                return Invalid(out errorCode);
            if (line.TryGetProperty("quantity", out var quantity)
                && quantity.ValueKind == JsonValueKind.String
                && (!decimal.TryParse(quantity.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var parsedQuantity) || parsedQuantity <= 0))
                return Invalid(out errorCode);
        }
        return true;
    }

    public static string SanitizeProviderFields(string kind, string json)
    {
        if (!IsKnownKind(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Provider fields must be an object.");
        var allowed = kind switch
        {
            "expense-receipt" or "purchase-invoice" => VendorFields,
            "sales-invoice" => SalesFields,
            "bank" => BankFields,
            "assistant-attachment" => new HashSet<string>(StringComparer.Ordinal) { "fields" },
            _ => throw new InvalidOperationException("Known document kind dispatch is incomplete.")
        };
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in parsed.RootElement.EnumerateObject().Where(property => allowed.Contains(property.Name))
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                var nested = property.Name switch
                {
                    "lines" => LineFields,
                    "sourceTotals" => TotalFields,
                    "paymentHints" => PaymentHintFields,
                    "fields" when kind == "assistant-attachment" => AssistantFieldProperties,
                    _ => null
                };
                if (nested is not null && property.Value.ValueKind == JsonValueKind.Array)
                    WriteFilteredArray(property.Value, nested, writer, property.Name == "fields" ? BoxProperties : null);
                else if (nested is not null && property.Value.ValueKind == JsonValueKind.Object)
                    WriteFilteredObject(property.Value, nested, writer);
                else
                    WriteCanonical(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void WriteFilteredArray(JsonElement array, HashSet<string> allowed, Utf8JsonWriter writer, HashSet<string>? boxProperties)
    {
        writer.WriteStartArray();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                WriteCanonical(item, writer);
                continue;
            }
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject().Where(property => allowed.Contains(property.Name))
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                if (boxProperties is not null && property.Name == "box" && property.Value.ValueKind == JsonValueKind.Object)
                    WriteFilteredObject(property.Value, boxProperties, writer);
                else
                    WriteCanonical(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteFilteredObject(JsonElement value, HashSet<string> allowed, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var property in value.EnumerateObject().Where(property => allowed.Contains(property.Name))
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(property.Name);
            WriteCanonical(property.Value, writer);
        }
        writer.WriteEndObject();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool OptionalLines(JsonElement fields)
    {
        if (!fields.TryGetProperty("lines", out var lines))
            return true;
        if (lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() > MaxFinancialLines)
            return false;

        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object || !HasOnlyProperties(line, LineFields))
                return false;
            if (!OptionalText(line, "description", MaxLineDescriptionLength)
                || !OptionalText(line, "taxHint", 200)
                || !OptionalText(line, "priceBasis", 16)
                || !OptionalDecimal(line, "quantity")
                || !OptionalDecimal(line, "unitAmount")
                || !OptionalDecimal(line, "sourceLineTotal")
                || !OptionalDecimal(line, "discount")
                || !OptionalBoolean(line, "missingQuantityDefaulted"))
                return false;
        }

        return true;
    }

    private static bool ValidateTotal(JsonElement totals, string name, JsonElement value) => name == "priceBasis"
        ? IsShortString(value, 16)
        : IsDecimalString(value);

    private static bool ValidatePaymentHint(JsonElement hints, string name, JsonElement value) => name switch
    {
        "paymentStatus" => IsOneOf(value, "paid", "unpaid", "partial", "unknown"),
        "amountPaid" => IsDecimalString(value),
        "paymentMethod" => IsShortString(value, 100),
        _ => false
    };

    private static bool ValidateAssistantFields(JsonElement fields)
    {
        if (!fields.TryGetProperty("fields", out var values))
            return true;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > MaxAssistantFields)
            return false;

        foreach (var field in values.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object || !HasOnlyProperties(field, AssistantFieldProperties))
                return false;
            if (!OptionalText(field, "label", 200) || !OptionalText(field, "value", MaxTextLength))
                return false;
            if (field.TryGetProperty("confidence", out var confidence)
                && (confidence.ValueKind != JsonValueKind.Number || !confidence.TryGetDouble(out var score) || score is < 0 or > 1))
                return false;
            if (field.TryGetProperty("box", out var box)
                && box.ValueKind is not JsonValueKind.Null
                && !ValidBox(box))
                return false;
        }

        return true;
    }

    private static bool ValidBox(JsonElement box)
    {
        if (box.ValueKind != JsonValueKind.Object || !HasOnlyProperties(box, BoxProperties)
            || !box.TryGetProperty("page", out var page)
            || page.ValueKind != JsonValueKind.Number
            || !page.TryGetInt32(out var pageNumber)
            || pageNumber is < 1 or > 10)
            return false;

        return NormalizedCoordinate(box, "x")
            && NormalizedCoordinate(box, "y")
            && NormalizedCoordinate(box, "width")
            && NormalizedCoordinate(box, "height");
    }

    private static bool NormalizedCoordinate(JsonElement box, string name) =>
        box.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var coordinate)
        && double.IsFinite(coordinate)
        && coordinate is >= 0 and <= 1;

    private static int GetMaximumNesting(JsonElement element, int depth)
    {
        var maximum = depth;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                maximum = Math.Max(maximum, GetMaximumNesting(property.Value, depth + 1));
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                maximum = Math.Max(maximum, GetMaximumNesting(item, depth + 1));
        }

        return maximum;
    }

    private static bool OptionalObject(
        JsonElement parent,
        string name,
        HashSet<string> properties,
        Func<JsonElement, string, JsonElement, bool> validate)
    {
        if (!parent.TryGetProperty(name, out var value))
            return true;
        if (value.ValueKind != JsonValueKind.Object || !HasOnlyProperties(value, properties))
            return false;
        foreach (var property in value.EnumerateObject())
        {
            if (!validate(value, property.Name, property.Value))
                return false;
        }
        return true;
    }

    private static bool HasOnlyProperties(JsonElement element, HashSet<string> allowed)
    {
        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (++count > 32 || !allowed.Contains(property.Name))
                return false;
        }
        return true;
    }

    private static bool OptionalText(JsonElement parent, string name, int maxLength)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        return value.ValueKind == JsonValueKind.String && (value.GetString()?.Length ?? 0) <= maxLength;
    }

    private static bool OptionalDate(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        return value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool OptionalCurrency(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var currency = value.GetString();
        return currency == "unknown" || (currency is { Length: 3 } && Regex.IsMatch(currency, "^[A-Z]{3}$", RegexOptions.CultureInvariant));
    }

    private static bool OptionalDecimal(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        return IsDecimalString(value);
    }

    private static bool IsDecimalString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var text = value.GetString();
        if (string.IsNullOrEmpty(text))
            return false;

        var start = text[0] == '-' ? 1 : 0;
        if (start == text.Length)
            return false;
        var decimalIndex = text.IndexOf('.', start);
        if (decimalIndex != text.LastIndexOf('.'))
            return false;
        var integerEnd = decimalIndex < 0 ? text.Length : decimalIndex;
        var fractionLength = decimalIndex < 0 ? 0 : text.Length - decimalIndex - 1;
        if (fractionLength > MaxDecimalScale || integerEnd == start || (decimalIndex >= 0 && fractionLength == 0))
            return false;
        if (text[start] == '0' && integerEnd - start > 1)
            return false;
        for (var index = start; index < text.Length; index++)
        {
            if (index == decimalIndex)
                continue;
            if (text[index] is < '0' or > '9')
                return false;
        }

        var significant = 0;
        var sawNonZero = false;
        for (var index = start; index < text.Length; index++)
        {
            if (index == decimalIndex)
                continue;
            if (text[index] != '0')
                sawNonZero = true;
            if (sawNonZero)
                significant++;
        }
        return significant <= MaxDecimalSignificantDigits;
    }

    private static bool OptionalBoolean(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static bool IsShortString(JsonElement value, int limit) =>
        value.ValueKind == JsonValueKind.String && (value.GetString()?.Length ?? 0) <= limit;

    private static bool IsOneOf(JsonElement value, params string[] choices) =>
        value.ValueKind == JsonValueKind.String && choices.Contains(value.GetString(), StringComparer.Ordinal);

    private static bool Invalid(out string? errorCode)
    {
        errorCode = "DOCUMENT_ACCEPT_INVALID";
        return false;
    }
}
