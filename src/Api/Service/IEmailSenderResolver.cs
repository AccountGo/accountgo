using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Service;

/// <summary>Server-constructed authorization context; client input never establishes company or actor identity.</summary>
public sealed record EmailDispatchContext(
    int CompanyId, string ActorId, int InvoiceId, string Purpose,
    string Mode = "platform", string? ApprovedSenderId = null, Guid? DispatchId = null);

/// <summary>A sender resolved from trusted configuration and organization policy.</summary>
public sealed record EmailSenderResolution(string? FromEmail, string? FromName, string? DomainId = null, string? SenderId = null);

/// <summary>Authorizes the selected sender before invoking email transport.</summary>
public interface IEmailSenderResolver
{
    /// <summary>Resolves trusted sender data or throws a safe policy denial.</summary>
    Task<EmailSenderResolution> ResolveAsync(EmailDispatchContext context, CancellationToken ct = default);
}

/// <summary>A policy failure that must be returned separately from provider failures.</summary>
public sealed class EmailPolicyException(string code, string message, int statusCode = 403) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Authorizes dispatch and records durable intent when supplied by an email module.</summary>
public interface IEmailDispatchService
{
    /// <summary>Sends an invoice message and returns its acceptance and history correlation.</summary>
    Task<EmailDispatchResult> SendAsync(EmailDispatchContext context, EmailMessage message, CancellationToken ct = default);

    /// <summary>Reports organization/actor/resource-bound acceptance certainty without authorizing a new send.</summary>
    Task<EmailDispatchDisposition> GetDispositionAsync(EmailDispatchContext context, CancellationToken ct = default) =>
        Task.FromResult(new EmailDispatchDisposition("Unknown", false));

    /// <summary>Durably closes a safe dispatch ID, preventing later admission or transport by an older request.</summary>
    Task<EmailDispatchDisposition> AbandonAsync(EmailDispatchContext context, CancellationToken ct = default) =>
        throw new EmailPolicyException("EMAIL_DISPATCH_DISPOSITION_UNAVAILABLE", "Dispatch certainty cannot be established by this server.", 409);

    /// <summary>Links provider acceptance to the existing invoice history without duplicating histories on retry.</summary>
    Task RecordHistoryAsync(EmailDispatchContext context, int historyId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Provider acceptance is distinct from confirmed delivery.</summary>
public sealed record EmailDispatchResult(string? ProviderMessageId, Guid? DispatchId = null, int? HistoryId = null);

/// <summary>Only a successful atomic abandonment permits replacing an unresolved custom attempt.</summary>
public sealed record EmailDispatchDisposition(string State, bool CanAbandon);

/// <summary>Preserves platform sending and explicitly denies unsupported sending modes.</summary>
public sealed class PlatformEmailSenderResolver : IEmailSenderResolver
{
    public Task<EmailSenderResolution> ResolveAsync(EmailDispatchContext context, CancellationToken ct = default)
    {
        if (context.Mode != "platform" || context.ApprovedSenderId != null)
            throw new EmailPolicyException("CUSTOM_EMAIL_UNAVAILABLE", "Custom email is unavailable. Select the platform sender explicitly.");
        return Task.FromResult(new EmailSenderResolution(null, null));
    }
}

/// <summary>Default dispatch preserves the configured platform sender and existing transport behavior.</summary>
public sealed class PlatformEmailDispatchService(IEmailSenderResolver resolver, IEmailSender sender) : IEmailDispatchService
{
    public async Task<EmailDispatchResult> SendAsync(EmailDispatchContext context, EmailMessage message, CancellationToken ct = default)
    {
        await resolver.ResolveAsync(context, ct);
        return new EmailDispatchResult(await sender.SendAsync(message, ct));
    }
}
