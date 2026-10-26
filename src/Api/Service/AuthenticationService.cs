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

        public AuthenticationService(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            Services.Security.ISecurityService securityService)
        {
            _userManager = userManager;
            _configuration = configuration;
            _securityService = securityService;
        }

        public Task<Token> CreateToken(ApplicationUser user, bool populateExp)
        {
            return CreateToken(user, purpose: null, lifetime: null, amr: null, mfa: null, mfaTimestamp: null, extraClaims: null);
        }

        public async Task<Token> CreateToken(
            ApplicationUser user,
            string? purpose,
            TimeSpan? lifetime = null,
            IEnumerable<string>? amr = null,
            bool? mfa = null,
            DateTimeOffset? mfaTimestamp = null,
            IEnumerable<Claim>? extraClaims = null)
        {
            var signingCredentials = GetSigningCredentials();
            var claims = await BuildClaimsAsync(user, purpose, amr, mfa, mfaTimestamp, extraClaims);
            var expiry = ResolveExpiry(purpose, lifetime);
            var tokenOptions = GenerateTokenOptions(signingCredentials, claims, expiry);
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

        private async Task<List<Claim>> BuildClaimsAsync(
            ApplicationUser user,
            string? purpose,
            IEnumerable<string>? amr,
            bool? mfa,
            DateTimeOffset? mfaTimestamp,
            IEnumerable<Claim>? extraClaims)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (!string.IsNullOrEmpty(purpose))
            {
                claims.Add(new Claim("purpose", purpose));
            }

            if (amr != null)
            {
                var amrList = amr.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
                if (amrList.Count > 0)
                {
                    claims.Add(new Claim("amr", string.Join(" ", amrList)));
                }
            }

            if (mfa.HasValue)
            {
                claims.Add(new Claim("mfa", mfa.Value ? "true" : "false"));
            }

            if (mfaTimestamp.HasValue)
            {
                claims.Add(new Claim("mfa_ts", mfaTimestamp.Value.ToUnixTimeSeconds().ToString()));
            }

            if (extraClaims != null)
            {
                claims.AddRange(extraClaims);
            }

            // Only attach full roles to normal access tokens, not partial tokens (mfa_pending, mfa_enroll)
            if (string.IsNullOrEmpty(purpose) || purpose == "access")
            {
                // 1. ASP.NET Identity roles
                var identityRoles = await _userManager.GetRolesAsync(user);
                foreach (var role in identityRoles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                // 2. Domain Security roles
                var domainUser = _securityService.GetUser(user.UserName ?? user.Email ?? string.Empty);
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
            }

            return claims;
        }

        private DateTime ResolveExpiry(string? purpose, TimeSpan? customLifetime)
        {
            if (customLifetime.HasValue)
            {
                return DateTime.UtcNow.Add(customLifetime.Value);
            }

            if (purpose == "mfa_pending")
            {
                return DateTime.UtcNow.AddSeconds(300); // 5 minutes
            }

            if (purpose == "mfa_enroll")
            {
                return DateTime.UtcNow.AddSeconds(1800); // 30 minutes
            }

            var expires = _configuration["JwtSettings:expires"] ?? "15";
            return DateTime.UtcNow.AddMinutes(Convert.ToDouble(expires));
        }

        private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims, DateTime expires)
        {
            var validIssuer = _configuration["JwtSettings:validIssuer"];
            var validAudience = _configuration["JwtSettings:validAudience"];

            var tokenOptions = new JwtSecurityToken
            (
                issuer: validIssuer,
                audience: validAudience,
                claims: claims,
                expires: expires,
                signingCredentials: signingCredentials
            );

            return tokenOptions;
        }
    }
}
