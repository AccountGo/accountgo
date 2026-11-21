# Prefixed Public IDs Reference

This document is the authoritative inventory for prefixed public IDs (`{prefix}_{26-char-body}`) across AccountGo.

- **Format:** `^[a-z]{2,5}_[0-9a-z]{26}$`
  - New rows: lowercase Crockford base32 ULID (48-bit Unix ms + 80 random bits).
  - Backfilled rows: 26 lowercase hex characters (104 random bits).
- **Storage:** `PublicId` column (`nvarchar`/`text`, max length 32, `NOT NULL DEFAULT ''`) with a unique filtered index `WHERE "PublicId" <> ''`.
- **Boundary:** Domain entities, services, and C# DTO properties stay `int`; translation happens at the HTTP/MCP/Web boundary via `[PublicId("prefix")]` and `[PublicIdByType("TypeProperty")]`.

---

## 1. Prefix Registry

Every entity in `ApiDbContext` and `ApplicationIdentityDbContext` that crosses the wire has a permanent prefix below. All `check` rows from the design have been resolved against controllers, DTOs, services, and MCP tools.

### Core / ApiDbContext

| Entity | Prefix | In Scope | Wire Evidence / Notes |
|---|---|---|---|
| `Company` | `ws` | **yes** | `OrganizationsController`, `AdministrationController`, `AuthController`, `SubscriptionController` |
| `CompanyMember` | `mem` | **yes** | `WorkspaceMembersController`, `OrganizationsController` |
| `CompanyInvitation` | `winv` | **yes** | `WorkspaceInvitationsController` (`{id}/resend`, `DELETE {id}`, list), `InvitationsController` (`pending/{id}/accept`, `Pending()`) |
| `SecurityRole` | `role` | **yes** | `WorkspaceMembersController`, `AdministrationController`, `Dto.Security.Role` |
| `WorkspaceApprovalRule` | `aprl` | **no** | Queried internally by `OperationKey`; `Id` never crosses the wire |
| `Account` | `acct` | **yes** | `AccountController`, `FinancialsController`, `Dto.Financial.Account` |
| `FinancialYear` | `fy` | **yes** | `FinancialsController`, `AdministrationController`, `Dto.Financial.FiscalYearSummaryDto` |
| `JournalEntryHeader` | `je` | **yes** | `FinancialsController`, `Dto.Financial.JournalEntry` |
| `JournalEntryLine` | `jel` | **yes** | `Dto.Financial.JournalEntryLine` |
| `GeneralLedgerHeader` | `gl` | **yes** | `FinancialsController`, `BankingController`, `Dto.Financial.GeneralLedgerTransaction` |
| `GeneralLedgerLine` | `gll` | **yes** | `BankingController`, `Dto.Financial.GeneralLedgerTransactionLine`, `Dto.Banking.*` |
| `MainContraAccount` | `mca` | **no** | Queried internally by account IDs; `Id` never crosses the wire |
| `LinkedEntryPair` | `lep` | **yes** | `LinkedEntriesController` (`POST {id}/reverse`, `List()` returns `id`) |
| `BooksLockChange` | `blc` | **no** | `OrganizationsController.GetBooksLock()` projects dates/reason/actor without `Id` |
| `YearEndCloseRun` | `yecr` | **yes** | `FinancialsController` (`YearEnd/{code}/CloseRuns/{runId}/Package`) |
| `YearEndCloseEvidence` | `yece` | **no** | Looked up by `YearEndCloseRunId`; `Id` never crosses the wire |
| `YearEndIssuanceDeclaration` | `yeid` | **no** | Looked up by `FinancialYearId`; `Id` never crosses the wire |
| `YearEndLifecycleEvent` | `yele` | **no** | Internal lifecycle log; `Id` never crosses the wire |
| `Bank` | `ba` | **yes** | `BankingController`, `FinancialsController`, `Dto.Banking.*` |
| `BankTransaction` | `btx` | **yes** | `BankingController`, `Dto.Banking.BankTransactionDtos` |
| `BankReconciliation` | `rec` | **yes** | `BankingController`, `Dto.Banking.BankReconciliationDtos` |
| `BankReconciliationItem` | `reci` | **yes** | `BankingController`, `Dto.Banking.BankReconciliationDtos` |
| `BankStatementImport` | `bsi` | **yes** | `BankingController`, `Dto.Banking.BankStatementDtos` |
| `BankStatementLine` | `bsl` | **yes** | `BankingController`, `Dto.Banking.BankStatementDtos` |
| `BankStatementColumnMapping` | `bscm` | **no** | Internal CSV header mapping cache; `Id` never crosses the wire |
| `BankStatementDescriptionRule` | `bsdr` | **yes** | `Dto.Banking.StatementDescriptionRuleDto.Id` (`BankingController.StatementDescriptionRule`, `BulkRecordExpenseFromStatementLines`) |
| `Party` | `pty` | **yes** | `Dto.Common.Party`, `Dto.Banking.BankTransactionDtos`, `Dto.Financial.OpenItemAsOf` |
| `Customer` | `cus` | **yes** | `SalesController`, `Dto.Sales.Client`, `Dto.Sales.Customer` |
| `Vendor` | `ven` | **yes** | `PurchasingController`, `Dto.Purchasing.Supplier`, `Dto.Purchasing.Vendor` |
| `Contact` | `cont` | **yes** | `Dto.Common.Contact`, `Dto.Common.ContactPersonDto`, `BillToContactId` |
| `Address` | `addr` | **no** | `PostalAddress` is an owned value object; `Address.Id` does not cross the wire (`Dto.Common.Address` is legacy/unused) |
| `Partner` | `ptnr` | **yes** | `PartnersController`, `Dto.Banking.StatementOwnerMovementDtos` |
| `PartnerShare` | `psh` | **no** | Composite key `(FinancialYearId, PartnerId)` with `Ignore(s => s.Id)`; has no `Id` column |
| `Tax` | `tax` | **yes** | `TaxController`, `Dto.TaxSystem.Tax`, `Dto.TaxSystem.TaxSetting` |
| `TaxGroup` | `txg` | **yes** | `TaxController`, `Dto.TaxSystem.TaxGroup` |
| `TaxFiling` | `txf` | **yes** | `TaxReportsController` (`AmendsFilingId`, filing responses) |
| `PaymentTerm` | `term` | **yes** | `AdministrationController.PaymentTerms`, `Dto.Common.PaymentTerm`, `Dto.Financial.PaymentTerm` |
| `AuditLog` | `alog` | **yes** | `AdministrationController.AuditLogs` (`Dto.Administration.AuditLog.Id`, `beforeId`) |
| `SalesInvoiceHeader` | `inv` | **yes** | `SalesController`, `Dto.Sales.SalesInvoice`, `SalesCreditsController`, `WriteOffsController` |
| `SalesInvoiceLine` | `il` | **yes** | `Dto.Sales.SalesInvoiceLine`, `CustomerCreditLineInputDto`, `CustomerReturnLineInputDto` |
| `SalesInvoiceEmail` | `iem` | **yes** | `SalesController.ToEmailItem` (`Dto.Sales.InvoiceEmailItemDto.Id`) |
| `SalesReceiptHeader` | `rcpt` | **yes** | `SalesController`, `Dto.Sales.SalesReceipt` |
| `SalesReceiptLine` | `rcptl` | **yes** | `Dto.Sales.SalesReceiptLine` (`SalesReceipt.SalesReceiptLines`) |
| `SalesOrderHeader` | `so` | **yes** | `SalesController.SalesOrders`, `Dto.Sales.SalesOrder`, `SalesInvoice.FromSalesOrderId` |
| `SalesOrderLine` | `sol` | **yes** | `Dto.Sales.SalesOrderLine` (`SalesOrder.SalesOrderLines`) |
| `SalesQuoteHeader` | `quo` | **yes** | `SalesController.Quotations`, `Dto.Sales.SalesQuotation`, `SalesOrder.QuotationId` |
| `SalesQuoteLine` | `quol` | **yes** | `Dto.Sales.SalesQuotationLine` (`SalesQuotation.SalesQuotationLines`) |
| `SalesDeliveryHeader` | `del` | **yes** | `Dto.Sales.SalesDelivery`, `SalesInvoice.FromSalesDeliveryId` |
| `SalesDeliveryLine` | `dell` | **no** | No endpoint or DTO exposes `SalesDeliveryLine.Id` |
| `CustomerAllocation` | `alloc` | **yes** | Referenced as polymorphic GL `SourceDocumentId` when `DocumentType == CustomerAllocation` |
| `CustomerCreditNote` | `cn` | **yes** | `SalesCreditsController`, `Dto.Sales.CustomerCreditDtos` |
| `CustomerCreditNoteLine` | `cnl` | **yes** | `Dto.Sales.CustomerCreditLineDto` |
| `CustomerReturnReceipt` | `crr` | **yes** | `ReturnsController`, `Dto.Inventory.ReturnsDtos` |
| `CustomerReturnReceiptLine` | `crrl` | **yes** | `Dto.Inventory.CustomerReturnReceiptLineDto` |
| `ReceivableWriteOff` | `wo` | **yes** | `WriteOffsController`, `Dto.Sales.WriteOffDtos` |
| `WriteOffRecovery` | `wor` | **yes** | `WriteOffsController`, `Dto.Sales.WriteOffDtos` |
| `RecurringSchedule` | `rsch` | **yes** | `RecurringSchedulesController`, `Services.Recurring.RecurringSchedule*` |
| `RecurringScheduleLine` | `rschl` | **yes** | `Services.Recurring.RecurringScheduleLineInput.Id`, `RecurringScheduleLineView.Id` |
| `RecurringOccurrence` | `rocc` | **yes** | `RecurringSchedulesController` (`occurrences/{occurrenceId}/retry`, `RecurringOccurrenceView.Id`) |
| `DonationInvoiceHeader` | `dinv` | **yes** | `DonationsController` (`DonationInvoice(int id)`, `DeleteDonationInvoice(int id)`, `Dto.Donations.DonationInvoice`) |
| `DonationInvoiceLine` | `dil` | **yes** | `Dto.Donations.DonationInvoiceLine` |
| `Donor` | `dnr` | **yes** | `DonorsController` (`GET/PUT/DELETE {id}`), `Dto.Donors.Donor`, `DonationInvoice.DonorId` |
| `PurchaseInvoiceHeader` | `bill` | **yes** | `PurchasingController`, `Dto.Purchasing.Bill`, `PurchasingCreditsController` |
| `PurchaseInvoiceLine` | `bl` | **yes** | `Dto.Purchasing.BillLine`, `SupplierCreditLineInputDto`, `SupplierReturnLineInputDto` |
| `PurchaseOrderHeader` | `po` | **yes** | `PurchasingController.PurchaseOrders`, `Dto.Purchasing.PurchaseOrder`, `SupplierAdvanceDto.PurchaseOrderHeaderId` |
| `PurchaseOrderLine` | `pol` | **yes** | `Dto.Purchasing.PurchaseOrderLine` |
| `PurchaseReceiptHeader` | `prc` | **yes** | `Dto.Purchasing.PurchaseReceipt`, `PostSupplierReturnDispatchRequest.PurchaseReceiptHeaderId`, GL `SourceDocumentId` |
| `PurchaseReceiptLine` | `prcl` | **yes** | `SupplierReturnLineInputDto.PurchaseReceiptLineId` |
| `VendorPayment` | `vpay` | **yes** | `PurchasingController`, `Dto.Purchasing.BillPayment` |
| `SupplierAdvance` | `adv` | **yes** | `RefundsAndAdvancesController`, `Dto.Financial.SupplierAdvanceDto` |
| `SupplierCreditNote` | `scn` | **yes** | `PurchasingCreditsController`, `Dto.Purchasing.SupplierCreditDtos` |
| `SupplierCreditNoteLine` | `scnl` | **yes** | `Dto.Purchasing.SupplierCreditLineDto` |
| `SupplierReturnDispatch` | `srd` | **yes** | `ReturnsController`, `Dto.Inventory.ReturnsDtos` |
| `SupplierReturnDispatchLine` | `srdl` | **yes** | `Dto.Inventory.SupplierReturnDispatchLineDto` |
| `PartyRefund` | `rf` | **yes** | `RefundsAndAdvancesController`, `Dto.Financial.PartyRefundDto` |
| `PartyRefundAllocation` | `rfa` | **yes** | `Dto.Financial.PartyRefundAllocationDto.Id` |
| `SettlementApplication` | `sapp` | **yes** | `SalesCreditsController`, `PurchasingCreditsController`, `Dto.Sales.SettlementApplicationSummaryDto` |
| `SettlementApplicationReversal` | `sarv` | **no** | Reversals are referenced via `SettlementApplication.Id` (`sapp`); `Id` never crosses the wire |
| `Item` | `item` | **yes** | `CatalogController`, `StockController`, `Dto.Inventory.CatalogItem`, `Dto.Inventory.Item` |
| `ItemCategory` | `icat` | **yes** | `Dto.Inventory.ItemCategory`, `Dto.Inventory.Item.ItemCategoryId` |
| `Measurement` | `uom` | **yes** | `Dto.Inventory.Measurement`, `SalesInvoiceLine.MeasurementId`, `DonationInvoiceLine.MeasurementId`, `Item.*MeasurementId` |
| `ItemTaxGroup` | `itg` | **yes** | `TaxController` (`DeleteItemTaxGroup`, `ItemTaxGroups`), `Dto.TaxSystem.ItemTaxGroup`, `Item.ItemTaxGroupId` |
| `InventoryAdjustment` | `iadj` | **yes** | `StockController.Adjust` (`id`), `StockController.Movements` (`documentId`), GL `SourceDocumentId` |
| `InventoryControlJournal` | `icj` | **yes** | `StockController.Movements` (`id`), `Dto.Inventory.InventoryControlJournal` |
| `DocumentNumberSeries` | `dns` | **no** | Keyed by fiscal year and sequence type; `Id` never crosses the wire |

### ApplicationIdentityDbContext

| Entity | Prefix | In Scope | Wire Evidence / Notes |
|---|---|---|---|
| `RefreshTokenSession` | `sess` | **yes** | `AuthController` (`UserSessionDto.Id`, session revocation) |
| `TrustedDevice` | `dev` | **yes** | MFA device listing and revocation |
| `UserWebAuthnCredential` | `pkey` | **yes** | Passkey/WebAuthn credential listing, registration, and deletion |

---

## 2. Exemptions

| Entity / Property | Reason |
|---|---|
| `AccountClass` / `AccountClassId` | Fixed 1–5 accounting classification enum-like lookup |
| `Status` / `StatusId` | Fixed document status enum-like lookup |
| `SecurityPermission`, `SecurityGroup` | Seeded permission/group catalogue; workspace endpoints key by code (`Name`). `Dto.Security.Permission.Id`, `Dto.Security.Group.Id`, `AdministrationController.SaveRoleRequest.PermissionIds`, and `AdministrationController` (`roles`, `groups`, `GetUser`) are scoped exemptions (`SystemAdministrators`-only legacy operator surface) |
| `User` (`Core.Domain.Security.User`) | Legacy unused user table (`ApplicationUser.Id` is already a string GUID); `Dto.Security.User.Id` on `AdministrationController.GetUser` is a scoped exemption (`SystemAdministrators`-only) |
| `AuditableAttribute`, `AuditableEntity` | Legacy audit configuration |
| `Log`, `SequenceNumber`, `EntitlementUsage`, `CompanySetting`, `GeneralLedgerSetting`, `WorkspaceOperatingSettings`, `CompanyCloseState`, `WorkspacePolicyCoordination`, `PersonalWorkspace`, `WorkspaceLink`, `YearEndCommandReceipt`, `SettlementCommandReceipt` | Internal infrastructure or 1-per-workspace settings rows |
| Join tables (`ItemTaxGroupTax`, `TaxGroupTax`, `CompanyMemberSecurityRole`, `SecurityRolePermission`, `SecurityUserRole`, `CustomerContact`, `VendorContact`) | Join rows whose own `Id` does not cross the wire |
| `WorkspaceAccessRequest` | Already uses opaque string `RequestId` |
| `RecordChange` (`RecordChange.Id`) | Append-only `long` audit row ID used for cursor pagination (`EntityId`/`ParentEntityId` are polymorphic public IDs) |
| `SecurityAuditEvent` (`SecurityAuditEvent.Id`) | Append-only Identity DB audit row ID and `int? cursor` pagination parameter on `GET /api/organizations/current/security-events`, parallel to `RecordChange` (scoped to event rows only) |
| `DataProtectionKeyEntity`, `ExternalTokenRedemption`, `OAuthAuthorizationRequest`, `UserRecoveryCode`, `WebAuthnChallengeEntity` | Internal auth/crypto infrastructure in Identity DB |
| `EmailDispatch` | Already uses GUID `DispatchId` |
| `CurrencyId`, `GroupId` on legacy `MasterGeneralLedger` | Legacy unused / lookup fields |
| `CoaTemplateId` | Already a string slug (e.g. `legacy-default`) |

---

## 3. Polymorphic Type Map (`[PublicIdByType]`)

Used by `PublicIdRegistry.TryGetPrefixForTypeName(string typeName, out string prefix)` for:
- `RecordChange` (`EntityType` / `EntityId`, `ParentEntityType` / `ParentEntityId`)
- `GeneralLedgerTransaction` (`DocumentType` / `SourceDocumentId`)
- `StockController.Movements` (`linkType` / `DocumentType` / `documentId`)
- `Account` role references (`SourceType` / `SourceEntityId`)
- `SettlementApplicationSummaryDto` (`SourceType` / `SourceId`, `DestinationType` / `DestinationId`)
- `PartyRefundAllocationDto` & `RefundAllocationInputDto` (`SourceType` / `SourceId`)
- `OpenItemAsOf` (`PartyType` / `PartyId`, `DocumentType` / `Id`)

| Type Name / Enum Value | Prefix | Notes |
|---|---|---|
| Every in-scope entity CLR class name (e.g. `SalesInvoiceHeader`, `JournalEntryHeader`, `BankTransaction`, `BankStatementLine`, `Account`, `Customer`, `Vendor`, `Party`, `Tax`, `TaxGroup`, `Company`, `CompanyMember`, `PaymentTerm`, `FinancialYear`, `Partner`, `RecurringSchedule`, `RecurringScheduleLine`, `BankStatementDescriptionRule`, `CustomerAllocation`, `SalesReceiptHeader`, `SalesReceiptLine`, `PurchaseInvoiceHeader`, `PurchaseInvoiceLine`, `VendorPayment`, `Bank`, `BankReconciliation`, `SalesInvoiceLine`, `JournalEntryLine`, etc.) | Entity's registered prefix | Auto-registered from `[PublicIdPrefix]` |
| `DocumentTypes.SalesInvoice` / `"SalesInvoice"` / `"invoice"` | `inv` | `SalesInvoiceHeader` |
| `DocumentTypes.SalesReceipt` / `"SalesReceipt"` | `rcpt` | `SalesReceiptHeader` |
| `DocumentTypes.SalesOrder` / `"SalesOrder"` | `so` | `SalesOrderHeader` |
| `DocumentTypes.SalesQuote` / `"SalesQuote"` | `quo` | `SalesQuoteHeader` |
| `DocumentTypes.SalesDelivery` / `"SalesDelivery"` | `del` | `SalesDeliveryHeader` |
| `DocumentTypes.PurchaseInvoice` / `"PurchaseInvoice"` / `"bill"` | `bill` | `PurchaseInvoiceHeader` |
| `DocumentTypes.PurchaseOrder` / `"PurchaseOrder"` | `po` | `PurchaseOrderHeader` |
| `DocumentTypes.PurchaseReceipt` / `"PurchaseReceipt"` | `prc` | `PurchaseReceiptHeader` |
| `DocumentTypes.PurchaseInvoicePayment` / `"PurchaseInvoicePayment"` / `"VendorPayment"` | `vpay` | `VendorPayment` |
| `DocumentTypes.JournalEntry` / `"JournalEntry"` | `je` | `JournalEntryHeader` |
| `DocumentTypes.BankTransaction` / `"BankTransaction"` | `btx` | `BankTransaction` |
| `DocumentTypes.CustomerAllocation` / `"CustomerAllocation"` | `alloc` | `CustomerAllocation` |
| `DocumentTypes.DonationInvoice` / `"DonationInvoice"` | `dinv` | `DonationInvoiceHeader` |
| `DocumentTypes.InventoryAdjustment` / `"InventoryAdjustment"` | `iadj` | `InventoryAdjustment` |
| `DocumentTypes.CustomerCreditNote` / `"CustomerCreditNote"` / `SettlementSourceType.CustomerCreditNote` (`1`) | `cn` | `CustomerCreditNote` |
| `DocumentTypes.SupplierCreditNote` / `"SupplierCreditNote"` / `SettlementSourceType.SupplierCreditNote` (`2`) | `scn` | `SupplierCreditNote` |
| `DocumentTypes.SupplierAdvance` / `"SupplierAdvance"` / `SettlementSourceType.SupplierAdvance` (`3`) | `adv` | `SupplierAdvance` |
| `DocumentTypes.CustomerReturnReceipt` / `"CustomerReturnReceipt"` | `crr` | `CustomerReturnReceipt` |
| `DocumentTypes.SupplierReturnDispatch` / `"SupplierReturnDispatch"` | `srd` | `SupplierReturnDispatch` |
| `DocumentTypes.ReceivableWriteOff` / `"ReceivableWriteOff"` | `wo` | `ReceivableWriteOff` |
| `DocumentTypes.WriteOffRecovery` / `"WriteOffRecovery"` | `wor` | `WriteOffRecovery` |
| `DocumentTypes.PartyRefund` / `"PartyRefund"` | `rf` | `PartyRefund` |
| `DocumentTypes.SettlementApplication` / `"SettlementApplication"` | `sapp` | `SettlementApplication` |
| `SettlementDestinationType.SalesInvoice` (`1`) | `inv` | `SalesInvoiceHeader` |
| `SettlementDestinationType.PurchaseInvoice` (`2`) | `bill` | `PurchaseInvoiceHeader` |
| Exempt entity names (`CompanySetting`, `GeneralLedgerSetting`, `WorkspaceOperatingSettings`, `BooksLockChange`, `PartnerShare`, `TaxGroupTax`, `CompanyMemberSecurityRole`, `MainContraAccount`) | `null` (exempt) | Serialized as `null`, never raw `int` |

---

## 4. Customer-Facing Extension Endpoints Inventory

Non-staff endpoints exposed by extension modules and called by the web app:

| Route Group | Public-Entity IDs Accepted / Returned | Status |
|---|---|---|
| `/api/auth/mfa/devices`, `/api/auth/mfa/devices/{id}` | `TrustedDevice.Id` (`dev`) in list response & delete route | Requires annotation/update |
| `/api/auth/mfa/webauthn/credentials`, `/api/auth/mfa/webauthn/{id}`, `/api/auth/mfa/webauthn/register/complete` | `UserWebAuthnCredential.Id` (`pkey`) in list/register response & delete route | Requires annotation/update |
| `/api/integrations/capabilities` | `companyId` (`ws`) in response | Requires annotation/update |
| `/api/integrations/review/*` (`pending-count`, `items`, `options`, `activity/read`) | `companyId` (`ws`), `bankId` (`ba`), `lineId` / `latestLineId` / `ThroughLineId` (`bsl`), `DraftBillId` (`bill`), `MatchedGeneralLedgerLineId` (`gll`) | Requires annotation/update |
| `/api/integrations/connections/*` | `bankId` (`ba`), `bankStatementLineId` (`bsl`) | Requires annotation/update |
| `/api/email/settings/dispatches` | `InvoiceId` (`inv`) in dispatch list | Requires annotation/update |
| `/api/email/settings/*` (domains, senders) | Extension-owned string/GUID IDs only; no public-entity ints | No change needed |
| `/api/invoice-branding/*` | No public-entity ints (uses `revision` counter) | No change needed |
| `/api/security/policy` | No public-entity ints | No change needed |
| `/api/billing/webhooks/*`, `/api/email/webhooks/*`, `/api/webhooks/*` | External provider webhooks; no public-entity ints on customer wire | No change needed |
| Staff-only endpoints (`/api/staff/*`) | Out of scope per design §1 & §5.10 | Untouched |
