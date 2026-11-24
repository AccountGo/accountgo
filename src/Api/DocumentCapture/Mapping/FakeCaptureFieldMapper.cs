// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// Development-only mapper for exercising the AI-assisted review locally. It changes the vendor or customer name to a longer name
/// built only from words printed in the text and, when the text has one, fills a missing document number. It makes no network call.
/// Registered only in Development and tests, like <see cref="FakeDocumentFieldExtractor"/>.
/// </summary>
public sealed partial class FakeCaptureFieldMapper : IVersionedCaptureFieldMapper
{
    public string Name => "fake";
    public string Version => "1";

    public Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
    {
        var fields = (JsonNode.Parse(request.RuleFieldsJson) as JsonObject) ?? new JsonObject();
        var partyField = request.Kind == "sales-invoice" ? "customerName" : "vendorName";
        var numberField = request.Kind == "sales-invoice" ? "sourceInvoiceNo" : "vendorInvoiceNo";

        if (fields[numberField] is null)
        {
            var match = request.Lines.Select(line => InvoiceNumberRegex().Match(line.Text)).FirstOrDefault(m => m.Success);
            if (match is not null)
                fields[numberField] = match.Groups[1].Value;
        }

        if (fields[partyField]?.GetValue<string>() is { Length: > 0 } name && request.Lines.Count > 0)
        {
            // Rebuild the name from the first printed line that starts with it, which is a name the document really prints.
            var line = request.Lines.Select(l => l.Text.Trim()).FirstOrDefault(t => t.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (line is not null && !string.Equals(line, name, StringComparison.Ordinal) && line.Length <= 120)
                fields[partyField] = line;
        }

        return Task.FromResult(new CaptureMappingResult(fields.ToJsonString(), Name, Version, Array.Empty<string>()));
    }

    [GeneratedRegex(@"(?:invoice|receipt|bill)\s*(?:no\.?|number|#)\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9\-/]{2,30})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex InvoiceNumberRegex();
}
