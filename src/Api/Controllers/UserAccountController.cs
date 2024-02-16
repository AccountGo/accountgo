using Api.Data;
using Dto.Authentication;
using Dto.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Security;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class UserAccountController(IUserAccount userAccount, ClaimsPrincipal claimsPrincipal) : BaseController
    {

        [AllowAnonymous]
        [HttpPost]
        [Route("[action]")]
        public async Task<IActionResult> SignIn([FromBody] LoginDTO model)
        {
            var signInResult = await userAccount.LoginAccount(model);

            if (signInResult.Flag)
                return Ok(signInResult);

            return Unauthorized(signInResult);
        }

        [HttpPost]
        [Route("[action]")]
        public async Task<IActionResult> Register([FromBody] UserDTO model)
        {
            var createAccountResult = await userAccount.CreateAccount(model);

            if (createAccountResult.Flag)
                return Ok(createAccountResult);

            return BadRequest(createAccountResult);
        }

        [HttpGet("[action]")]
        public string Test()
        {
            var a = claimsPrincipal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            return "Test success";
        }
    }
}
