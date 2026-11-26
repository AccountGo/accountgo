// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures;
using Api.Filters;
using Api.PublicIds;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// "Download all documents" (design 3.9 S2/S3). A workspace-level action with the same authority as deleting the workspace:
/// Owner plus MFA step-up. Not behind <c>Documents:Enabled</c>, because it also covers scanned bills and invoices.
/// </summary>
[ApiController]
[Authorize]
[TenantScoped]
[RequireWorkspaceRole(CompanyMemberRole.Owner)]
[Route("api/documents/exports")]
public sealed class DocumentExportsController(DocumentExportService exports, ITenantContext tenant) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        Ok(await exports.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = "MfaStepUp")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await exports.CreateAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (DocumentCaptureServiceException ex)
        {
            return Problem(ex);
        }
        catch (DocumentCaptureAdmissionBusyException)
        {
            Response.Headers.RetryAfter = "1";
            return Problem(new DocumentCaptureServiceException(503, "DOCUMENT_BUSY"));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get([Dto.Common.PublicId("dex")] int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await exports.GetRunAsync(id, cancellationToken).ConfigureAwait(false));
        }
        catch (DocumentCaptureServiceException ex)
        {
            return Problem(ex);
        }
    }

    [HttpGet("{id}/file")]
    [Authorize(Policy = "MfaStepUp")]
    public async Task<IActionResult> File([Dto.Common.PublicId("dex")] int id)
    {
        try
        {
            await exports.StreamAsync(id, Response, tenant.CompanySlug ?? "workspace", HttpContext.RequestAborted).ConfigureAwait(false);
            return new EmptyResult();
        }
        catch (DocumentCaptureServiceException ex) when (!Response.HasStarted)
        {
            return Problem(ex);
        }
    }

    private ObjectResult Problem(DocumentCaptureServiceException exception)
    {
        if (exception.StatusCode == StatusCodes.Status404NotFound)
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });
        var problem = new ProblemDetails { Status = exception.StatusCode, Title = exception.ErrorCode };
        problem.Extensions["code"] = exception.ErrorCode;
        return new ObjectResult(problem) { StatusCode = exception.StatusCode };
    }
}
