#nullable enable
using Core.Domain;

namespace Core.Tenancy
{
    public interface ITenantGuard
    {
        T EnsureOwned<T>(int id, string? paramName = null) where T : BaseEntity, ICompanyOwned;
        T? EnsureOwned<T>(int? id, string? paramName = null) where T : BaseEntity, ICompanyOwned;
    }
}
