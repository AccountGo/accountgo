using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Api.Service
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly Services.Security.ISecurityService _securityService;

        private ApplicationUser? _user;

        public AuthenticationService(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            Services.Security.ISecurityService securityService)
        {
            _userManager = userManager;
            _configuration = configuration;
            _securityService = securityService;
        }

        public async Task<Token> CreateToken(ApplicationUser user, bool populateExp)
        {
            _user = user;
            var signingCredentials = GetSigningCredentials();
            var claims = await GetClaimsAsync();
            var tokenOptions = GenerateTokenOptions(signingCredentials, claims);
            var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenOptions);

            return new Token(accessToken, string.Empty);
        }

        private SigningCredentials GetSigningCredentials()
        {
            var secretKey = _configuration["SECRET_KEY"] ?? _configuration["SECRET:key"] ?? _configuration["SECRET__key"] ?? Environment.GetEnvironmentVariable("SECRET_KEY");
            if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
            {
                throw new InvalidOperationException("FATAL: SECRET_KEY (or SECRET:key) is missing or shorter than 32 bytes.");
            }

            var key = Encoding.UTF8.GetBytes(secretKey);
            var secret = new SymmetricSecurityKey(key);

            return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
        }

        private async Task<List<Claim>> GetClaimsAsync()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, _user!.Id),
                new Claim(ClaimTypes.Name, _user.UserName ?? _user.Email ?? string.Empty),
                new Claim(ClaimTypes.Email, _user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // 1. ASP.NET Identity roles
            var identityRoles = await _userManager.GetRolesAsync(_user);
            foreach (var role in identityRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // 2. Domain Security roles
            var domainUser = _securityService.GetUser(_user.UserName ?? _user.Email ?? string.Empty);
            if (domainUser != null && domainUser.Roles != null)
            {
                foreach (var role in domainUser.Roles)
                {
                    if (role?.SecurityRole != null && !claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role.SecurityRole.Name))
                    {
                        claims.Add(new Claim(ClaimTypes.Role, role.SecurityRole.Name));
                    }
                }
            }

            return claims;
        }

        private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
        {
            var validIssuer = _configuration["JwtSettings:validIssuer"];
            var validAudience = _configuration["JwtSettings:validAudience"];
            var expires = _configuration["JwtSettings:expires"] ?? "15";

            var tokenOptions = new JwtSecurityToken
            (
                issuer: validIssuer,
                audience: validAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(expires)),
                signingCredentials: signingCredentials
            );

            return tokenOptions;
        }
    }
}
