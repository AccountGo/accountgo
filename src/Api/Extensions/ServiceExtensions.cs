using System.IO;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Api.Extensions
{
    public static class ServiceExtensions
    {
        public static void ConfigureDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            var keyDirectory = configuration["DataProtection:KeyDirectory"] ?? Environment.GetEnvironmentVariable("DATAPROTECTION_KEY_DIRECTORY");

            var dpBuilder = services.AddDataProtection()
                .SetApplicationName("AccountGo");

            if (!string.IsNullOrWhiteSpace(keyDirectory))
            {
                var dir = new DirectoryInfo(keyDirectory);
                if (!dir.Exists)
                {
                    dir.Create();
                }
                dpBuilder.PersistKeysToFileSystem(dir);
            }
            else
            {
                services.AddSingleton<Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository, Api.Data.DataProtection.DbXmlRepository>();
                services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
                    .Configure<IServiceProvider>((options, sp) =>
                    {
                        options.XmlRepository = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository>();
                    });
            }
        }

        public static void ConfigureCors(this IServiceCollection services, IConfiguration configuration)
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[]
                {
                    "http://localhost:8000",
                    "https://localhost:8000",
                    "http://localhost:5173",
                    "https://localhost:5173",
                    "http://localhost:3000"
                };

            services.AddCors(options =>
            {
                options.AddPolicy(name: "Frontend",
                    policy =>
                    {
                        policy.WithOrigins(allowedOrigins)
                            .AllowAnyMethod()
                            .AllowAnyHeader()
                            .AllowCredentials();
                    });

                // Backwards compatibility alias
                options.AddPolicy(name: "AllowAll",
                    policy =>
                    {
                        policy.WithOrigins(allowedOrigins)
                            .AllowAnyMethod()
                            .AllowAnyHeader()
                            .AllowCredentials();
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
            var secretKey = configuration["SECRET_KEY"] ?? configuration["SECRET:key"] ?? configuration["SECRET__key"] ?? Environment.GetEnvironmentVariable("SECRET_KEY");

            if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
            {
                throw new InvalidOperationException(
                    "FATAL SECURITY CONFIGURATION: 'SECRET_KEY' (or 'SECRET:key') is missing or shorter than 32 bytes (256 bits). " +
                    "Configure a secure key via environment variable 'SECRET_KEY' or user-secrets ('dotnet user-secrets set \"SECRET_KEY\" \"<key>\"').");
            }

            var audiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(validAudience))
            {
                audiences.Add(validAudience);
            }
            audiences.Add("https://api.accountgo.dev");
            audiences.Add("https://accountgo-api.anter.workers.dev");

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
                    ClockSkew = TimeSpan.FromMinutes(1),

                    ValidIssuer = validIssuer,
                    ValidAudience = validAudience,
                    ValidAudiences = audiences,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.Request.Path.StartsWithSegments("/mcp") &&
                            Api.Middleware.TenantResolutionMiddleware.TryCreateMcpGatePrincipal(
                                context.HttpContext,
                                JwtBearerDefaults.AuthenticationScheme,
                                out var gatePrincipal) &&
                            gatePrincipal != null)
                        {
                            context.Principal = gatePrincipal;
                            context.Success();
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (string.IsNullOrEmpty(userId))
                        {
                            context.Fail("Account deleted");
                            return;
                        }
                        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                        var user = await users.FindByIdAsync(userId);
                        if (user == null || user.DeletedAt != null)
                            context.Fail("Account deleted");
                    }
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

        public static void ConfigureSqlContext(this IServiceCollection services, IConfiguration configuration,
            IHostEnvironment environment)
        {
            var provider = System.Environment.GetEnvironmentVariable("DBPROVIDER")
                ?? configuration["DatabaseProvider"]
                ?? "Sqlite";

            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var sqliteConnection = configuration.GetConnectionString("SqliteConnection")
                    ?? "Data Source=accountgo.db;Foreign Keys=True;";

                Console.WriteLine("[Database Provider] Using SQLite");

                services.AddDbContext<ApiDbContext>((sp, options) =>
                {
                    options.UseSqlite(sqliteConnection);
                    options.AddInterceptors(sp.GetRequiredService<ServerTimingDbCommandInterceptor>());
                    if (environment.IsDevelopment())
                    {
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                    }
                });

                services.AddDbContext<ApplicationIdentityDbContext>((sp, options) =>
                {
                    options.UseSqlite(sqliteConnection);
                    options.AddInterceptors(sp.GetRequiredService<ServerTimingDbCommandInterceptor>());
                });

                return;
            }

            string dbServer, dbUserID, dbUserPassword, dbName = string.Empty;

            var connectionString = configuration.GetConnectionString("gdb-db") ?? null;

            if (connectionString == null)
            {
                // These environment variables can be overriden from launchSettings.json.
                dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost,1444";
                dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
                dbUserPassword = System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "SqlPassword!";
                dbName = System.Environment.GetEnvironmentVariable("DBNAME") ?? "accountgodb";

                connectionString = string.Format(configuration.GetConnectionString("DefaultConnection")!, dbServer,
                    dbUserID, dbUserPassword, dbName);
            }

            Console.WriteLine("[Database Provider] Using SQL Server");

            services.AddDbContext<ApiDbContext>((sp, options) =>
            {
                options.UseSqlServer(
                    connectionString: connectionString,
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.UseQuerySplittingBehavior(
                            querySplittingBehavior: QuerySplittingBehavior.SplitQuery);
                        sqlOptions.EnableRetryOnFailure();
                    });
                options.AddInterceptors(sp.GetRequiredService<ServerTimingDbCommandInterceptor>());

                // Only enable sensitive logging for debugging purpose on ApiDbContext in development setting
                if (environment.IsDevelopment())
                {
                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }
            });

            services.AddDbContext<ApplicationIdentityDbContext>((sp, options) =>
            {
                options.UseSqlServer(connectionString: connectionString);
                options.AddInterceptors(sp.GetRequiredService<ServerTimingDbCommandInterceptor>());
            });
        }
    }
}
