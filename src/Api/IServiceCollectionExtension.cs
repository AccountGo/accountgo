using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Api
{
    public static class IServiceCollectionExtension
    {
        public static IServiceCollection AddJWTService(this IServiceCollection services, IConfiguration configuration)
        {
            // JWT 
            var key = Encoding.ASCII.GetBytes(configuration["Jwt:Key"]);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
            };

            var jwtEvents = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    context.NoResult();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";

                    var response = new { message = "Invalid token" };

                    context.Response.WriteAsync(JsonConvert.SerializeObject(response));

                    return Task.CompletedTask;
                }
            };

            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = true;
                x.TokenValidationParameters = validationParameters;
                x.Events = jwtEvents;
            });

            return services;
        }
    }
}
