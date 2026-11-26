// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures;
using Api.Filters;
using Api.PublicIds;
using Core.Authorization;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Api.Controllers;

/// <summary>
/// The documents library: stored files, filed scans and attachments on bills, invoices, customers and vendors. Reads are
/// authorised per document by <see cref="DocumentLibraryService"/> (library permission, or read access to a linked record);
/// library-only management actions also carry the operation guard.
/// </summary>
[ApiController]
[Authorize]
[TenantScoped]
[Route("api/documents")]
public sealed class DocumentsController(DocumentLibraryService documents, ITenantContext tenant, IEntitlementService entitlements)
    : ControllerBase
{
    private const int ActionBodyLimit = 4 * 1024;

    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentUploadLimits.MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentUploadLimits.MaxRequestBodyBytes, ValueLengthLimit = 8 * 1024)]
    public async Task<IActionResult> Upload([FromForm] DocumentUploadForm form, CancellationToken cancellationToken) =>
        await Run(async () =>
        {
            if (form.File is null || form.File.Length == 0)
                return ErrorProblem(400, "DOCUMENT_EMPTY");
            var bytes = await ReadBoundedAsync(form.File, cancellationToken).ConfigureAwait(false);
            var view = await documents.UploadAsync(bytes, form.File.ContentType, form.File.FileName,
                new DocumentMetadataInput(form.Title, form.Category, form.DocumentDate, form.Notes),
                string.IsNullOrWhiteSpace(form.LinkTargetType) ? null : form.LinkTargetType.Trim(),
                string.IsNullOrWhiteSpace(form.LinkTargetId) ? null : form.LinkTargetId.Trim(),
                Request.Headers["Idempotency-Key"].FirstOrDefault(), form.Force, cancellationToken).ConfigureAwait(false);
            return Ok(view);
        }).ConfigureAwait(false);

    [HttpGet]
    [RequireOperation(AccessOperationCatalog.DocumentsView)]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] bool? linked, [FromQuery] bool archived = false,
        [FromQuery] bool trash = false, [FromQuery] string? q = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        await Run(async () => Ok(await documents.ListAsync(
            new DocumentListQuery(category, linked, archived, trash, q, page, pageSize), cancellationToken).ConfigureAwait(false)))
            .ConfigureAwait(false);

    [HttpGet("linked/{targetType}/{targetId}")]
    public async Task<IActionResult> Linked(string targetType, string targetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        await Run(async () => Ok(await documents.ListLinkedAsync(targetType, targetId, page, pageSize, cancellationToken)
            .ConfigureAwait(false))).ConfigureAwait(false);

    [HttpGet("{id}")]
    public async Task<IActionResult> Get([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.GetAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpGet("{id}/bytes")]
    public async Task<IActionResult> Bytes([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () =>
        {
            var (bytes, contentType, fileName) = await documents.GetBytesAsync(id, cancellationToken).ConfigureAwait(false);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "sandbox";
            var disposition = new ContentDispositionHeaderValue(contentType.StartsWith("image/", StringComparison.Ordinal) ? "inline" : "attachment");
            disposition.SetHttpFileName(fileName);
            Response.Headers.ContentDisposition = disposition.ToString();
            return File(bytes, contentType);
        }).ConfigureAwait(false);

    [HttpPatch("{id}")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    [RequestSizeLimit(ActionBodyLimit)]
    public async Task<IActionResult> Update([Dto.Common.PublicId("cap")] int id, [FromBody] DocumentMetadataRequest request,
        CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.UpdateMetadataAsync(id,
            new DocumentMetadataInput(request.Title, request.Category, request.DocumentDate, request.Notes), cancellationToken)
            .ConfigureAwait(false))).ConfigureAwait(false);

    [HttpPost("{id}/links")]
    [RequestSizeLimit(ActionBodyLimit)]
    public async Task<IActionResult> Link([Dto.Common.PublicId("cap")] int id, [FromBody] DocumentLinkRequest request,
        CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.LinkAsync(id, request.TargetType, request.TargetId, cancellationToken)
            .ConfigureAwait(false))).ConfigureAwait(false);

    [HttpDelete("{id}/links/{targetType}/{targetId}")]
    public async Task<IActionResult> Unlink([Dto.Common.PublicId("cap")] int id, string targetType, string targetId,
        CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.UnlinkAsync(id, targetType, targetId, cancellationToken).ConfigureAwait(false)))
            .ConfigureAwait(false);

    [HttpPost("{id}/archive")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    public async Task<IActionResult> Archive([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.ArchiveAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpPost("{id}/unarchive")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    public async Task<IActionResult> Unarchive([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.UnarchiveAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpDelete("{id}")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    public async Task<IActionResult> Trash([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.TrashAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpPost("{id}/restore")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    public async Task<IActionResult> Restore([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () => Ok(await documents.RestoreAsync(id, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);

    [HttpDelete("{id}/permanent")]
    [RequireOperation(AccessOperationCatalog.DocumentsManage)]
    public async Task<IActionResult> DeletePermanently([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken) =>
        await Run(async () =>
        {
            await documents.DeletePermanentlyAsync(id, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }).ConfigureAwait(false);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (DocumentCaptureServiceException ex)
        {
            return ServiceProblem(ex);
        }
        catch (DocumentCaptureAdmissionBusyException)
        {
            Response.Headers.RetryAfter = "1";
            return ErrorProblem(503, "DOCUMENT_BUSY");
        }
    }

    private ObjectResult ServiceProblem(DocumentCaptureServiceException exception)
    {
        if (exception.StatusCode == StatusCodes.Status404NotFound && exception.ErrorCode == "DOCUMENT_NOT_FOUND")
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });
        if (exception.StatusCode is 429 or 503)
            Response.Headers.RetryAfter = "1";
        var problem = new ProblemDetails { Status = exception.StatusCode, Title = exception.ErrorCode };
        problem.Extensions["code"] = exception.ErrorCode;
        if (exception.CaptureId is not null)
            problem.Extensions["documentId"] = exception.CaptureId;
        switch (exception)
        {
            case DocumentLimitException limit:
                problem.Extensions["maxBytes"] = limit.Limit.MaxBytes;
                problem.Extensions["maxPdfPages"] = limit.Limit.MaxPdfPages;
                break;
            case DocumentStorageLimitException storage:
                problem.Extensions["usedBytes"] = storage.UsedBytes;
                problem.Extensions["limitBytes"] = storage.LimitBytes;
                problem.Extensions["requestedBytes"] = storage.RequestedBytes;
                problem.Extensions["upgradeAvailable"] = entitlements.Get(tenant.RequireCompanyId()).SelfServicePlans.Count > 0;
                break;
        }
        return new ObjectResult(problem) { StatusCode = exception.StatusCode };
    }

    private static ObjectResult ErrorProblem(int statusCode, string code)
    {
        var problem = new ProblemDetails { Status = statusCode, Title = code };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static async Task<byte[]> ReadBoundedAsync(IFormFile file, CancellationToken cancellationToken)
    {
        const int ceiling = Core.Entitlements.DocumentStoragePolicy.StoredMaxFileBytes;
        if (file.Length > ceiling)
            throw new DocumentCaptureServiceException(413, "DOCUMENT_TOO_LARGE");
        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream((int)Math.Min(file.Length, ceiling));
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > ceiling)
                throw new DocumentCaptureServiceException(413, "DOCUMENT_TOO_LARGE");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }
}

public sealed class DocumentUploadForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string? Notes { get; set; }
    public string? LinkTargetType { get; set; }
    public string? LinkTargetId { get; set; }
    public bool Force { get; set; }
}

public sealed record DocumentMetadataRequest(string? Title, string? Category, DateTime? DocumentDate, string? Notes);

public sealed record DocumentLinkRequest(string TargetType, string TargetId);
