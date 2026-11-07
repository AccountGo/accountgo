using System;

namespace Api.Service.Google;

public record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? HostedDomain,
    string? Nonce,
    string TokenId,
    DateTime ExpiresAtUtc
);
