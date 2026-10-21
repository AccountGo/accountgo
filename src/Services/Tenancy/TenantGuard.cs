#nullable enable
using System;
using Core.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Tenancy
{
    public class TenantGuard : ITenantGuard
    {
        private readonly IServiceProvider _serviceProvider;

        public TenantGuard(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public T EnsureOwned<T>(int id, string? paramName = null) where T : BaseEntity, ICompanyOwned
        {
            if (id <= 0)
            {
                throw new TenantEntityNotFoundException(typeof(T).Name, id, paramName);
            }

            var repo = _serviceProvider.GetRequiredService<IRepository<T>>();
            var entity = repo.GetById(id);
            if (entity == null)
            {
                throw new TenantEntityNotFoundException(typeof(T).Name, id, paramName);
            }

            return entity;
        }

        public T? EnsureOwned<T>(int? id, string? paramName = null) where T : BaseEntity, ICompanyOwned
        {
            if (!id.HasValue || id.Value <= 0)
                return null;

            return EnsureOwned<T>(id.Value, paramName);
        }
    }
}
