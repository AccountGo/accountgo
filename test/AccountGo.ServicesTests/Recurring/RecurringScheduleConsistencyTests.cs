// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Api.Data;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests.Recurring
{
    /// <summary>
    /// #479: SaveChanges must reject recurring rows whose foreign keys cross a tenant boundary.
    /// A matching foreign key only proves the id exists; the id must also belong to the row's company.
    /// These tests put the referenced row in company B and the recurring row in company A, so the
    /// tenant query filter would normally hide B's row; the consistency hook must still fail closed.
    /// </summary>
    public class RecurringScheduleConsistencyTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ApiDbContext _db;
        private readonly TenantContext _tenant = new();
        private readonly Company _companyA;
        private readonly Company _companyB;

        public RecurringScheduleConsistencyTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ApiDbContext(
                new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options,
                tenantContext: _tenant);
            _db.Database.EnsureCreated();

            _companyA = new Company { Name = "Company A", Slug = "company-a" };
            _companyB = new Company { Name = "Company B", Slug = "company-b" };
            _db.Companies.Add(_companyA);
            _db.Companies.Add(_companyB);
            _db.SaveChanges();

            ActAs(_companyA);
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private void ActAs(Company company) =>
            _tenant.SetTenant(company.Id, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

        private Customer AddCustomer(Company owner)
        {
            using (_tenant.Impersonate(owner.Id, owner.Slug))
            {
                var customer = new Customer { CompanyId = owner.Id, No = Guid.NewGuid().ToString("N") };
                _db.Customers.Add(customer);
                _db.SaveChanges();
                return customer;
            }
        }

        private Vendor AddVendor(Company owner)
        {
            using (_tenant.Impersonate(owner.Id, owner.Slug))
            {
                var vendor = new Vendor { CompanyId = owner.Id, No = Guid.NewGuid().ToString("N") };
                _db.Vendors.Add(vendor);
                _db.SaveChanges();
                return vendor;
            }
        }

        private PaymentTerm AddPaymentTerm(Company owner)
        {
            using (_tenant.Impersonate(owner.Id, owner.Slug))
            {
                var paymentTerm = new PaymentTerm
                {
                    CompanyId = owner.Id,
                    Description = "Net 30",
                    PaymentType = PaymentTypes.AfterNoOfDays,
                    DueAfterDays = 30,
                    IsActive = true,
                };
                _db.PaymentTerms.Add(paymentTerm);
                _db.SaveChanges();
                return paymentTerm;
            }
        }

        private RecurringSchedule AddSchedule(
            Company owner,
            int partyId,
            RecurringScheduleKind kind,
            int? paymentTermId = null)
        {
            var schedule = new RecurringSchedule
            {
                CompanyId = owner.Id,
                Kind = kind,
                PartyId = partyId,
                Name = "Retainer",
                Cadence = RecurringCadence.Monthly,
                Interval = 1,
                DayOfMonth = 1,
                StartDate = new DateTime(2026, 1, 1),
                PaymentTermId = paymentTermId,
            };
            _db.RecurringSchedules.Add(schedule);
            _db.SaveChanges();
            return schedule;
        }

        [Fact]
        public void Schedule_PartyInSameCompany_Saves()
        {
            var customer = AddCustomer(_companyA);

            var ex = Record.Exception(() => AddSchedule(_companyA, customer.Id, RecurringScheduleKind.SalesInvoice));

            Assert.Null(ex);
        }

        [Fact]
        public void Schedule_SalesPartyFromOtherCompany_Throws()
        {
            // Customer belongs to B; schedule is created while acting as A.
            var foreignCustomer = AddCustomer(_companyB);
            ActAs(_companyA);

            Assert.Throws<CrossTenantWriteException>(() =>
                AddSchedule(_companyA, foreignCustomer.Id, RecurringScheduleKind.SalesInvoice));
        }

        [Fact]
        public void Schedule_VendorPartyFromOtherCompany_Throws()
        {
            var foreignVendor = AddVendor(_companyB);
            ActAs(_companyA);

            Assert.Throws<CrossTenantWriteException>(() =>
                AddSchedule(_companyA, foreignVendor.Id, RecurringScheduleKind.VendorBill));
        }

        [Fact]
        public void Schedule_PartyIdNotFound_Throws()
        {
            // No customer with this id exists anywhere: fail closed rather than trust the id.
            Assert.Throws<CrossTenantWriteException>(() =>
                AddSchedule(_companyA, 999999, RecurringScheduleKind.SalesInvoice));
        }

        [Fact]
        public void Schedule_PaymentTermFromOtherCompany_Throws()
        {
            var customer = AddCustomer(_companyA);
            var foreignPaymentTerm = AddPaymentTerm(_companyB);
            ActAs(_companyA);

            Assert.Throws<CrossTenantWriteException>(() =>
                AddSchedule(
                    _companyA,
                    customer.Id,
                    RecurringScheduleKind.SalesInvoice,
                    foreignPaymentTerm.Id));
        }

        [Fact]
        public void Schedule_PaymentTermFromSameCompany_Saves()
        {
            var customer = AddCustomer(_companyA);
            var paymentTerm = AddPaymentTerm(_companyA);

            var ex = Record.Exception(() =>
                AddSchedule(
                    _companyA,
                    customer.Id,
                    RecurringScheduleKind.SalesInvoice,
                    paymentTerm.Id));

            Assert.Null(ex);
        }

        [Fact]
        public void Schedule_PaymentTermIdNotFound_Throws()
        {
            var customer = AddCustomer(_companyA);

            Assert.Throws<CrossTenantWriteException>(() =>
                AddSchedule(_companyA, customer.Id, RecurringScheduleKind.SalesInvoice, 999999));
        }

        [Fact]
        public void Line_CompanyIdMatchesScheduleAndNoForeignRefs_Saves()
        {
            var customer = AddCustomer(_companyA);
            var schedule = AddSchedule(_companyA, customer.Id, RecurringScheduleKind.SalesInvoice);

            var line = new RecurringScheduleLine
            {
                CompanyId = _companyA.Id,
                RecurringScheduleId = schedule.Id,
                Description = "Service",
                Quantity = 1,
                Amount = 100,
            };
            _db.RecurringScheduleLines.Add(line);

            var ex = Record.Exception(() => _db.SaveChanges());

            Assert.Null(ex);
        }

        [Fact]
        public void Occurrence_SalesInvoiceFromOtherCompany_Throws()
        {
            var customer = AddCustomer(_companyA);
            var schedule = AddSchedule(_companyA, customer.Id, RecurringScheduleKind.SalesInvoice);

            // A sales invoice header owned by company B.
            SalesInvoiceHeader foreignInvoice;
            using (_tenant.Impersonate(_companyB.Id, _companyB.Slug))
            {
                var customerB = new Customer { CompanyId = _companyB.Id, No = Guid.NewGuid().ToString("N") };
                _db.Customers.Add(customerB);
                _db.SaveChanges();

                foreignInvoice = new SalesInvoiceHeader { CompanyId = _companyB.Id, CustomerId = customerB.Id };
                _db.SalesInvoiceHeaders.Add(foreignInvoice);
                _db.SaveChanges();
            }
            ActAs(_companyA);

            var occurrence = new RecurringOccurrence
            {
                CompanyId = _companyA.Id,
                RecurringScheduleId = schedule.Id,
                ScheduledRunDate = new DateTime(2026, 1, 1),
                PeriodStart = new DateTime(2026, 1, 1),
                PeriodEnd = new DateTime(2026, 1, 31),
                DocumentDate = new DateTime(2026, 1, 1),
                IdempotencyKey = $"{_companyA.Id}-{schedule.Id}-2026-01-01",
                SalesInvoiceId = foreignInvoice.Id,
            };
            _db.RecurringOccurrences.Add(occurrence);

            Assert.Throws<CrossTenantWriteException>(() => _db.SaveChanges());
        }

        [Fact]
        public void Line_CompanyIdDiffersFromSchedule_Throws()
        {
            var customer = AddCustomer(_companyA);
            var schedule = AddSchedule(_companyA, customer.Id, RecurringScheduleKind.SalesInvoice);

            // Line claims company A (so the tenant guard passes) but parent schedule is A too;
            // force a mismatch by pointing the line's CompanyId at B while the schedule is A.
            var line = new RecurringScheduleLine
            {
                CompanyId = _companyB.Id,
                RecurringScheduleId = schedule.Id,
                Description = "Service",
                Quantity = 1,
                Amount = 100,
            };
            _db.RecurringScheduleLines.Add(line);

            // The tenant guard rejects the B-owned line while acting as A; either way the write fails.
            Assert.Throws<CrossTenantWriteException>(() => _db.SaveChanges());
        }

        [Fact]
        public void TenantTables_AllNamesExistInEfSchema()
        {
            // The SQLite upgrader's TenantTables list and the schema it touches must use the same
            // table names EF materializes. A stale singular name (e.g. "RecurringSchedule") would
            // point the tenancy upgrade at a table that is never created.
            var efTables = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    efTables.Add(reader.GetString(0));
            }

            foreach (var table in Api.Data.Schema.SqliteSchemaUpgrader.TenantTables)
            {
                Assert.True(efTables.Contains(table),
                    $"TenantTables lists '{table}', but EF creates no such table. The name is stale or misspelled.");
            }
        }

        [Fact]
        public void Upgrader_CreatesPluralRecurringTables()
        {
            // Drop the EF-created recurring tables, then let the upgrader recreate them.
            // The upgrader must create the exact plural names EF queries.
            foreach (var table in new[] { "RecurringOccurrences", "RecurringScheduleLines", "RecurringSchedules" })
            {
                using var drop = _connection.CreateCommand();
                drop.CommandText = $"DROP TABLE IF EXISTS \"{table}\";";
                drop.ExecuteNonQuery();
            }

            Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(_connection);

            foreach (var table in new[] { "RecurringSchedules", "RecurringScheduleLines", "RecurringOccurrences" })
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                var p = cmd.CreateParameter();
                p.ParameterName = "$name";
                p.Value = table;
                cmd.Parameters.Add(p);
                Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
            }
        }
    }
}
