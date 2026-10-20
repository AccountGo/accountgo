using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Security;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : BaseController
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAdministrationService _administrationService;
        private readonly ISecurityService _securityService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            IAdministrationService administrationService,
            ISecurityService securityService)
        {
            _userManager = userManager;
            _administrationService = administrationService;
            _securityService = securityService;
        }

        [Authorize(Roles = "Administrator,SystemAdministrators")]
        [HttpPost("AddNewUser")]
        public async Task<IActionResult> AddNewUser([FromBody] AddNewUserRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var username = request.Email.Trim();
                var user = new ApplicationUser
                {
                    UserName = username,
                    Email = username,
                    EmailConfirmed = true
                };

                var result = string.IsNullOrEmpty(request.Password)
                    ? await _userManager.CreateAsync(user)
                    : await _userManager.CreateAsync(user, request.Password);

                if (result.Succeeded)
                {
                    var newUser = new Core.Domain.Security.User
                    {
                        EmailAddress = username,
                        UserName = username,
                        Firstname = request.FirstName.Trim(),
                        Lastname = request.LastName.Trim()
                    };

                    _administrationService.SaveUser(newUser);

                    return Ok(new { success = true, email = username });
                }

                return BadRequest(new ProblemDetails
                {
                    Title = "Failed to Create User",
                    Detail = string.Join("; ", result.Errors.Select(e => e.Description)),
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Error Creating User",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
    }
}
