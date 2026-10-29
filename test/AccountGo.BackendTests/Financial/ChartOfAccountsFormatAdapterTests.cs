#nullable disable
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Service.Import;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class ChartOfAccountsFormatAdapterTests : IClassFixture<ChartOfAccountsImportTests.Fixture>
    {
        private readonly ChartOfAccountsImportTests.Fixture _fixture;

        public ChartOfAccountsFormatAdapterTests(ChartOfAccountsImportTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        private static async Task<JsonElement> Json(HttpResponseMessage res)
        {
            var text = await res.Content.ReadAsStringAsync();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        private static ICoaFormatAdapterRegistry CreateRegistry()
        {
            return new CoaFormatAdapterRegistry(new ICoaFormatAdapter[]
            {
                new AccountGoV1CoaFormatAdapter(),
                new XeroCoaFormatAdapter(),
                new QboCoaFormatAdapter()
            });
        }

        [Fact]
        public void AccountGoV1_MapsCanonicalRows()
        {
            var csv = "# AccountGo-COA-v1\nAccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status\n10111,Checking,,Assets,FALSE,TRUE,Operating cash,Active";
            var result = CreateRegistry().Adapt(csv);
            Assert.True(result.Success);
            Assert.Equal("accountgo-v1", result.FormatKey);
            Assert.Single(result.Rows);
            Assert.Equal("10111", result.Rows[0].AccountCode);
            Assert.Equal(1, result.Rows[0].AccountClassId);
            Assert.True(result.Rows[0].IsCash);
        }

        [Fact]
        public void SnapshotMarker_IsRejectedWithConversionGuidance()
        {
            var csv = "# AccountGo-COA-Snapshot-v1\nAccountCode,AccountName,AccountClass,Balance\n10111,Checking,Assets,10";
            var result = CreateRegistry().Adapt(csv);
            Assert.False(result.Success);
            Assert.Contains(result.FileErrors, e => e.Contains("conversion journal") && e.Contains("Snapshot"));
        }

        [Fact]
        public void XeroSignature_ReturnsAdapterNotInstalled()
        {
            var csv = "*Code,*Name,*Type,*Tax Code\n200,Sales,Revenue,Tax on Sales";
            var result = CreateRegistry().Adapt(csv);
            Assert.False(result.Success);
            Assert.Equal("xero", result.FormatKey);
            Assert.Contains(result.FileErrors, e => e.Contains("not installed") && e.Contains("format-adapters.md"));
        }

        [Fact]
        public void QboSignature_ReturnsAdapterNotInstalled()
        {
            var csv = "Account Name,Type,Detail Type,Account Number\nChecking,Bank,Checking,10111";
            var result = CreateRegistry().Adapt(csv);
            Assert.False(result.Success);
            Assert.Equal("qbo", result.FormatKey);
            Assert.Contains(result.FileErrors, e => e.Contains("not installed") && e.Contains("QuickBooks"));
        }

        [Fact]
        public void UnknownFormat_IsRejected()
        {
            var csv = "Foo,Bar,Baz\n1,2,3";
            var result = CreateRegistry().Adapt(csv);
            Assert.False(result.Success);
            Assert.Contains(result.FileErrors, e => e.Contains("Unrecognized") || e.Contains("Unsupported") || e.Contains("Expected"));
        }

        [Fact]
        public async Task ValidateApi_RejectsXeroStub_WithDocumentationPointer()
        {
            var owner = await _fixture.CreateUser("xero-stub-owner@example.com");
            await _fixture.CreateWorkspace(owner.Token, "Xero Stub", "xero-stub");

            var csv = "*Account Code,*Account Name,*Account Type\n400,Sales,Revenue";
            var res = await _fixture.SendJson(HttpMethod.Post, "/api/Financials/ImportAccounts/Validate?mode=append",
                new { csvContent = csv, mode = "append" }, owner.Token, "xero-stub");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var preview = await Json(res);
            Assert.True(preview.GetProperty("hasErrors").GetBoolean());
            var errors = preview.GetProperty("fileErrors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errors, e => e != null && e.Contains("not installed") && e.Contains("format-adapters.md"));
        }
    }
}
