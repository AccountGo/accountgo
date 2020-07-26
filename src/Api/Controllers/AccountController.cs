using Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Security;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class AccountController : BaseController
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAdministrationService _administrationService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IAdministrationService administrationService,
            ISecurityService securityService
            )
        {
            _userManager = userManager;
            _administrationService = administrationService;
        }

        [HttpPost]
        [Route("[action]")]
        public async System.Threading.Tasks.Task<IActionResult> SignIn([FromBody]dynamic loginViewModel)
        {
            if (loginViewModel == null)
            {
                throw new System.ArgumentNullException(nameof(loginViewModel));
            }
            //var error = await _signInManager.PreSignInCheck(user);
            //if (error != null)
            //{
            //    return error;
            //}

            //if (await IsLockedOut(user))
            //{
            //    return await LockedOut(user);
            //}
            string password = loginViewModel.Password;
            string username = loginViewModel.Email;
            
            try
            {
                var applicationUser = await _userManager.FindByEmailAsync(username);
                if (applicationUser == null)
                {
                    System.Console.WriteLine($"Unable to load user with email '{username}'.");
                }
                if (await _userManager.CheckPasswordAsync(applicationUser, password))
                {
                    //await ResetLockout(user);
                    return new ObjectResult(_userManager.FindByEmailAsync(applicationUser.Email));
                }
            }
            catch(System.Exception ex)
            {
                System.Console.WriteLine(ex.StackTrace);
            }


            //Logger.LogWarning(2, "User {userId} failed to provide the correct password.", await UserManager.GetUserIdAsync(user));

            //if (_userManager.SupportsUserLockout && lockoutOnFailure)
            //{
            //    // If lockout is requested, increment access failed count which might lock out the user
            //    await _userManager.AccessFailedAsync(user);
            //    if (await _userManager.IsLockedOutAsync(user))
            //    {
            //        return await LockedOut(user);
            //    }
            //}
            //return SignInResult.Failed;
            // If we got this far, something failed, redisplay form
            return new BadRequestObjectResult(Microsoft.AspNetCore.Identity.SignInResult.Failed);
        }

        [HttpPost]
        [Route("[action]")]
        public async System.Threading.Tasks.Task<IActionResult> AddNewUser([FromBody]dynamic registerViewModel)
        {
            try
            {
                if (registerViewModel == null)
                {
                    throw new System.ArgumentNullException(nameof(registerViewModel));
                }

                string password = registerViewModel.Password;
                string username = registerViewModel.Email;
                string firstName = registerViewModel.FirstName;
                string lastName = registerViewModel.LastName;

                var user = new ApplicationUser { UserName = username, Email = username };
                var result = await _userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    Core.Domain.Security.User newUser =
                        new Core.Domain.Security.User
                        {
                            EmailAddress = username,
                            UserName = username,
                            Firstname = firstName,
                            Lastname = lastName
                        };

                    _administrationService.SaveUser(newUser);

                    return new ObjectResult(result);
                }
                return new BadRequestObjectResult(result);
            }
            catch(System.Exception ex)
            {
                var errors = new[] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }
    }
}
