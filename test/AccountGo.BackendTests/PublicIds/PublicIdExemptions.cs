// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace AccountGo.BackendTests.PublicIds;

/// <summary>
/// Authoritative exemption registry for DTO properties and controller action parameters
/// that legitimately carry numeric integer IDs (with required reason strings).
/// </summary>
public static class PublicIdExemptions
{
    /// <summary>Scoped DTO property exemptions: "TypeFullName.PropertyName" -> reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> DtoProperties = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Fixed 1-5 accounting classification (enum-like lookup)
        ["Dto.Financial.Account.AccountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["Dto.Financial.CreateAccountRequest.AccountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["Dto.Financial.CoaImportRowPreviewDto.AccountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["Dto.Financial.AccountClass.Id"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["Api.Mcp.Models.BalanceSheetAccountRow.AccountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["Api.Service.Import.CoaCanonicalRow.AccountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",

        // Fixed status lookup
        ["Dto.Common.Status.Id"] = "Fixed document status enum-like lookup.",
        ["Dto.Sales.SalesInvoice.StatusId"] = "Fixed document status enum-like lookup.",
        ["Dto.Sales.SalesOrder.StatusId"] = "Fixed document status enum-like lookup.",
        ["Dto.Sales.SalesQuotation.StatusId"] = "Fixed document status enum-like lookup.",
        ["Dto.Purchasing.PurchaseOrder.StatusId"] = "Fixed document status enum-like lookup.",

        // SystemAdministrators-only legacy operator surface (per c-050)
        ["Dto.Security.Permission.Id"] = "Seeded SecurityPermission catalogue on SystemAdministrators-only AdministrationController endpoints.",
        ["Dto.Security.Group.Id"] = "Seeded SecurityGroup catalogue on SystemAdministrators-only AdministrationController endpoints.",
        ["Dto.Security.User.Id"] = "Legacy Core.Domain.Security.User on SystemAdministrators-only AdministrationController.GetUser.",
        ["Api.Controllers.AdministrationController+SaveRoleRequest.PermissionIds"] = "Seeded SecurityPermission IDs on SystemAdministrators-only AdministrationController.SaveRole.",

        // Legacy / unused DTOs whose entities do not cross the customer wire
        ["Dto.Common.Address.Id"] = "Legacy unused Address DTO; PostalAddress is an owned value object.",
        ["Dto.Financial.MasterGeneralLedger.CurrencyId"] = "Legacy unused CurrencyId field on MasterGeneralLedger.",
        ["Dto.Financial.MasterGeneralLedger.GroupId"] = "Legacy unused GroupId field on MasterGeneralLedger.",
        ["Dto.Administration.GeneralLedgerSetting.Id"] = "1-per-workspace settings row (exempt in design §4).",
        ["Dto.Administration.Company.Id"] = "Legacy unused Dto.Administration.Company; active endpoints return CompanySummaryDto / anonymous DTOs.",
        ["Dto.TaxSystem.TaxGroupTax.Id"] = "Join table row (exempt in design §4).",
        ["Dto.TaxSystem.ItemTaxGroupTax.Id"] = "Join table row (exempt in design §4).",
    };

    /// <summary>Scoped controller action parameter exemptions: "ControllerName.ActionName.paramName" -> reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> ActionParameters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AuditActivityController.SecurityEvents.cursor"] = "Append-only SecurityAuditEvent pagination cursor (exempt per c-050).",
        ["AuditActivityController.Activity.cursor"] = "Append-only RecordChange long pagination cursor (exempt in design §4).",
    };

    /// <summary>
    /// Scoped HTTP wire walker exemptions for numeric JSON fields:
    /// "EndpointPathFragment:jsonPropertyName" -> reason.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WireFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["*:accountClassId"] = "Fixed 1-5 AccountClass enum-like classification lookup.",
        ["*:statusId"] = "Fixed document status enum-like lookup.",
        ["/api/organizations/current/activity:id"] = "Append-only RecordChange long audit row ID (exempt in design §4).",
        ["/api/organizations/current/security-events:id"] = "Append-only SecurityAuditEvent audit row ID (exempt per c-050).",
        ["/api/Administration/roles:id"] = "SystemAdministrators-only SecurityPermission IDs (exempt per c-050).",
        ["/api/Administration/groups:id"] = "SystemAdministrators-only SecurityGroup/SecurityPermission IDs (exempt per c-050).",
    };
}
