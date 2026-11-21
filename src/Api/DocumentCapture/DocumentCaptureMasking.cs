// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;

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

    public static string MaskSensitiveValue(string value)
    {
        var maskedIbans = IbanRegex().Replace(value, match => MaskAllButLastFour(match.Value));
        return AccountNumberRegex().Replace(maskedIbans, match =>
        {
            var chars = match.Value.ToCharArray();
            var remainingDigits = Math.Min(4, chars.Count(char.IsAsciiDigit));
            for (var i = chars.Length - 1; i >= 0; i--)
            {
                if (!char.IsAsciiDigit(chars[i]))
                    continue;
                if (remainingDigits > 0)
                    remainingDigits--;
                else
                    chars[i] = '*';
            }
            return new string(chars);
        });
    }

    private static string MaskAllButLastFour(string value)
    {
        var chars = value.ToCharArray();
        var remaining = 4;
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]))
                continue;
            if (remaining > 0)
                remaining--;
            else
                chars[i] = '*';
        }
        return new string(chars);
    }

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?:[ -]?[A-Z0-9]){11,30}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<![A-Z0-9])[0-9](?:[ -]?[0-9]){8,17}(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberRegex();
}
