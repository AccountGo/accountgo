#nullable enable
using System;

namespace Core.Tenancy
{
    public abstract class TenantException : Exception
    {
        protected TenantException(string message)
            : base(message)
        {
        }

        protected TenantException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public class TenantNotResolvedException : TenantException
    {
        public TenantNotResolvedException(string message = "Tenant context has not been resolved.")
            : base(message)
        {
        }
    }

    public class CrossTenantWriteException : TenantException
    {
        public CrossTenantWriteException(string message = "Attempted cross-tenant write operation was rejected.")
            : base(message)
        {
        }
    }

    public class TenantEntityNotFoundException : TenantException
    {
        public string EntityType { get; }
        public int EntityId { get; }
        public string? ParameterName { get; }

        public TenantEntityNotFoundException(string entityType, int entityId, string? parameterName = null)
            : base($"The referenced {entityType} with ID {entityId} was not found or does not belong to the active workspace.")
        {
            EntityType = entityType;
            EntityId = entityId;
            ParameterName = parameterName;
        }
    }
}
