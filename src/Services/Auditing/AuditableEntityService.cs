using Core.Data;
using Core.Domain.Auditing;
using System.Collections.Generic;
using System.Linq;

namespace Services.Auditing
{   
    /*
        Service for managing auditable entities.
    */
    public class AuditableEntityService : BaseService, IAuditableEntityService
    {

        private readonly IRepository<AuditableEntity> _entityRepo;

        // Constructor
        public AuditableEntityService(IRepository<AuditableEntity> entityRepo)
            : base(null, null, null, null)
        {
            _entityRepo = entityRepo;
        }

        // Retrieve all auditable entities
        public IEnumerable<AuditableEntity> GetAllAuditableEntities()
        {
            return _entityRepo.GetAllIncluding(e => e.AuditableAttributes).ToList();
        }

        // Retrieve a specific auditable entity by its ID
        public AuditableEntity GetAuditableEntityById(int id)
        {
            return _entityRepo.GetAllIncluding(e => e.AuditableAttributes).FirstOrDefault(e => e.Id == id);
        }

        // Create a new auditable entity
        public void CreateAuditableEntity(AuditableEntity entity)
        {
            _entityRepo.Insert(entity);
        }

        // Update an existing auditable entity
        public void UpdateAuditableEntity(AuditableEntity entity)
        {
            _entityRepo.Update(entity);
        }

        // Delete an auditable entity by its ID
        public void DeleteAuditableEntity(int id)
        {
            var entity = _entityRepo.GetById(id);
            if (entity == null)
            {
                return;
            }
            _entityRepo.Delete(entity);
        }
    }
}