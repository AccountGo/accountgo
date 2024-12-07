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

        static Dictionary<string, string> parseConnectionString(string connString)
        {
            var parts = connString.Split(';');
            var dict = parts
                .Select(p => p.Split('='))
                .ToDictionary(p => p[0], p => p[1]);

            return dict;
        }

        public static void ConfigureSqlContext(this IServiceCollection services, IConfiguration configuration)
        {
            string dbServer, dbUserID, dbUserPassword, dbName = string.Empty;

            var connectionString = configuration.GetConnectionString("gdb-db") ?? null;

            if (connectionString == null)
            {
                // These environment variables can be overriden from launchSettings.json.
                dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost,1444";
                dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
                dbUserPassword = System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "SqlPassword!";
                dbName = System.Environment.GetEnvironmentVariable("DBNAME") ?? "accountgodb";

                connectionString = string.Format(configuration.GetConnectionString("DefaultConnection")!, dbServer, dbUserID, dbUserPassword, dbName);
            } 

            Console.WriteLine("DB Connection String: " + connectionString);

            services
                .AddDbContext<ApiDbContext>(options => options.UseSqlServer(connectionString
                , options => options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)))
                //.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery) // Add this line
                .AddDbContext<ApplicationIdentityDbContext>(options => options.UseSqlServer(connectionString));
        }
    }
}
