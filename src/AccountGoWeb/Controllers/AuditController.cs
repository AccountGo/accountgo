using System.Text;
using Dto.Auditing;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;


namespace AccountGoWeb.Controllers
{   
    /*
        This controller provides web views for managing auditable entities and attributes.
        NOTE: Manages both Auditable Entities and Auditable Attributes.
    */
    public class AuditController : BaseController
    {
        private readonly ILogger<AuditController> _logger;

        public AuditController(IConfiguration config, ILogger<AuditController> logger)
        {
            _baseConfig = config;
            _logger     = logger;
        }

        // #####Auditable Entities#####

        // Returns a view listing all auditable entities
        public async Task<IActionResult> GetAuditableEntities()
        {
            ViewBag.PageContentHeader = "Auditable Entities";

            var entities = await GetAsync<List<AuditableEntity>>("audit/entities");
            return View(entities);
        }

        // Returns a view for a specific auditable entity by ID
        public async Task<IActionResult> GetEntity(int? id = null)
        {
            AuditableEntity model;

            if(id == null)
            {   
                // If no ID is provided, create a new AuditableEntity model
                model = new AuditableEntity()
                {
                    EnableAudit = true
                };
            }
            else
            {
                model = await GetAsync<AuditableEntity>($"audit/entity?id={id}");
            }

            ViewBag.PageContentHeader = "Auditable Entity";
            return View(model);
        }

        // Saves an auditable entity (new or existing). This do updates via POST.
        [HttpPost]
        public async Task<IActionResult> SaveEntity(AuditableEntity model)
        {
            if (!ModelState.IsValid)
            {
                return View("GetEntity", model);
            }

            var json = JsonConvert.SerializeObject(model);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await PostAsync("audit/entity", content);

            return RedirectToAction(nameof(GetAuditableEntities));
        }

        public async Task<IActionResult> DeleteEntity(int id)
        {
            await DeleteAsync($"audit/entity/{id}");
            return RedirectToAction(nameof(GetAuditableEntities));
        }



        // #####Auditable Attributes#####

        // Returns a view listing all auditable attributes for a specific entity
        public async Task<IActionResult> GetAuditableAttributes(int entityId)
        {
            ViewBag.PageContentHeader = "Auditable Attributes";
            ViewBag.EntityId          = entityId;

            var attributes = await GetAsync<List<AuditableAttribute>>($"audit/attributes?entityId={entityId}");
            return View(attributes);
        }

        // Returns a view for a specific auditable attribute by ID or a new one if no ID is provided.
        public async Task<IActionResult> GetAttribute(int? id, int entityId)
        {
            AuditableAttribute model;

            if (id == null)
            {
                // If no ID is provided, create new AuditableAttribute
                model = new AuditableAttribute()
                {
                    AuditableEntityId = entityId,
                    EnableAudit       = true
                };
            }
            else
            {
                model = await GetAsync<AuditableAttribute>($"audit/attribute?id={id}");
            }

            ViewBag.PageContentHeader = "Auditable Attribute";
            ViewBag.EntityId          = entityId;

            return View(model);
        }

        // Saves an auditable attribute (new or existing). This do updates via POST.
        [HttpPost]
        public async Task<IActionResult> SaveAttribute(AuditableAttribute model, int entityId)
        {
            if (!ModelState.IsValid)
            {                
                return View("GetAttribute", model);
            }

            // This line make sure the attribute is linked to the correct entity
            model.AuditableEntityId = entityId;

            var json = JsonConvert.SerializeObject(model);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await PostAsync("audit/attribute", content);

            return RedirectToAction(nameof(GetAuditableAttributes), new { entityId });
        }

        // Deletes an auditable attribute by ID
        public async Task<IActionResult> DeleteAttribute(int id, int entityId)
        {
            await DeleteAsync($"audit/attribute/{id}");
            return RedirectToAction(nameof(GetAuditableAttributes), new { entityId });
    
        }
    }
}