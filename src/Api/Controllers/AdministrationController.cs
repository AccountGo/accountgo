using Dto.Administration;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using System;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class AdministrationController : BaseController
    {
        private readonly IAdministrationService _adminService;

        public AdministrationController(IAdministrationService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Company(string companyCode)
        {
            if (string.IsNullOrEmpty(companyCode))
                return new ObjectResult(_adminService.GetDefaultCompany());
            else
                return new ObjectResult(_adminService.GetDefaultCompany());
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveCompany([FromBody]Company companyDto)
        {
            string[] errors = null;
            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;
                    return new BadRequestObjectResult(errors);
                }

                Core.Domain.Company company = null;

                if (companyDto.Id == 0)
                {
                    company = new Core.Domain.Company();
                }
                else
                {
                    company = _adminService.GetDefaultCompany();
                }
                
                company.CompanyCode = companyDto.CompanyCode;
                company.Name = companyDto.Name;
                company.ShortName = companyDto.ShortName;

                _adminService.SaveCompany(company);

                return new ObjectResult(Ok());
            }
            catch(Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }
    }
}
