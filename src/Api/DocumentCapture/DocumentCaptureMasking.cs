// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Api.DocumentCaptures;

public static partial class DocumentCaptureMasking
{
    public static string MaskAssistantFields(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("fields", out var values)
            || values.ValueKind != JsonValueKind.Array)
            return JsonSerializer.Serialize(fields);

        var masked = new List<Dictionary<string, object?>>();
        foreach (var item in values.EnumerateArray())
        {
            var field = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                field[property.Name] = (property.Name is "value" or "label") && property.Value.ValueKind == JsonValueKind.String
                    ? MaskSensitiveValue(property.Value.GetString() ?? string.Empty)
                    : JsonSerializer.Deserialize<object>(property.Value.GetRawText());
            }
            masked.Add(field);
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?> { ["fields"] = masked });
    }

    public static string? MaskRawText(string? rawText)
    {
        if (rawText is null)
            return null;
        if (rawText.Length > DocumentCaptureFieldSchemas.MaxRawTextLength)
            rawText = rawText[..DocumentCaptureFieldSchemas.MaxRawTextLength];
        return MaskSensitiveValue(rawText);
    }

    public static string MaskFinancialFreeText(string kind, string json)
    {
        if (kind is not ("expense-receipt" or "purchase-invoice" or "sales-invoice"))
            return json;
        var root = JsonNode.Parse(json);
        if (root is not JsonObject fields)
            return json;
        MaskProperty(fields, "notes");
        if (fields["lines"] is JsonArray lines)
            foreach (var line in lines.OfType<JsonObject>())
                MaskProperty(line, "description");
        return root.ToJsonString();
    }

    private static void MaskProperty(JsonObject value, string name)
    {
        if (value[name] is JsonValue node && node.TryGetValue<string>(out var text) && text is not null)
            value[name] = MaskSensitiveValue(text);
    }

    public static string MaskSensitiveValue(string value)
    {
        var maskedIbans = IbanRegex().Replace(value, "••••");
        return AccountNumberRegex().Replace(maskedIbans, match => IsPlausibleDate(match.Value) ? match.Value : "••••");
    }

    private static bool IsPlausibleDate(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        return date.Year is >= 1990 and <= 2100;
    }

    [GeneratedRegex(@"(?<![A-Z0-9])[A-Z]{2}[ -]?[0-9]{2}(?:[ -]?[A-Z0-9]){11,30}(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<![0-9])[0-9](?:[ -]?[0-9]){7,16}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberRegex();
}
