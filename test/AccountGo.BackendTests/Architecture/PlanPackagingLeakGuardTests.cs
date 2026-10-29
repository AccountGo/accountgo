// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    // Digests of identifiers that must not appear in this repository.
    private static readonly HashSet<string> ForbiddenTokenDigests = new(StringComparer.Ordinal)
    {
        "32c43f1d941c0d8d433532e7ac94c83cd6e1c3b344ccd8d11cc4d02e8e912e74",
        "31ccc0849300e18f5135a2f4514a7715e3c653329f394ffc2e3ae9972688c5a8",
        "bf3de4165adb56391ebed4839574c9e970e7c7b1f209ab144bca9ff4e6319af7",
        "d7a8886f27cedee53a6d2e9a102bc1323f6d7aa9dc50cfd0a2d9cc45e4313c46",
        "fafca1dbf2c395bd2cb90f0a7d44ea34536ec925c5a3733faddc43dbfb1506f5",
        "c41e1ee4ae09d274d4aa6cc77f9cec11c93e3829cdb293bb2ed092e62af5e8f2",
        "ae2ab7a70ea537678d645e6dbdffcf90dd4ea909b4068c2dc836436acce096c9",
        "700c8cb3230d65a8911bc384ebcdf0b353a1f373cbadb84058b9ccabda355c6f",
        "14386bdbe24db460d82cc6c567cc537d1b674072786e7d78c1d530e507a39ca2",
        "71edb6b16ed1f07828588321ed37faf9978586eac232b5671038e008509a242f",
        "41bf3b5484dc570deaae1fabb343067aa7cbf444111056812368ebafe2c3270a",
    };

    private static readonly (string RelativePath, string Reason)[] Allowlist =
    [
        ("src/Api/Data/Migrations/", "Historical EF migrations keep prior seed/plan values."),
        ("src/Api/d1/migrations/", "Historical D1 migrations keep prior seed/plan values."),
        ("src/Api/Data/Schema/SqliteSchemaUpgrader.cs", "Legacy plan-key rewrites for databases upgraded in place."),
        ("src/Api/Data/Seed/SeeData.cs", "EF seed must match the migration snapshot."),
    ];

    private static readonly Regex TokenPattern = new(
        @"[A-Za-z][A-Za-z0-9_.-]*",
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

                if (MonthlyPriceLiteral.IsMatch(line))
                    failures.Add($"{location}: monthlyPrice numeric literal");

                foreach (Match match in TokenPattern.Matches(line))
                {
                    if (ForbiddenTokenDigests.Contains(Sha256Hex(match.Value.ToLowerInvariant())))
                        failures.Add($"{location}: forbidden token");
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
                if (line.Contains("ENTITLEMENTS_FREE_", StringComparison.Ordinal))
                    failures.Add($"{location}: ENTITLEMENTS_FREE_");

                foreach (Match match in TokenPattern.Matches(line))
                {
                    if (ForbiddenTokenDigests.Contains(Sha256Hex(match.Value.ToLowerInvariant())))
                        failures.Add($"{location}: forbidden token");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Plan packaging leak guard failed:\n" + string.Join("\n", failures.Take(50))
            + (failures.Count > 50 ? $"\n... and {failures.Count - 50} more" : ""));
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
                yield return file;
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
