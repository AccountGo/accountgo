// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Reflection;
using System.Text.RegularExpressions;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Parsing;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureRulesRegisterTests
{
    private static readonly Regex RuleIdPattern = new(@"^[A-Z]+-\d{2}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal) { "active", "changed", "retired" };

    [Fact]
    public void ProviderVersion_MatchesExpectedFormatAndStaysWithinLengthLimit()
    {
        Assert.Equal("rules-1", DocumentTextFieldParser.RulesVersion);
        Assert.Equal(
            CaptureWorkerModels.Version + "+" + DocumentTextFieldParser.RulesVersion,
            LocalOcrDocumentFieldExtractor.CurrentProviderVersion);
        Assert.Equal("ocr-1+d5de5df3+20b69451+rules-1", LocalOcrDocumentFieldExtractor.CurrentProviderVersion);
        Assert.True(
            LocalOcrDocumentFieldExtractor.CurrentProviderVersion.Length <= 100,
            $"ProviderVersion '{LocalOcrDocumentFieldExtractor.CurrentProviderVersion}' exceeds 100 characters.");
    }

    [Fact]
    public void Register_IsConsistentWithRuleTraitsInTests()
    {
        var registerPath = Path.Combine(FindRepositoryRoot(), "docs", "system-architecture", "DOCUMENT_CAPTURE_RULES.md");
        Assert.True(File.Exists(registerPath), $"Rules register not found at {registerPath}");

        var registerText = File.ReadAllText(registerPath);
        Assert.Contains($"`{DocumentTextFieldParser.RulesVersion}`", registerText, StringComparison.Ordinal);

        var rows = ParseRegisterRows(registerText);
        Assert.NotEmpty(rows);

        // 1. Unique IDs
        var duplicateIds = rows
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicateIds.Count == 0, $"Duplicate rule IDs in register: {string.Join(", ", duplicateIds)}");

        // 2. Valid statuses
        var invalidStatuses = rows
            .Where(r => !ValidStatuses.Contains(r.Status))
            .Select(r => $"{r.Id}={r.Status}")
            .ToList();
        Assert.True(invalidStatuses.Count == 0, $"Invalid rule statuses in register: {string.Join(", ", invalidStatuses)}");

        // 3. Collect all [Trait("Rule", "<ID>")] across the test assembly via reflection
        var traitMap = CollectRuleTraits(typeof(DocumentCaptureRulesRegisterTests).Assembly);
        var registeredIds = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // 4. Every active ID must have at least one test with that Rule trait
        var untestedActive = rows
            .Where(r => r.Status == "active" && !traitMap.ContainsKey(r.Id))
            .Select(r => r.Id)
            .ToList();
        Assert.True(
            untestedActive.Count == 0,
            $"Active rule(s) in register missing a [Trait(\"Rule\", \"<ID>\")] test: {string.Join(", ", untestedActive)}");

        // 5. Every Rule trait in tests must exist in the register
        var unregisteredTraits = traitMap.Keys
            .Where(id => !registeredIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            unregisteredTraits.Count == 0,
            $"Test(s) reference Rule trait(s) not present in register: {string.Join(", ", unregisteredTraits)}");
    }

    private static List<(string Id, string Status)> ParseRegisterRows(string markdown)
    {
        var results = new List<(string Id, string Status)>();
        var inRegisterSection = false;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inRegisterSection = line.Equals("## Rules register", StringComparison.Ordinal);
                continue;
            }

            if (!inRegisterSection || !line.StartsWith('|'))
                continue;

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 7)
                continue;

            var id = cells[0].Trim('`', ' ');
            if (id.Equals("ID", StringComparison.OrdinalIgnoreCase) || id.All(ch => ch == '-'))
                continue;

            if (!RuleIdPattern.IsMatch(id))
                Assert.Fail($"Unexpected rule ID format '{id}' in register row: {line}");

            var status = cells[6].Trim('`', ' ');
            results.Add((id, status));
        }

        return results;
    }

    private static Dictionary<string, List<string>> CollectRuleTraits(Assembly assembly)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void RecordTraits(MemberInfo member, string label)
        {
            foreach (var attr in CustomAttributeData.GetCustomAttributes(member))
            {
                if (attr.AttributeType != typeof(TraitAttribute) || attr.ConstructorArguments.Count != 2)
                    continue;

                var name = attr.ConstructorArguments[0].Value as string;
                var value = attr.ConstructorArguments[1].Value as string;
                if (!string.Equals(name, "Rule", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (!map.TryGetValue(value, out var list))
                {
                    list = [];
                    map[value] = list;
                }
                list.Add(label);
            }
        }

        foreach (var type in assembly.GetTypes())
        {
            RecordTraits(type, type.FullName ?? type.Name);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                RecordTraits(method, $"{type.Name}.{method.Name}");
            }
        }

        return map;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
