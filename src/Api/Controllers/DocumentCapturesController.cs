// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures;
using Api.Filters;
using Api.PublicIds;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Api.Controllers;

[ApiController]
[Authorize]
[TenantScoped]
[Route("api/document-captures")]
public sealed class DocumentCapturesController(
    DocumentCaptureService captures,
    ITenantContext tenant,
    IEntitlementService entitlements,
    IOptions<DocumentCaptureOptions> options,
    IDocumentFieldExtractor extractor) : ControllerBase
{
    private const int UploadBodyLimit = DocumentCaptureImageLimits.MaxInputBytes + 64 * 1024;
    private const int AcceptBodyLimit = 256 * 1024;
    private const int LinkBodyLimit = 4 * 1024;
    private const int LinkLookupBodyLimit = 1 * 1024;

    [HttpGet("capabilities")]
    public IActionResult Capabilities()
    {
        var enabled = options.Value.Enabled;
        var providerAvailable = extractor.IsConfigured;
        var kinds = tenant.IsResolved
            ? DocumentCaptureFieldSchemas.SupportedKinds.Where(kind =>
                DocumentCaptureAccessPolicy.AuthorizeNew(entitlements, tenant.RequireCompanyId(), tenant.Permissions, kind)
                == DocumentCaptureAccessFailure.None).ToArray()
            : Array.Empty<string>();
        return Ok(new
        {
            enabled,
            providerAvailable,
            kinds,
            schemaVersion = DocumentCaptureFieldSchemas.CurrentSchemaVersion,
            maxUploadBytes = DocumentCaptureImageLimits.MaxInputBytes,
            maxPdfPages = DocumentCapturePdfLimits.MaxPages,
            maxImageWidth = DocumentCaptureImageLimits.MaxDimensionPixels,
            maxImageHeight = DocumentCaptureImageLimits.MaxDimensionPixels,
            maxImagePixels = DocumentCaptureImageLimits.MaxTotalPixels,
            allowedMimeTypes = new[] { "image/jpeg", "image/png", "application/pdf" }
        });
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadBodyLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadBodyLimit, ValueLengthLimit = 8 * 1024)]
    public async Task<IActionResult> Upload([FromForm] DocumentCaptureUploadRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            return ErrorProblem(400, "DOCUMENT_EMPTY");
        try
        {
            var source = await ReadBoundedAsync(request.File, cancellationToken).ConfigureAwait(false);
            var capture = await captures.UploadAsync(request.Kind, request.SchemaVersion, request.File.ContentType,
                source, Request.Headers["Idempotency-Key"].FirstOrDefault(), cancellationToken, request.Force).ConfigureAwait(false);
            return Ok(capture);
        }
        catch (DocumentCaptureServiceException ex)
        {
            return ServiceProblem(ex);
        }
        catch (DocumentCaptureMediaException ex)
        {
            return ErrorProblem(StatusForMediaError(ex.ErrorCode), ex.ErrorCode);
        }
        catch (DocumentCaptureAdmissionBusyException)
        {
            Response.Headers.RetryAfter = "1";
            return ErrorProblem(503, "DOCUMENT_BUSY");
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await captures.GetAsync(id, cancellationToken).ConfigureAwait(false));
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

    [HttpGet("{id}/image")]
    [HttpGet("{id}/bytes")]
    public async Task<IActionResult> GetBytes([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await captures.GetBytesAsync(id, cancellationToken).ConfigureAwait(false);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            var fileName = result.ContentType switch
            {
                "application/pdf" => "document.pdf",
                "image/jpeg" => "document.jpg",
                _ => "document.png"
            };
            return File(result.Bytes, result.ContentType, fileName);
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

    [HttpPost("{id}/accept")]
    [RequestSizeLimit(AcceptBodyLimit)]
    public async Task<IActionResult> Accept(
        [Dto.Common.PublicId("cap")] int id,
        [FromBody] DocumentCaptureAcceptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var fieldsJson = request.Fields.ToString(Formatting.None);
            return Ok(await captures.AcceptAsync(id, fieldsJson, cancellationToken).ConfigureAwait(false));
        }
        catch (PublicIdSerializationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return ErrorProblem(400, "DOCUMENT_ACCEPT_INVALID");
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

    [HttpPost("{id}/reject")]
    [RequestSizeLimit(LinkBodyLimit)]
    public async Task<IActionResult> Reject([Dto.Common.PublicId("cap")] int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await captures.RejectAsync(id, cancellationToken).ConfigureAwait(false));
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

    [HttpPost("{id}/links")]
    [HttpPost("{id}/link")]
    [RequestSizeLimit(LinkBodyLimit)]
    public async Task<IActionResult> Link(
        [Dto.Common.PublicId("cap")] int id,
        [FromBody] DocumentCaptureLinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await captures.LinkAsync(id, request.TargetType, request.TargetId, cancellationToken).ConfigureAwait(false));
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

    [HttpGet("linked/{targetType}/{targetId}")]
    [RequestSizeLimit(LinkLookupBodyLimit)]
    public async Task<IActionResult> GetLinked(string targetType, string targetId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await captures.GetLinkedAsync(targetType, targetId, cancellationToken).ConfigureAwait(false));
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

    private static ObjectResult ErrorProblem(int statusCode, string code, string? captureId = null)
    {
        var problem = new ProblemDetails { Status = statusCode, Title = code };
        problem.Extensions["code"] = code;
        if (captureId is not null)
            problem.Extensions["captureId"] = captureId;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private ObjectResult ServiceProblem(DocumentCaptureServiceException exception)
    {
        if (exception.StatusCode == StatusCodes.Status404NotFound && exception.ErrorCode == "DOCUMENT_NOT_FOUND")
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });
        if (exception.EntitlementDecision is { } decision)
        {
            var problem = EntitlementProblemFactory.FromDecision(decision, tenant, entitlements.Options.SupportEmail,
                exception.RequiredModules);
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        }
        if (exception.StatusCode == 503 && exception.ErrorCode == "DOCUMENT_BUSY")
            Response.Headers.RetryAfter = "1";
        return ErrorProblem(exception.StatusCode, exception.ErrorCode, exception.CaptureId);
    }

    private static int StatusForMediaError(string code) => code switch
    {
        "DOCUMENT_EMPTY" or "DOCUMENT_MALFORMED" or "DOCUMENT_DIMENSIONS" => 400,
        "DOCUMENT_TOO_LARGE" => 413,
        "DOCUMENT_UNSUPPORTED_TYPE" => 415,
        "DOCUMENT_DECODE_BUSY" or "DOCUMENT_RATE_LIMITED" => 429,
        _ => 422
    };

    private static async Task<byte[]> ReadBoundedAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > DocumentCaptureImageLimits.MaxInputBytes)
            throw new DocumentCaptureServiceException(413, "DOCUMENT_TOO_LARGE");
        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream((int)Math.Min(file.Length, DocumentCaptureImageLimits.MaxInputBytes));
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > DocumentCaptureImageLimits.MaxInputBytes)
                throw new DocumentCaptureServiceException(413, "DOCUMENT_TOO_LARGE");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }
}

public sealed class DocumentCaptureUploadRequest
{
    [BindRequired]
    public string Kind { get; set; } = string.Empty;

    [BindRequired]
    public int SchemaVersion { get; set; }

    [BindRequired]
    public IFormFile? File { get; set; }

    public bool Force { get; set; }
}

public sealed record DocumentCaptureAcceptRequest(JToken Fields);
public sealed record DocumentCaptureLinkRequest(string TargetType, string TargetId);
