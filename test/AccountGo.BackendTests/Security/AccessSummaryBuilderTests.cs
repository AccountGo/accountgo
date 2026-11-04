// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Service;
using Xunit;

namespace AccountGo.BackendTests.Security
{
    public sealed class AccessSummaryBuilderTests
    {
        [Fact]
        public void Describe_ReportsStrongestLevelPerArea()
        {
            var areas = AccessSummaryBuilder.Describe(new[]
            {
                "sales.invoices.view", "sales.invoices.create",
                "financials.journal.view", "financials.journal.post",
                "reports.view",
            });

            Assert.Equal(AccessSummaryBuilder.CreateAndEditLevel, areas.Single(a => a.Area == "sales").Level);
            Assert.Equal(AccessSummaryBuilder.ManageAndPostLevel, areas.Single(a => a.Area == "financials").Level);
            Assert.Equal(AccessSummaryBuilder.ViewOnlyLevel, areas.Single(a => a.Area == "reports").Level);
        }

        [Fact]
        public void Describe_TreatsReconcileAsManage_AndUsesFriendlyLabels()
        {
            var areas = AccessSummaryBuilder.Describe(new[] { "banking.reconcile", "banking.reconcile.undo" });

            var banking = Assert.Single(areas);
            Assert.Equal("Banking", banking.Label);
            Assert.Equal(AccessSummaryBuilder.ManageAndPostLevel, banking.Level);
        }

        [Fact]
        public void Describe_IgnoresBlankNames_AndRejectsNull()
        {
            Assert.Empty(AccessSummaryBuilder.Describe(new[] { "", "  " }));
            Assert.Throws<ArgumentNullException>(() => AccessSummaryBuilder.Describe(null!));
        }
    }
}
