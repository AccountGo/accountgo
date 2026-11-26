// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Api.DocumentCaptures;

/// <summary>
/// The transaction a record delete runs in when documents may link to the record (draft bill, draft invoice, client, vendor):
/// the company admission lock first, then the record's own row, so it can't interleave with a document link, unlink or trash
/// on the same record (design 3.11 U1). Returns null when the company lock is busy.
/// </summary>
public static class DocumentRecordDeletion
{
    public static async Task<IDbContextTransaction?> BeginAsync(IServiceProvider services, ApiDbContext db, int companyId,
        string targetType, int targetId, CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction;
        try
        {
            transaction = await services.GetRequiredService<IDocumentCaptureAdmissionLock>()
                .AcquireCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);
        }
        catch (DocumentCaptureAdmissionBusyException)
        {
            return null;
        }
        try
        {
            await DocumentTargetLocks.LockAsync(db, companyId, [(targetType, targetId)], cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static ObjectResult Busy(HttpResponse response)
    {
        response.Headers.RetryAfter = "1";
        var problem = new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = "DOCUMENT_BUSY" };
        problem.Extensions["code"] = "DOCUMENT_BUSY";
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }
}
