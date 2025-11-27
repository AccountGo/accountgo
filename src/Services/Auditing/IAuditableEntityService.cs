using Core.Domain.Auditing;
using System.Collections.Generic;

namespace Services.Auditing
{   
    /*
        Interface for managing auditable entities.
    */
    public interface IAuditableEntityService
    {
        IEnumerable<AuditableEntity> GetAllAuditableEntities();
        AuditableEntity GetAuditableEntityById(int id);
        void CreateAuditableEntity(AuditableEntity entity);
        void UpdateAuditableEntity(AuditableEntity entity);
        void DeleteAuditableEntity(int id);
    }
}