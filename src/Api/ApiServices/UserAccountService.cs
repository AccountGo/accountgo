using Dto.Authentication;
using Dto.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using static Dto.Response.ServiceResponses;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System;
using Api.Data;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Core.Utilities;
using Microsoft.Identity.Client;

namespace Api.ApiServices
{
    public class UserAccountService(
                                    UserManager<ApplicationUser> userManager, 
                                    RoleManager<IdentityRole> roleManager, 
                                    IConfiguration config,
                                    IDateTimeProvider dateTimeProvider
                                    ) : IUserAccount
    {
        public async Task<GeneralResponse> CreateAccount(UserDTO userDTO)
        {
            if (userDTO is null) return new GeneralResponse(false, "Model is empty");
            var newUser = new ApplicationUser()
            {
                FirstName = userDTO.FirstName,
                LastName = userDTO.LastName,
                Email = userDTO.Email,
                UserName = userDTO.Email
            };
            var user = await userManager.FindByEmailAsync(newUser.Email);
            if (user is not null) return new GeneralResponse(false, "User registered already");

            var createUser = await userManager.CreateAsync(newUser!, userDTO.Password);
            if (!createUser.Succeeded) return new GeneralResponse(false, "Error occured.. please try again");

            //Assign Default Role : Admin to first registrar; rest is user
            var checkAdmin = await roleManager.FindByNameAsync("Admin");
            if (checkAdmin is null)
            {
                await roleManager.CreateAsync(new IdentityRole() { Name = "Admin" });
                await userManager.AddToRoleAsync(newUser, "Admin");
                return new GeneralResponse(true, "Account Created");
            }
            else
            {
                var checkUser = await roleManager.FindByNameAsync("User");
                if (checkUser is null)
                    await roleManager.CreateAsync(new IdentityRole() { Name = "User" });

                await userManager.AddToRoleAsync(newUser, "User");
                return new GeneralResponse(true, "Account Created");
            }
        }

        public async Task<LoginResponse> LoginAccount(LoginDTO loginDTO)
        {
            try
            {
                var error = new LoginResponse(false, null!, "Invalid email/password");

                if (loginDTO == null)
                    return error;

                var getUser = await userManager.FindByEmailAsync(loginDTO.Email);
                if (getUser is null)
                    return error;

                bool checkUserPasswords = await userManager.CheckPasswordAsync(getUser, loginDTO.Password);
                if (!checkUserPasswords)
                    return error;

                var getUserRole = await userManager.GetRolesAsync(getUser);

                string token = GenerateToken(getUser.Id, getUser.Name, getUser.Email, getUserRole.FirstOrDefault());
                return new LoginResponse(true, token!, "Login completed");
            }
            catch(Exception ex)
            {
                var systemError = new LoginResponse(false, null, "Something went wrong");
                systemError.Errors.Add(ex.Message);
                systemError.Errors.Add(ex.InnerException.Message);

                //TODO: log error

                return systemError;
            }

        }

        private string GenerateToken(string userId, string name, string email, string role)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(config["Jwt:Key"]);
            var securityKey = new SymmetricSecurityKey(key);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);

            var claimsIdentity = new ClaimsIdentity(new[]
            {
                new Claim("id", userId),
                new Claim("name", name),
                new Claim("email", email),
                new Claim("role", role)
            });

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claimsIdentity,
                Expires = DateTime.UtcNow.AddDays(30),
                SigningCredentials = credentials,
                Issuer = "localhost",
                Audience = "localhost"
            };
            var createdToken = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(createdToken);
        }
    }
}
