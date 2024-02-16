using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.Json;
using WebBlazor.Authentication;

namespace WebBlazor
{
    public static class Generics
    {
        public static ClaimsPrincipal SetClaimPrincipal(UserSession model)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, model.Id!),
                    new(ClaimTypes.Name, model.Name!),
                    new(ClaimTypes.Email, model.Email!),
                    new(ClaimTypes.Role, model.Role!),
                }, "JwtAuth"));
        }

        public static IEnumerable<Claim> ParseClaimsFromJwt(string jwtToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);
            return token.Claims;
        }

        public static UserSession GetUserClaimsFromToken(string jwtToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);
            var claims = token.Claims;

            string Id = claims.First(c => c.Type == "id").Value!;
            string Name = claims.First(c => c.Type == "name").Value!;
            string Email = claims.First(c => c.Type == "email").Value!;
            string Role = claims.First(c => c.Type == "role").Value!;

            return new UserSession(Id, Name, Email, Role);
        }

        public static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
            };
        }

        public static string SerializeObj<T>(T modelObject) => JsonSerializer.Serialize(modelObject, JsonOptions());
        public static T DeserializeJsonString<T>(string jsonString) => JsonSerializer.Deserialize<T>(jsonString, JsonOptions())!;
        public static IList<T> DeserializeJsonStringList<T>(string jsonString) => JsonSerializer.Deserialize<IList<T>>(jsonString, JsonOptions())!;

        public static StringContent GenerateStringContent(string serialiazedObj) => new(serialiazedObj, System.Text.Encoding.UTF8, "application/json");
    }
}
