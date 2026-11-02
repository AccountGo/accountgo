using System;

namespace Api.Service;

public static class GoogleIdentityPolicy
{
    // Google is authoritative for Gmail and for the Workspace domain in the signed hd claim.
    public static bool IsAuthoritativeEmail(string email, string? hostedDomain)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        var domain = email[(at + 1)..];
        return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(hostedDomain) && domain.Equals(hostedDomain, StringComparison.OrdinalIgnoreCase));
    }
}
