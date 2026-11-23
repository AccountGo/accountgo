// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Api.Service.Ai;

public enum McpFieldDisposition
{
    /// <summary>Safe accounting/structural field (codes, dates, IDs, statuses, currencies, links).</summary>
    KeepUnmasked,

    /// <summary>Keep readable accounting label (CoA account name, item name, tax rate name) after scrubbing embedded PII.</summary>
    KeepAndScrub,

    /// <summary>Replace with a per-session customer pseudonym token (<c>C-XXXXXXXX</c>).</summary>
    PseudonymizeCustomer,

    /// <summary>Replace with a per-session vendor pseudonym token (<c>V-XXXXXXXX</c>).</summary>
    PseudonymizeVendor,

    /// <summary>Replace with a per-session person/member/contact pseudonym token (<c>P-XXXXXXXX</c>).</summary>
    PseudonymizePerson,

    /// <summary>Replace with a per-session bank account pseudonym token (<c>B-XXXXXXXX</c>).</summary>
    PseudonymizeBankAccount,

    /// <summary>Resolve pseudonym role from sibling party/entity ID prefix (<c>cus_</c>, <c>ven_</c>, <c>ba_</c>, else person).</summary>
    PseudonymizeContextualParty,

    /// <summary>Resolve generic <c>name</c> property from object context; fail closed to <see cref="MaskSensitive"/> if unknown.</summary>
    ContextualName,

    /// <summary>Chart-of-accounts account name: pseudonymize as <c>B-…</c> if linked to a bank account, otherwise <see cref="KeepAndScrub"/>.</summary>
    AccountNameOrBankPseudonym,

    /// <summary>Free-text field (descriptions, memos, notes, comments) -> <c>"[text hidden]"</c>.</summary>
    HideFreeText,

    /// <summary>Sensitive contact/address/bank-detail/tax-ID field -> <c>"[masked]"</c>. Also the fail-closed default for unclassified strings.</summary>
    MaskSensitive,
}

/// <summary>
/// Central fail-closed field classification registry for all MCP tool JSON payloads.
/// Any string property name not present in <see cref="Classifications"/> fails closed to <see cref="McpFieldDisposition.MaskSensitive"/>.
/// </summary>
public static partial class McpFieldClassificationRegistry
{
    public const string MaskedPlaceholder = "[masked]";
    public const string HiddenTextPlaceholder = "[text hidden]";

    private static readonly Dictionary<string, McpFieldDisposition> Classifications = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- KeepUnmasked: envelope, metadata, enums, currencies, codes, IDs, numbers, dates, links ---
        ["organization"] = McpFieldDisposition.KeepUnmasked,
        ["status"] = McpFieldDisposition.KeepUnmasked,
        ["kind"] = McpFieldDisposition.KeepUnmasked,
        ["link"] = McpFieldDisposition.KeepUnmasked,
        ["sourcePath"] = McpFieldDisposition.KeepUnmasked,
        ["upgradePlanHint"] = McpFieldDisposition.KeepUnmasked,
        ["requiredModules"] = McpFieldDisposition.KeepUnmasked,
        ["activeModules"] = McpFieldDisposition.KeepUnmasked,
        ["accountingBasis"] = McpFieldDisposition.KeepUnmasked,
        ["basis"] = McpFieldDisposition.KeepUnmasked,
        ["currency"] = McpFieldDisposition.KeepUnmasked,
        ["currencyCode"] = McpFieldDisposition.KeepUnmasked,
        ["baseCurrency"] = McpFieldDisposition.KeepUnmasked,
        ["sourceCurrency"] = McpFieldDisposition.KeepUnmasked,
        ["mirrorCurrency"] = McpFieldDisposition.KeepUnmasked,
        ["currencySymbol"] = McpFieldDisposition.KeepUnmasked,
        ["fiscalYearStatus"] = McpFieldDisposition.KeepUnmasked,
        ["periodStatus"] = McpFieldDisposition.KeepUnmasked,
        ["normalBalance"] = McpFieldDisposition.KeepUnmasked,
        ["accountClass"] = McpFieldDisposition.KeepUnmasked,
        ["classification"] = McpFieldDisposition.KeepUnmasked,
        ["lineType"] = McpFieldDisposition.KeepUnmasked,
        ["documentType"] = McpFieldDisposition.KeepUnmasked,
        ["documentTypeLabel"] = McpFieldDisposition.KeepUnmasked,
        ["entityType"] = McpFieldDisposition.KeepUnmasked,
        ["parentEntityType"] = McpFieldDisposition.KeepUnmasked,
        ["action"] = McpFieldDisposition.KeepUnmasked,
        ["actorKind"] = McpFieldDisposition.KeepUnmasked,
        ["role"] = McpFieldDisposition.KeepUnmasked,
        ["roles"] = McpFieldDisposition.KeepUnmasked,
        ["securityRoles"] = McpFieldDisposition.KeepUnmasked,
        ["permissions"] = McpFieldDisposition.KeepUnmasked,
        ["plan"] = McpFieldDisposition.KeepUnmasked,
        ["planKey"] = McpFieldDisposition.KeepUnmasked,
        ["edition"] = McpFieldDisposition.KeepUnmasked,
        ["subscriptionStatus"] = McpFieldDisposition.KeepUnmasked,
        ["frequency"] = McpFieldDisposition.KeepUnmasked,
        ["interval"] = McpFieldDisposition.KeepUnmasked,
        ["reconciliationStatus"] = McpFieldDisposition.KeepUnmasked,
        ["matchStatus"] = McpFieldDisposition.KeepUnmasked,
        ["state"] = McpFieldDisposition.KeepUnmasked,
        ["type"] = McpFieldDisposition.KeepUnmasked,
        ["accountType"] = McpFieldDisposition.KeepUnmasked,
        ["transactionType"] = McpFieldDisposition.KeepUnmasked,
        ["entryType"] = McpFieldDisposition.KeepUnmasked,
        ["category"] = McpFieldDisposition.KeepUnmasked,
        ["direction"] = McpFieldDisposition.KeepUnmasked,
        ["paymentMethod"] = McpFieldDisposition.KeepUnmasked,
        ["method"] = McpFieldDisposition.KeepUnmasked,
        ["taxType"] = McpFieldDisposition.KeepUnmasked,
        ["taxCode"] = McpFieldDisposition.KeepUnmasked,
        ["code"] = McpFieldDisposition.KeepUnmasked,
        ["accountCode"] = McpFieldDisposition.KeepUnmasked,
        ["parentAccountCode"] = McpFieldDisposition.KeepUnmasked,
        ["sku"] = McpFieldDisposition.KeepUnmasked,
        ["itemCode"] = McpFieldDisposition.KeepUnmasked,
        ["unitOfMeasure"] = McpFieldDisposition.KeepUnmasked,
        ["uom"] = McpFieldDisposition.KeepUnmasked,
        ["measurement"] = McpFieldDisposition.KeepUnmasked,
        ["measurementName"] = McpFieldDisposition.KeepUnmasked,
        ["slug"] = McpFieldDisposition.KeepUnmasked,
        ["companySlug"] = McpFieldDisposition.KeepUnmasked,
        ["companyCode"] = McpFieldDisposition.KeepUnmasked,
        ["shortName"] = McpFieldDisposition.KeepUnmasked,
        ["version"] = McpFieldDisposition.KeepUnmasked,
        ["schemaVersion"] = McpFieldDisposition.KeepUnmasked,
        ["correlationId"] = McpFieldDisposition.KeepUnmasked,
        ["reasonCode"] = McpFieldDisposition.KeepUnmasked,
        ["severity"] = McpFieldDisposition.KeepUnmasked,
        ["checkCode"] = McpFieldDisposition.KeepUnmasked,
        ["bucket"] = McpFieldDisposition.KeepUnmasked,
        ["source"] = McpFieldDisposition.KeepUnmasked,
        ["origin"] = McpFieldDisposition.KeepUnmasked,
        ["mode"] = McpFieldDisposition.KeepUnmasked,
        ["workspaceKind"] = McpFieldDisposition.KeepUnmasked,
        ["isPersonal"] = McpFieldDisposition.KeepUnmasked,
        ["entityKind"] = McpFieldDisposition.KeepUnmasked,
        ["maskedAccountNumber"] = McpFieldDisposition.KeepUnmasked,
        ["accountNumberMasked"] = McpFieldDisposition.KeepUnmasked,
        ["last4"] = McpFieldDisposition.KeepUnmasked,
        ["paymentTermId"] = McpFieldDisposition.KeepUnmasked,
        ["paymentTerm"] = McpFieldDisposition.KeepUnmasked,
        ["paymentTermName"] = McpFieldDisposition.KeepUnmasked,
        ["paymentTerms"] = McpFieldDisposition.KeepUnmasked,
        ["settlementSummary"] = McpFieldDisposition.KeepAndScrub,
        ["activationState"] = McpFieldDisposition.KeepUnmasked,
        ["area"] = McpFieldDisposition.KeepUnmasked,
        ["asOf"] = McpFieldDisposition.KeepUnmasked,
        ["booksStartDate"] = McpFieldDisposition.KeepUnmasked,
        ["collaborationIntent"] = McpFieldDisposition.KeepUnmasked,
        ["collaborationState"] = McpFieldDisposition.KeepUnmasked,
        ["end"] = McpFieldDisposition.KeepUnmasked,
        ["fiscalYearCode"] = McpFieldDisposition.KeepUnmasked,
        ["fiscalYearEnd"] = McpFieldDisposition.KeepUnmasked,
        ["from"] = McpFieldDisposition.KeepUnmasked,
        ["journalWorkflow"] = McpFieldDisposition.KeepUnmasked,
        ["key"] = McpFieldDisposition.KeepUnmasked,
        ["lock"] = McpFieldDisposition.KeepUnmasked,
        ["measurementId"] = McpFieldDisposition.KeepUnmasked,
        ["oldestDueDate"] = McpFieldDisposition.KeepUnmasked,
        ["path"] = McpFieldDisposition.KeepUnmasked,
        ["periodLabel"] = McpFieldDisposition.KeepUnmasked,
        ["purchaseAccountCode"] = McpFieldDisposition.KeepUnmasked,
        ["purpose"] = McpFieldDisposition.KeepUnmasked,
        ["salesAccountCode"] = McpFieldDisposition.KeepUnmasked,
        ["start"] = McpFieldDisposition.KeepUnmasked,
        ["surface"] = McpFieldDisposition.KeepUnmasked,
        ["to"] = McpFieldDisposition.KeepUnmasked,
        ["itemDescription"] = McpFieldDisposition.HideFreeText,
        ["measurementDescription"] = McpFieldDisposition.KeepAndScrub,
        ["question"] = McpFieldDisposition.KeepAndScrub,
        ["detail"] = McpFieldDisposition.HideFreeText,

        // Record public IDs & document numbers
        ["id"] = McpFieldDisposition.KeepUnmasked,
        ["publicId"] = McpFieldDisposition.KeepUnmasked,
        ["companyId"] = McpFieldDisposition.KeepUnmasked,
        ["accountId"] = McpFieldDisposition.KeepUnmasked,
        ["ledgerAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["linkedAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["expenseAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["revenueAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["salesAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["inventoryAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["costOfGoodsSoldAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["clientId"] = McpFieldDisposition.KeepUnmasked,
        ["customerId"] = McpFieldDisposition.KeepUnmasked,
        ["vendorId"] = McpFieldDisposition.KeepUnmasked,
        ["supplierId"] = McpFieldDisposition.KeepUnmasked,
        ["partyId"] = McpFieldDisposition.KeepUnmasked,
        ["bankId"] = McpFieldDisposition.KeepUnmasked,
        ["bankAccountId"] = McpFieldDisposition.KeepUnmasked,
        ["invoiceId"] = McpFieldDisposition.KeepUnmasked,
        ["salesInvoiceId"] = McpFieldDisposition.KeepUnmasked,
        ["billId"] = McpFieldDisposition.KeepUnmasked,
        ["purchaseInvoiceId"] = McpFieldDisposition.KeepUnmasked,
        ["paymentId"] = McpFieldDisposition.KeepUnmasked,
        ["receiptId"] = McpFieldDisposition.KeepUnmasked,
        ["journalEntryId"] = McpFieldDisposition.KeepUnmasked,
        ["generalLedgerHeaderId"] = McpFieldDisposition.KeepUnmasked,
        ["reversedGeneralLedgerHeaderId"] = McpFieldDisposition.KeepUnmasked,
        ["sourceDocumentId"] = McpFieldDisposition.KeepUnmasked,
        ["sourceJournalId"] = McpFieldDisposition.KeepUnmasked,
        ["mirrorJournalId"] = McpFieldDisposition.KeepUnmasked,
        ["itemId"] = McpFieldDisposition.KeepUnmasked,
        ["taxRateId"] = McpFieldDisposition.KeepUnmasked,
        ["taxGroupId"] = McpFieldDisposition.KeepUnmasked,
        ["reconciliationId"] = McpFieldDisposition.KeepUnmasked,
        ["bankReconciliationId"] = McpFieldDisposition.KeepUnmasked,
        ["statementId"] = McpFieldDisposition.KeepUnmasked,
        ["statementLineId"] = McpFieldDisposition.KeepUnmasked,
        ["scheduleId"] = McpFieldDisposition.KeepUnmasked,
        ["recurringScheduleId"] = McpFieldDisposition.KeepUnmasked,
        ["fiscalYearId"] = McpFieldDisposition.KeepUnmasked,
        ["memberId"] = McpFieldDisposition.KeepUnmasked,
        ["companyMemberId"] = McpFieldDisposition.KeepUnmasked,
        ["entityId"] = McpFieldDisposition.KeepUnmasked,
        ["parentEntityId"] = McpFieldDisposition.KeepUnmasked,
        ["recordId"] = McpFieldDisposition.KeepUnmasked,
        ["captureId"] = McpFieldDisposition.KeepUnmasked,
        ["linkId"] = McpFieldDisposition.KeepUnmasked,
        ["pairId"] = McpFieldDisposition.KeepUnmasked,
        ["lineId"] = McpFieldDisposition.KeepUnmasked,
        ["contactId"] = McpFieldDisposition.KeepUnmasked,
        ["no"] = McpFieldDisposition.KeepUnmasked,
        ["invoiceNo"] = McpFieldDisposition.KeepUnmasked,
        ["billNo"] = McpFieldDisposition.KeepUnmasked,
        ["vendorInvoiceNo"] = McpFieldDisposition.KeepUnmasked,
        ["receiptNo"] = McpFieldDisposition.KeepUnmasked,
        ["paymentNo"] = McpFieldDisposition.KeepUnmasked,
        ["journalNo"] = McpFieldDisposition.KeepUnmasked,
        ["voucherNo"] = McpFieldDisposition.KeepUnmasked,
        ["documentNo"] = McpFieldDisposition.KeepUnmasked,
        ["referenceNo"] = McpFieldDisposition.KeepUnmasked,
        ["reference"] = McpFieldDisposition.KeepUnmasked,
        ["checkNo"] = McpFieldDisposition.KeepUnmasked,
        ["chequeNo"] = McpFieldDisposition.KeepUnmasked,
        ["number"] = McpFieldDisposition.KeepUnmasked,
        ["prefix"] = McpFieldDisposition.KeepUnmasked,
        ["field"] = McpFieldDisposition.KeepUnmasked,
        ["fieldName"] = McpFieldDisposition.KeepUnmasked,
        ["propertyName"] = McpFieldDisposition.KeepUnmasked,

        // Dates & timestamps
        ["date"] = McpFieldDisposition.KeepUnmasked,
        ["invoiceDate"] = McpFieldDisposition.KeepUnmasked,
        ["billDate"] = McpFieldDisposition.KeepUnmasked,
        ["dueDate"] = McpFieldDisposition.KeepUnmasked,
        ["paymentDate"] = McpFieldDisposition.KeepUnmasked,
        ["receiptDate"] = McpFieldDisposition.KeepUnmasked,
        ["transactionDate"] = McpFieldDisposition.KeepUnmasked,
        ["entryDate"] = McpFieldDisposition.KeepUnmasked,
        ["postingDate"] = McpFieldDisposition.KeepUnmasked,
        ["lastInvoiceDate"] = McpFieldDisposition.KeepUnmasked,
        ["lastBillDate"] = McpFieldDisposition.KeepUnmasked,
        ["asOfDate"] = McpFieldDisposition.KeepUnmasked,
        ["asOfDateUtc"] = McpFieldDisposition.KeepUnmasked,
        ["fromDate"] = McpFieldDisposition.KeepUnmasked,
        ["toDate"] = McpFieldDisposition.KeepUnmasked,
        ["startDate"] = McpFieldDisposition.KeepUnmasked,
        ["endDate"] = McpFieldDisposition.KeepUnmasked,
        ["periodStart"] = McpFieldDisposition.KeepUnmasked,
        ["periodEnd"] = McpFieldDisposition.KeepUnmasked,
        ["createdAt"] = McpFieldDisposition.KeepUnmasked,
        ["updatedAt"] = McpFieldDisposition.KeepUnmasked,
        ["postedAt"] = McpFieldDisposition.KeepUnmasked,
        ["voidedAt"] = McpFieldDisposition.KeepUnmasked,
        ["approvedAt"] = McpFieldDisposition.KeepUnmasked,
        ["occurredAt"] = McpFieldDisposition.KeepUnmasked,
        ["joinedAt"] = McpFieldDisposition.KeepUnmasked,
        ["nextRunDate"] = McpFieldDisposition.KeepUnmasked,
        ["lastRunDate"] = McpFieldDisposition.KeepUnmasked,
        ["statementDate"] = McpFieldDisposition.KeepUnmasked,
        ["reconciledAt"] = McpFieldDisposition.KeepUnmasked,
        ["clearedAt"] = McpFieldDisposition.KeepUnmasked,
        ["closedAt"] = McpFieldDisposition.KeepUnmasked,
        ["lockedThrough"] = McpFieldDisposition.KeepUnmasked,
        ["booksLockedThrough"] = McpFieldDisposition.KeepUnmasked,
        ["timestamp"] = McpFieldDisposition.KeepUnmasked,
        ["sentAt"] = McpFieldDisposition.KeepUnmasked,
        ["deliveredAt"] = McpFieldDisposition.KeepUnmasked,

        // --- KeepAndScrub: CoA account names (non-bank), item names, tax rate names, system messages ---
        ["accountName"] = McpFieldDisposition.AccountNameOrBankPseudonym,
        ["ledgerAccountName"] = McpFieldDisposition.AccountNameOrBankPseudonym,
        ["linkedAccountName"] = McpFieldDisposition.AccountNameOrBankPseudonym,
        ["expenseAccountName"] = McpFieldDisposition.KeepAndScrub,
        ["revenueAccountName"] = McpFieldDisposition.KeepAndScrub,
        ["salesAccountName"] = McpFieldDisposition.KeepAndScrub,
        ["itemName"] = McpFieldDisposition.KeepAndScrub,
        ["productName"] = McpFieldDisposition.KeepAndScrub,
        ["serviceName"] = McpFieldDisposition.KeepAndScrub,
        ["taxName"] = McpFieldDisposition.KeepAndScrub,
        ["taxRateName"] = McpFieldDisposition.KeepAndScrub,
        ["roleName"] = McpFieldDisposition.KeepAndScrub,
        ["securityRoleName"] = McpFieldDisposition.KeepAndScrub,
        ["permissionName"] = McpFieldDisposition.KeepAndScrub,
        ["fiscalYearName"] = McpFieldDisposition.KeepAndScrub,
        ["periodName"] = McpFieldDisposition.KeepAndScrub,
        ["companyName"] = McpFieldDisposition.KeepAndScrub,
        ["workspaceName"] = McpFieldDisposition.KeepAndScrub,
        ["title"] = McpFieldDisposition.KeepAndScrub,
        ["label"] = McpFieldDisposition.HideFreeText,
        ["message"] = McpFieldDisposition.KeepAndScrub,
        ["recentInvoicesNote"] = McpFieldDisposition.KeepAndScrub,
        ["recentBillsNote"] = McpFieldDisposition.KeepAndScrub,

        // --- Contextual generic name ---
        ["name"] = McpFieldDisposition.ContextualName,

        // --- Pseudonymize: Customer ---
        ["customerName"] = McpFieldDisposition.PseudonymizeCustomer,
        ["clientName"] = McpFieldDisposition.PseudonymizeCustomer,
        ["billToName"] = McpFieldDisposition.PseudonymizeCustomer,

        // --- Pseudonymize: Vendor ---
        ["vendorName"] = McpFieldDisposition.PseudonymizeVendor,
        ["supplierName"] = McpFieldDisposition.PseudonymizeVendor,

        // --- Pseudonymize: Bank Account ---
        ["bankName"] = McpFieldDisposition.PseudonymizeBankAccount,
        ["bankAccountName"] = McpFieldDisposition.PseudonymizeBankAccount,
        ["institution"] = McpFieldDisposition.PseudonymizeBankAccount,
        ["institutionName"] = McpFieldDisposition.PseudonymizeBankAccount,
        ["financialInstitution"] = McpFieldDisposition.PseudonymizeBankAccount,

        // --- Pseudonymize: Contextual party (cus_/ven_/ba_/else person) ---
        ["partyName"] = McpFieldDisposition.PseudonymizeContextualParty,
        ["counterpartyName"] = McpFieldDisposition.PseudonymizeContextualParty,
        ["counterparty"] = McpFieldDisposition.PseudonymizeContextualParty,
        ["payee"] = McpFieldDisposition.PseudonymizeContextualParty,
        ["payeeName"] = McpFieldDisposition.PseudonymizeContextualParty,

        // --- Pseudonymize: Person / Member / Contact ---
        ["personName"] = McpFieldDisposition.PseudonymizePerson,
        ["memberName"] = McpFieldDisposition.PseudonymizePerson,
        ["userName"] = McpFieldDisposition.PseudonymizePerson,
        ["fullName"] = McpFieldDisposition.PseudonymizePerson,
        ["displayName"] = McpFieldDisposition.PseudonymizePerson,
        ["contactName"] = McpFieldDisposition.PseudonymizePerson,
        ["firstName"] = McpFieldDisposition.PseudonymizePerson,
        ["lastName"] = McpFieldDisposition.PseudonymizePerson,
        ["actorName"] = McpFieldDisposition.PseudonymizePerson,
        ["actorDisplayName"] = McpFieldDisposition.PseudonymizePerson,
        ["createdBy"] = McpFieldDisposition.PseudonymizePerson,
        ["createdByName"] = McpFieldDisposition.PseudonymizePerson,
        ["updatedBy"] = McpFieldDisposition.PseudonymizePerson,
        ["updatedByName"] = McpFieldDisposition.PseudonymizePerson,
        ["approvedBy"] = McpFieldDisposition.PseudonymizePerson,
        ["approvedByName"] = McpFieldDisposition.PseudonymizePerson,
        ["postedBy"] = McpFieldDisposition.PseudonymizePerson,
        ["voidedBy"] = McpFieldDisposition.PseudonymizePerson,
        ["invitedBy"] = McpFieldDisposition.PseudonymizePerson,
        ["invitedByName"] = McpFieldDisposition.PseudonymizePerson,
        ["ownerName"] = McpFieldDisposition.PseudonymizePerson,

        // --- HideFreeText -> "[text hidden]" ---
        ["description"] = McpFieldDisposition.HideFreeText,
        ["memo"] = McpFieldDisposition.HideFreeText,
        ["notes"] = McpFieldDisposition.HideFreeText,
        ["note"] = McpFieldDisposition.HideFreeText,
        ["narration"] = McpFieldDisposition.HideFreeText,
        ["comment"] = McpFieldDisposition.HideFreeText,
        ["comments"] = McpFieldDisposition.HideFreeText,
        ["lineDescription"] = McpFieldDisposition.HideFreeText,
        ["voidReason"] = McpFieldDisposition.HideFreeText,
        ["reason"] = McpFieldDisposition.HideFreeText,
        ["summary"] = McpFieldDisposition.HideFreeText,
        ["terms"] = McpFieldDisposition.HideFreeText,
        ["termsAndConditions"] = McpFieldDisposition.HideFreeText,
        ["footer"] = McpFieldDisposition.HideFreeText,
        ["subject"] = McpFieldDisposition.HideFreeText,
        ["body"] = McpFieldDisposition.HideFreeText,
        ["details"] = McpFieldDisposition.HideFreeText,
        ["rawDescription"] = McpFieldDisposition.HideFreeText,
        ["statementDescription"] = McpFieldDisposition.HideFreeText,
        ["bankDescription"] = McpFieldDisposition.HideFreeText,
        ["payeeDescription"] = McpFieldDisposition.HideFreeText,
        ["changesJson"] = McpFieldDisposition.HideFreeText,
        ["oldValue"] = McpFieldDisposition.HideFreeText,
        ["newValue"] = McpFieldDisposition.HideFreeText,
        ["before"] = McpFieldDisposition.HideFreeText,
        ["after"] = McpFieldDisposition.HideFreeText,
        ["diff"] = McpFieldDisposition.HideFreeText,

        // --- MaskSensitive -> "[masked]" ---
        ["email"] = McpFieldDisposition.MaskSensitive,
        ["emailAddress"] = McpFieldDisposition.MaskSensitive,
        ["vendorEmail"] = McpFieldDisposition.MaskSensitive,
        ["customerEmail"] = McpFieldDisposition.MaskSensitive,
        ["clientEmail"] = McpFieldDisposition.MaskSensitive,
        ["actorEmail"] = McpFieldDisposition.MaskSensitive,
        ["fromEmail"] = McpFieldDisposition.MaskSensitive,
        ["toEmail"] = McpFieldDisposition.MaskSensitive,
        ["ccEmail"] = McpFieldDisposition.MaskSensitive,
        ["bccEmail"] = McpFieldDisposition.MaskSensitive,
        ["recipientEmail"] = McpFieldDisposition.MaskSensitive,
        ["phone"] = McpFieldDisposition.MaskSensitive,
        ["phoneNumber"] = McpFieldDisposition.MaskSensitive,
        ["vendorPhone"] = McpFieldDisposition.MaskSensitive,
        ["customerPhone"] = McpFieldDisposition.MaskSensitive,
        ["clientPhone"] = McpFieldDisposition.MaskSensitive,
        ["mobile"] = McpFieldDisposition.MaskSensitive,
        ["fax"] = McpFieldDisposition.MaskSensitive,
        ["website"] = McpFieldDisposition.MaskSensitive,
        ["url"] = McpFieldDisposition.MaskSensitive,
        ["address"] = McpFieldDisposition.MaskSensitive,
        ["vendorAddress"] = McpFieldDisposition.MaskSensitive,
        ["customerAddress"] = McpFieldDisposition.MaskSensitive,
        ["clientAddress"] = McpFieldDisposition.MaskSensitive,
        ["address1"] = McpFieldDisposition.MaskSensitive,
        ["address2"] = McpFieldDisposition.MaskSensitive,
        ["addressLines"] = McpFieldDisposition.MaskSensitive,
        ["street"] = McpFieldDisposition.MaskSensitive,
        ["street1"] = McpFieldDisposition.MaskSensitive,
        ["street2"] = McpFieldDisposition.MaskSensitive,
        ["companyStreet"] = McpFieldDisposition.MaskSensitive,
        ["city"] = McpFieldDisposition.MaskSensitive,
        ["stateProvince"] = McpFieldDisposition.MaskSensitive,
        ["province"] = McpFieldDisposition.MaskSensitive,
        ["region"] = McpFieldDisposition.MaskSensitive,
        ["postalCode"] = McpFieldDisposition.MaskSensitive,
        ["companyPostalCode"] = McpFieldDisposition.MaskSensitive,
        ["zip"] = McpFieldDisposition.MaskSensitive,
        ["zipCode"] = McpFieldDisposition.MaskSensitive,
        ["country"] = McpFieldDisposition.MaskSensitive,
        ["companyCountry"] = McpFieldDisposition.MaskSensitive,
        ["billingAddress"] = McpFieldDisposition.MaskSensitive,
        ["shippingAddress"] = McpFieldDisposition.MaskSensitive,
        ["mailingAddress"] = McpFieldDisposition.MaskSensitive,
        ["accountNumber"] = McpFieldDisposition.MaskSensitive,
        ["routingNumber"] = McpFieldDisposition.MaskSensitive,
        ["iban"] = McpFieldDisposition.MaskSensitive,
        ["swift"] = McpFieldDisposition.MaskSensitive,
        ["swiftCode"] = McpFieldDisposition.MaskSensitive,
        ["bic"] = McpFieldDisposition.MaskSensitive,
        ["cardNumber"] = McpFieldDisposition.MaskSensitive,
        ["bsb"] = McpFieldDisposition.MaskSensitive,
        ["sortCode"] = McpFieldDisposition.MaskSensitive,
        ["branchCode"] = McpFieldDisposition.MaskSensitive,
        ["taxId"] = McpFieldDisposition.MaskSensitive,
        ["taxNumber"] = McpFieldDisposition.MaskSensitive,
        ["tin"] = McpFieldDisposition.MaskSensitive,
        ["ein"] = McpFieldDisposition.MaskSensitive,
        ["vatNumber"] = McpFieldDisposition.MaskSensitive,
        ["gstNumber"] = McpFieldDisposition.MaskSensitive,
        ["ssn"] = McpFieldDisposition.MaskSensitive,
        ["registrationNumber"] = McpFieldDisposition.MaskSensitive,
        ["businessNumber"] = McpFieldDisposition.MaskSensitive,
        ["companyRegistrationNumber"] = McpFieldDisposition.MaskSensitive,
        ["actorUserId"] = McpFieldDisposition.KeepUnmasked,
        ["userId"] = McpFieldDisposition.KeepUnmasked,
        ["invitedByUserId"] = McpFieldDisposition.KeepUnmasked,
        ["ownerUserId"] = McpFieldDisposition.KeepUnmasked,
    };

    public static bool IsExplicitlyClassified(string propertyName) =>
        !string.IsNullOrWhiteSpace(propertyName) && Classifications.ContainsKey(propertyName);

    /// <summary>
    /// Returns the explicit disposition for <paramref name="propertyName"/>, or fails closed to
    /// <see cref="McpFieldDisposition.MaskSensitive"/> for any unclassified property.
    /// </summary>
    public static McpFieldDisposition Classify(string propertyName) =>
        !string.IsNullOrWhiteSpace(propertyName) && Classifications.TryGetValue(propertyName, out var disposition)
            ? disposition
            : McpFieldDisposition.MaskSensitive;

    public static IReadOnlyDictionary<string, McpFieldDisposition> AllClassifications => Classifications;

    /// <summary>
    /// Scrubs embedded emails, phone numbers, IBANs, and long numeric identifiers from an otherwise safe label.
    /// </summary>
    public static string ScrubEmbeddedPii(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var scrubbed = EmailRegex().Replace(value, MaskedPlaceholder);
        scrubbed = IbanRegex().Replace(scrubbed, MaskedPlaceholder);
        scrubbed = PhoneRegex().Replace(scrubbed, MaskedPlaceholder);
        scrubbed = LongDigitsRegex().Replace(scrubbed, MaskedPlaceholder);
        return scrubbed;
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<!\w)(?:\+?\d[\d\s().\-]{7,}\d)(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<!\d)\d{8,}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongDigitsRegex();
}
