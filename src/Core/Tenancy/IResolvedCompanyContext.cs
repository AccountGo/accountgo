#nullable enable
using Core.Domain;

namespace Core.Tenancy;

/// <summary>A company already loaded for the current tenant scope, when available.</summary>
public interface IResolvedCompanyContext
{
    Company? ResolvedCompany { get; }
}
