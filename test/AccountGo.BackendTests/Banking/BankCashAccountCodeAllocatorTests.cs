// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Core.Domain;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankCashAccountCodeAllocatorTests
{
    [Fact]
    public void Allocate_Prefers10114Through10119()
    {
        var code = BankCashAccountCodeAllocator.Allocate(new[] { "10111", "10112", "10113" }, CoaTemplates.Legacy);
        Assert.Equal("10114", code);
    }

    [Fact]
    public void Allocate_SkipsCodesTheTemplateReserves()
    {
        // Prefereds exhausted; scan from 10100 skips reserved base-chart codes (10100, 10110, 10120, …).
        var used = new HashSet<string> { "10114", "10115", "10116", "10117", "10118", "10119" };
        var code = BankCashAccountCodeAllocator.Allocate(used, CoaTemplates.Legacy);
        Assert.Equal("10101", code);
        Assert.DoesNotContain(code, CoaTemplates.ReservedCashRangeAccountCodes(CoaTemplates.Legacy));

        used.Add(code);
        // Fill gaps through 10119 so the next candidate would be 10120 (AR) if we did not skip reserved.
        for (var c = 10101; c <= 10119; c++)
            used.Add(c.ToString());
        var afterReserved = BankCashAccountCodeAllocator.Allocate(used, CoaTemplates.Legacy);
        Assert.Equal("10122", afterReserved);
        Assert.DoesNotContain("10120", new[] { afterReserved });
        Assert.DoesNotContain("10121", new[] { afterReserved });
    }

    [Fact]
    public void Allocate_ServicesTemplate_CanUseOmitted10900()
    {
        var used = new HashSet<string>();
        for (var c = 10114; c <= 10119; c++)
            used.Add(c.ToString());
        // Fill every non-reserved gap below 10900 so the services-omitted code is next.
        for (var c = 10100; c < 10900; c++)
        {
            var text = c.ToString();
            if (!CoaTemplates.ReservedCashRangeAccountCodes(CoaTemplates.ServicesSme).Contains(text))
                used.Add(text);
        }

        var code = BankCashAccountCodeAllocator.Allocate(used, CoaTemplates.ServicesSme);
        Assert.Equal("10900", code);
    }
}
