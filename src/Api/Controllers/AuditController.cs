using System.ComponentModel;
using System.Diagnostics;
using Dto.Auditing;
using Microsoft.AspNetCore.Mvc;
using Services.Auditing;


namespace Api.Controllers
{   
    /*
        This controller provides API endpoints for managing auditable entities and attributes.
        NOTE: Manages both Auditable Entities and Auditable Attributes.
    */
    [Route("api/[controller]")]
    public class AuditController : BaseController
    {
        private readonly IAuditableEntityService _entityService;
        private readonly IAuditableAttributeService _attributeService;

        // Constructor
        public AuditController(
            IAuditableEntityService entityService,
            IAuditableAttributeService attributeService
        )
        {
            _entityService    = entityService;
            _attributeService = attributeService;
        }
        

        // #####Auditable Entities#####

        // GET: api/Audit/Entities
        // Returns a list of all auditable entities
        [HttpGet]
        [Route("Entities")]
        public IActionResult GetAuditableEntities()
        {
            var entities = _entityService.GetAllAuditableEntities();
            
            var dtoList = entities.Select(e => new AuditableEntity {
                Id          = e.Id,
                EntityName  = e.EntityName,
                EnableAudit = e.EnableAudit
            }).ToList();

            return new ObjectResult(dtoList);
         }


        // GET: api/Audit/Entity?id=1
        // Returns a specific auditable entity by ID
        [HttpGet]
        [Route("Entity")]
        public IActionResult GetEntity(int id)
        {
            var e = _entityService.GetAuditableEntityById(id);
            if (e == null)
            {
                return NotFound();
            }

            var dto = new AuditableEntity
            {
                Id          = e.Id,
                EntityName  = e.EntityName,
                EnableAudit = e.EnableAudit
            };

            return new ObjectResult(dto);
        }


        // POST: api/Audit/Entity
        // Creates or updates an auditable entity
        [HttpPost]
        [Route("Entity")]
        public IActionResult SaveEntity([FromBody] AuditableEntity model)
        {   
            // Validate the model
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }


            Core.Domain.Auditing.AuditableEntity entity;

            // This flag indicates whether creating a new entity or updating an existing one
            Boolean isNew = model.Id == 0;
            if (isNew)
            {
                entity = new Core.Domain.Auditing.AuditableEntity();
            }
            else
            {   
                // Why ! at the end of the line? to suppress nullable warning.
                entity = _entityService.GetAuditableEntityById(model.Id)!;
                if (entity == null)
                {
                    return NotFound();
                }
            }

            entity.EntityName  = model.EntityName;
            entity.EnableAudit = model.EnableAudit;

            if (isNew)
            {
                _entityService.CreateAuditableEntity(entity);
            }
            else
            {
                _entityService.UpdateAuditableEntity(entity);
            }
            
            return Ok();
        }

        // DELETE: api/Audit/Entity/1
        // Deletes an auditable entity by ID
        [HttpDelete]
        [Route("Entity/{id}")]
        public IActionResult DeleteEntity(int id)
        {
            var entity = _entityService.GetAuditableEntityById(id);
            if (entity == null)
            {
                return NotFound();
            }

            _entityService.DeleteAuditableEntity(id);
            return Ok();
        }



        // #####Auditable Attributes#####

        // GET: api/Audit/Attributes?entityId=1
        // Returns a list of auditable attributes for a specific auditable entity id.
        [HttpGet]
        [Route("Attributes")]
        public IActionResult GetAuditableAttributes(int entityId)
        {
            var attributes = _attributeService.GetAuditableAttributesByEntityId(entityId);
            
            var dtoList = attributes.Select(a => new AuditableAttribute {
                Id               = a.Id,
                AuditableEntityId = a.AuditableEntityId,
                AttributeName    = a.AttributeName,
                EnableAudit      = a.EnableAudit
            }).ToList();

            return new ObjectResult(dtoList);
        }
    

        // GET: api/Audit/Attribute?id=1
        // Returns a specific auditable attribute by its ID
        [HttpGet]
        [Route("Attribute")]
        public IActionResult GetAttribute(int id)
        {
            var a = _attributeService.GetAuditableAttributeById(id);
            if (a == null)
            {
                return NotFound();
            }

            var dto = new AuditableAttribute
            {
                Id                = a.Id,
                AuditableEntityId = a.AuditableEntityId,
                AttributeName     = a.AttributeName,
                EnableAudit       = a.EnableAudit
            };

            return new ObjectResult(dto);
        }


        // POST: api/Audit/Attribute
        // Creates or updates an auditable attribute
        [HttpPost]
        [Route("Attribute")]
        public IActionResult SaveAttribute([FromBody] AuditableAttribute model)
        {   
            // Validate the model
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Core.Domain.Auditing.AuditableAttribute attribute;
            bool isNew = model.Id == 0;

            if (isNew)
            {
                attribute = new Core.Domain.Auditing.AuditableAttribute();
            }
            else
            {   
                attribute = _attributeService.GetAuditableAttributeById(model.Id)!;
                if (attribute == null)
                {
                    return NotFound();
                }
            }

            attribute.AuditableEntityId = model.AuditableEntityId;
            attribute.AttributeName     = model.AttributeName;
            attribute.EnableAudit       = model.EnableAudit;

            if (isNew)
            {
                _attributeService.CreateAuditableAttribute(attribute);
            }
            else
            {
                _attributeService.UpdateAuditableAttribute(attribute);
            }

            return Ok();
        }

        // DELETE: api/Audit/Attribute/1
        // Deletes an auditable attribute by ID
        [HttpDelete]
        [Route("Attribute/{id}")]
        public IActionResult DeleteAttribute(int id)
        {
            var attribute = _attributeService.GetAuditableAttributeById(id);
            if (attribute == null)
            {
                return NotFound();
            }

            _attributeService.DeleteAuditableAttribute(id);
            return Ok();
        }
    }
}