using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain.Auditing;

namespace Services.Auditing
{   
    /*
        Service for managing auditable attributes.
    */
    public class AuditableAttributeService : BaseService, IAuditableAttributeService
    {
        private readonly IRepository<AuditableAttribute> _attributeRepo;

        // Constructor
        public AuditableAttributeService(IRepository<AuditableAttribute> attributeRepo)
            : base(null, null, null, null)
        {
            _attributeRepo = attributeRepo;
        }

        // Retrieve attributes by auditable entity ID
        public IEnumerable<AuditableAttribute> GetAuditableAttributesByEntityId(int entityId)
        {
            return _attributeRepo.Table.Where(a => a.AuditableEntityId == entityId).ToList();
        }

        // Retrieve a specific auditable attribute by its ID
        public AuditableAttribute GetAuditableAttributeById(int id)
        {
            return _attributeRepo.GetById(id);
        }

        // Create a new auditable attribute
        public void CreateAuditableAttribute(AuditableAttribute dto)
        {
            _attributeRepo.Insert(dto);
        }

        // Update an existing auditable attribute
        public void UpdateAuditableAttribute(AuditableAttribute dto)
        {
            _attributeRepo.Update(dto);
        }

        // Delete an auditable attribute by its ID
        public void DeleteAuditableAttribute(int id)
        {
            var attribute = _attributeRepo.GetById(id);
            if (attribute == null)
            {
                return;
            }
            _attributeRepo.Delete(attribute);
        }
    }
}