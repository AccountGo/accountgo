// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using Api.Filters;
using Microsoft.AspNetCore.Http;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class ReconciliationConflictCultureTests
{
    [Fact]
    public async Task ReconciledConflict_UsesGregorianDatesUnderThaiCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var exception = new ReconciledTransactionException("Checking", new DateTime(2026, 11, 30));
            Assert.Contains("2026-11-30", exception.Message);
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            await new MultiTenancyExceptionHandler().TryHandleAsync(context, exception, default);
            context.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("2026-11-30", body.RootElement.GetProperty("statementEndDate").GetString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
