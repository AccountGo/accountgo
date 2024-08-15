using Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Api.Extensions
{
    public static class ServiceExtensions
    {
        public static void ConfigureCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(name: "AllowAll",
                                  policy =>
                                  {
                                      policy.AllowAnyOrigin()
                                            .AllowAnyMethod()
                                            .AllowAnyHeader();
                                  });
            });
        }

        public static void ConfigureIdentity(this IServiceCollection services)
        {
            services.AddIdentity<ApplicationUser, IdentityRole>()
                 .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
                 .AddDefaultTokenProviders();
        }

        public static void ConfigureJWT(this IServiceCollection services, IConfiguration configuration)
        {
            var validIssuer = configuration["JwtSettings:validIssuer"];
            var validAudience = configuration["JwtSettings:validAudience"];
            var secretKey = configuration["SECRET:key"];

            services.AddAuthentication(opt =>
            {
                opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = validIssuer,
                    ValidAudience = validAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!))
                };
            });
        }

        public static void ConfigureSqlContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            // These environment variables can be overriden from launchSettings.json.
            string dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost,1444";
            string dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
            string dbUserPassword = System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "SqlPassword!";
            string dbName = System.Environment.GetEnvironmentVariable("DBNAME") ?? "accountgodb";

            connectionString = String.Format(configuration.GetConnectionString("DefaultConnection")!, dbServer, dbUserID, dbUserPassword, dbName);

            System.Console.WriteLine("DB Connection String: " + connectionString);
            
            services
                //.AddEntityFrameworkSqlServer()
                .AddDbContext<ApiDbContext>(options => options.UseSqlServer(connectionString
                , options => options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)))
                //.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery) // Add this line
                .AddDbContext<ApplicationIdentityDbContext>(options => options.UseSqlServer(connectionString));
        }
    }
}
