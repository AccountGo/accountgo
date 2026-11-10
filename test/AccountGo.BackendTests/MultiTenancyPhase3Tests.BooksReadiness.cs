#nullable disable
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        private static readonly string[] ModulesWithoutFinancials = { "sales", "purchases", "reports" };
        private static readonly string[] ModulesWithFinancials = { "sales", "purchases", "reports", "financials" };

        [Fact]
        public async Task BooksCheck_TurningOnFinancials_WithUnpostedInvoice_Returns409_UntilPosted()
        {
            var draftId = await SaveDraft();
            await SaveDraft(); // drafts never count
            await Issue(draftId);
            UnlinkInvoicePosting(draftId);

            var off = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithoutFinancials });
            Assert.True(off.IsSuccessStatusCode, await off.Content.ReadAsStringAsync());

            var blocked = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithFinancials });
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await Json(blocked);
            Assert.Equal("BOOKS_NOT_READY", problem.GetProperty("code").GetString());
            var unposted = problem.GetProperty("unpostedInvoices").EnumerateArray().ToList();
            Assert.Single(unposted);
            Assert.Equal(draftId, unposted[0].GetProperty("id").GetInt32());
            Assert.Empty(problem.GetProperty("unpostedBills").EnumerateArray());

            var post = await Send(HttpMethod.Post, "/api/Sales/PostUnpostedInvoices");
            Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());
            var posted = await Json(post);
            Assert.True(posted.GetProperty("isReady").GetBoolean(), posted.GetRawText());
            Assert.True(posted.GetProperty("results")[0].GetProperty("posted").GetBoolean());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var invoice = db.SalesInvoiceHeaders.IgnoreQueryFilters().Single(i => i.Id == draftId);
                Assert.NotNull(invoice.GeneralLedgerHeaderId);
            }

            var on = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithFinancials });
            Assert.True(on.IsSuccessStatusCode, await on.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task BooksCheck_DoesNotBlock_WhenFinancialsAlreadyOn_OrBooksComplete()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);

            // Financials is already on: unrelated module changes never run the check.
            UnlinkInvoicePosting(draftId);
            var keep = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithFinancials });
            Assert.True(keep.IsSuccessStatusCode, await keep.Content.ReadAsStringAsync());

            // Complete books: off then on succeeds.
            var post = await Send(HttpMethod.Post, "/api/Sales/PostUnpostedInvoices");
            Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());
            var off = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithoutFinancials });
            Assert.True(off.IsSuccessStatusCode, await off.Content.ReadAsStringAsync());
            var on = await Send(HttpMethod.Put, "/api/Subscription/update", new { activeModules = ModulesWithFinancials });
            Assert.True(on.IsSuccessStatusCode, await on.Content.ReadAsStringAsync());
        }

        /// <summary>Simulates an invoice whose posting failed when it was issued.</summary>
        private void UnlinkInvoicePosting(int invoiceId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var invoice = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                invoice.GeneralLedgerHeaderId = null;
                db.SaveChanges();
            }
        }
    }
}
