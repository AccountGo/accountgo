// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccountGo.BackendTests.Architecture;

/// <summary>
/// Fails the public build when product packaging strings, list prices, or private
/// module identifiers leak into source and docs that ship with the open repository.
/// Historical migrations and the legacy schema upgrader are allowlisted explicitly.
/// </summary>
public class PlanPackagingLeakGuardTests
{
    // Private module / assembly type names that must not appear in public src/.
    private static readonly string[] ForbiddenPrivateNames =
    [
        "accountgo-commercial",
        "HostedEntitlementService",
        "HostedPlanCatalog",
        "HostedPlanCatalogProvider",
        "HostedPlanRules",
        "HostedEntitlementSettings",
        "AccountGo.Billing",
    ];

    private static readonly (string RelativePath, string Reason)[] Allowlist =
    [
        ("src/Api/Data/Migrations/", "Historical EF migrations keep prior seed/plan values."),
        ("src/Api/d1/migrations/", "Historical D1 migrations keep prior seed/plan values."),
        ("src/Api/Data/Schema/SqliteSchemaUpgrader.cs", "Legacy plan-key rewrites for databases upgraded in place."),
    ];

    private static readonly Regex PlanTieruffix = new(
        @"\b(invoicing|accounting)_(free|pro)\b",
        RegexOptions.Compiled);

    private static readonly Regex MonthlyPriceLiteral = new(
        @"(?i)\bmonthlyPrice\s*=\s*-?\d+(\.\d+)?\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist",
    };

    [Fact]
    public void PublicTree_HasNoPlanPackagingLeaks()
    {
        var root = FindSolutionRoot();
        var failures = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsAllowlisted(relative))
                continue;
            if (relative.EndsWith("worker-configuration.d.ts", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var location = $"{relative}:{i + 1}";

                if (PlanTieruffix.IsMatch(line))
                    failures.Add($"{location}: plan tier key");
                if (MonthlyPriceLiteral.IsMatch(line))
                    failures.Add($"{location}: monthlyPrice numeric literal");
                foreach (var name in ForbiddenPrivateNames)
                {
                    if (line.Contains(name, StringComparison.Ordinal))
                        failures.Add($"{location}: forbidden name '{name}'");
                }
            }
        }

        foreach (var file in EnumerateDocFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsAllowlisted(relative))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var location = $"{relative}:{i + 1}";
                if (line.Contains("accountgo-commercial", StringComparison.Ordinal))
                    failures.Add($"{location}: forbidden name 'accountgo-commercial'");
                if (line.Contains("ENTITLEMENTS_FREE_", StringComparison.Ordinal))
                    failures.Add($"{location}: ENTITLEMENTS_FREE_");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Plan packaging leak guard failed:\n" + string.Join("\n", failures.Take(50))
            + (failures.Count > 50 ? $"\n... and {failures.Count - 50} more" : ""));
    }

    private static bool IsAllowlisted(string relativePath) =>
        Allowlist.Any(entry =>
            relativePath.Equals(entry.RelativePath.TrimEnd('/'), StringComparison.Ordinal)
            || relativePath.StartsWith(entry.RelativePath, StringComparison.Ordinal));

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var srcRoot = Path.Combine(root, "src");
        if (!Directory.Exists(srcRoot))
            yield break;

        var stack = new Stack<string>();
        stack.Push(srcRoot);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                if (SkipDirNames.Contains(Path.GetFileName(child)))
                    continue;
                stack.Push(child);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateDocFiles(string root)
    {
        var docs = Path.Combine(root, "docs");
        if (Directory.Exists(docs))
        {
            foreach (var file in Directory.EnumerateFiles(docs, "*", SearchOption.AllDirectories))
                yield return file;
        }

        var readme = Path.Combine(root, "README.md");
        if (File.Exists(readme))
            yield return readme;
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AccountGo.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
