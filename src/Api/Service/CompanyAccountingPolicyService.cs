using System;
using System.Linq;
using Api.Data;

namespace Api.Service
{
    /// <summary>Change-control helpers for company accounting policy.</summary>
    public class CompanyAccountingPolicyService
    {
        private readonly ApiDbContext _db;

        public CompanyAccountingPolicyService(ApiDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>True once any general ledger journal exists for the company (queried inside the resolved tenant scope); policy fields freeze from then on. Not enforced until Phase 4.</summary>
        public bool IsLedgerFrozen(int companyId)
        {
            return _db.GeneralLedgerHeaders.Any(h => h.CompanyId == companyId);
        }
    }
}
