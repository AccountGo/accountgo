using System.Collections.Generic;
using Core.Domain.Auditing;

namespace Services.Auditing
{   
    /*
        Interface for managing auditable attributes.
    */
    public interface IAuditableAttributeService
    {
        IEnumerable<AuditableAttribute> GetAuditableAttributesByEntityId(int entityId);
        AuditableAttribute GetAuditableAttributeById(int id);
        void CreateAuditableAttribute(AuditableAttribute dto);
        void UpdateAuditableAttribute(AuditableAttribute dto);
        void DeleteAuditableAttribute(int id);
    }
}