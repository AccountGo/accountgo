using Api.Data;
using Dto.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Api.Service
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        private ApplicationUser? _user;

        public AuthenticationService(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

        public async Task<bool> ValidateUser(dynamic loginViewModel)
        {
            string username = loginViewModel.Email;
            string password = loginViewModel.Password;

            _user = await _userManager.FindByEmailAsync(username);

            var result = (_user != null && await _userManager.CheckPasswordAsync(_user, password));

            if (!result)
                System.Console.WriteLine($"'{loginViewModel.Email}': Authentication failed. Wrong user name or password.");

            return result;
        }

        public async Task<Token> CreateToken(bool populateExp)
        {
            var signingCredentials = GetSigningCredentials();
            var claims = GetClaims();
            var tokenOptions = GenerateTokenOptions(signingCredentials, claims);

            var refreshToken = GenerateRefreshToken();

            _user!.RefreshToken = refreshToken;

            if (populateExp)
                _user.RefreshTokenExpiryTime = DateTime.Now.AddDays(7);

            await _userManager.UpdateAsync(_user);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenOptions);

            return new Token(accessToken, refreshToken);
        }

        public async Task<Token> RefreshToken(Token tokenDto)
        {
            var principal = GetPrincipalFromExpriedToken(tokenDto.AccessToken);

            var user = await _userManager.FindByEmailAsync(principal.Identity!.Name!);
            if (user == null || user.RefreshToken != tokenDto.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.Now)
                throw new SecurityTokenException("Invalid Client request. The tokenDto has some invalid values.");

            _user = user;

            return await CreateToken(populateExp: true);
        }

        private SigningCredentials GetSigningCredentials()
        {
            var secretKey = _configuration["SECRET:key"];

            var key = Encoding.UTF8.GetBytes(secretKey!);
            var secret = new SymmetricSecurityKey(key);

            return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
        }

        private List<Claim> GetClaims()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, _user!.UserName!),
                new Claim(ClaimTypes.Email, _user!.Email!)
            };

            return claims;
        }

        private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
        {
            var validIssuer = _configuration["JwtSettings:validIssuer"];
            var validAudience = _configuration["JwtSettings:validAudience"];
            var expires = _configuration["JwtSettings:expires"];

            var tokenOptions = new JwtSecurityToken
            (
                issuer: validIssuer,
                audience: validAudience,
                claims: claims,
                expires: DateTime.Now.AddMinutes(Convert.ToDouble(expires)),
                signingCredentials: signingCredentials
            );

            return tokenOptions;
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        private ClaimsPrincipal GetPrincipalFromExpriedToken(string token)
        {
            var validIssuer = _configuration["JwtSettings:validIssuer"];
            var validAudience = _configuration["JwtSettings:validAudience"];
            var secretKey = _configuration["SECRET:key"];

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
                ValidateLifetime = false,
                ValidIssuer = validIssuer,
                ValidAudience = validAudience
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);

            var jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }
    }
}
