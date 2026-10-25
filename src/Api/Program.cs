using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Api.ActionFilters;
using Api.Data;
using Api.Data.Repositories;
using Api.Data.Seed;
using Api.Extensions;
using Api.Service;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

var builder = WebApplication.CreateBuilder(args);

// Load .env configuration from ContentRootPath or working directory
var envFilePath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (!File.Exists(envFilePath))
{
    envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
}
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
        var parts = trimmed.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var val = parts[1].Trim().Trim('"', '\'');
            Environment.SetEnvironmentVariable(key, val);
            builder.Configuration[key] = val;
        }
    }
}

// Add database context and provide builder environment information to the extended method for debugging purpose
builder.Services.ConfigureSqlContext(builder.Configuration, builder.Environment);

builder.AddServiceDefaults();

// Validation & Problem Details
builder.Services.AddScoped<ValidationFilterAttribute>();
builder.Services.AddProblemDetails();

// Invoice PDFs (QuestPDF Community licence: free for organisations under USD 1M annual revenue)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Mapping
builder.Services.AddAutoMapper(typeof(Program));

// authentication & authorization
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Configure Email Sender (Resend vs Dev Console)
var resendApiKey = builder.Configuration["RESEND_API_KEY"] ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");
var emailProvider = builder.Configuration["EMAIL_PROVIDER"] ?? builder.Configuration["Email:Provider"] ?? builder.Configuration["EMAIL__PROVIDER"];

if (!string.IsNullOrWhiteSpace(resendApiKey) || string.Equals(emailProvider, "Resend", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });
    Console.WriteLine("✓ ResendEmailSender registered as IEmailSender.");
}
else
{
    builder.Services.AddScoped<IEmailSender, DevConsoleEmailSender>();
    Console.WriteLine("ℹ DevConsoleEmailSender registered (configure RESEND_API_KEY in .env to enable Resend).");
}

builder.Services.AddScoped<IEmailOtpService, EmailOtpService>();
builder.Services.AddScoped<CompanyProvisioningService>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<CompanyAccountingPolicyService>();
builder.Services.AddScoped<WorkspaceInvitationService>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddAuthentication();
builder.Services.ConfigureIdentity();
builder.Services.ConfigureJWT(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    // Gate 1: Enforce global authentication fallback across all controllers and endpoints.
    // Endpoints that need to be public must explicitly declare [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddScoped<IFinancialService, FinancialService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<Api.Service.SalesInvoicePresentation>();
builder.Services.AddScoped<Api.Service.D1Sync.ID1SyncContext, Api.Service.D1Sync.D1SyncContext>();
builder.Services.AddScoped<Core.Tenancy.ITenantContext, Api.Tenancy.TenantContext>();
builder.Services.AddScoped<Core.Tenancy.ITenantGuard, Services.Tenancy.TenantGuard>();
builder.Services.AddMemoryCache();
builder.Services.AddExceptionHandler<Api.Filters.MultiTenancyExceptionHandler>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<Api.Filters.TenantEndpointFilter>();
})
.AddNewtonsoftJson(
    options =>
        {
            options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
            options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        }
    );

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// cors
builder.Services.ConfigureCors(builder.Configuration);

// generic repository
builder.Services.AddScoped(typeof(Core.Data.IRepository<>), typeof(EfRepository<>));

// custom repositories
builder.Services.AddScoped(typeof(Core.Data.ISalesOrderRepository), typeof(SalesOrderRepository));
builder.Services.AddScoped(typeof(Core.Data.IPurchaseOrderRepository), typeof(PurchaseOrderRepository));
builder.Services.AddScoped(typeof(Core.Data.ISecurityRepository), typeof(SecurityRepository));

// domain services
builder.Services.AddScoped(typeof(Services.Sales.ISalesService), typeof(Services.Sales.SalesService));
builder.Services.AddScoped(typeof(Services.Donations.IDonationsService), typeof(Services.Donations.DonationsService));
builder.Services.AddScoped(typeof(Services.Dashboard.IDashboardService), typeof(Services.Dashboard.DashboardService));
builder.Services.AddScoped(typeof(Services.Financial.IFinancialService), typeof(Services.Financial.FinancialService));
builder.Services.AddScoped(typeof(Services.Inventory.IInventoryService), typeof(Services.Inventory.InventoryService));
builder.Services.AddScoped(typeof(Services.Inventory.IStockService), typeof(Services.Inventory.StockService));
builder.Services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
builder.Services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
builder.Services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
builder.Services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));
builder.Services.AddScoped(typeof(Services.Auditing.IAuditableEntityService), typeof(Services.Auditing.AuditableEntityService));
builder.Services.AddScoped(typeof(Services.Auditing.IAuditableAttributeService), typeof(Services.Auditing.AuditableAttributeService));

//seed the database
builder.Services.AddScoped<DatabaseSeeder>();

// Model Context Protocol (MCP) Server for AI Agent Integration
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<Api.Mcp.Tools.FinancialMcpTools>();

// AccountGo Assistant → Anter external agent-runner (API key stays server-side)
builder.Services.Configure<Api.Options.AnterOptions>(builder.Configuration.GetSection(Api.Options.AnterOptions.SectionName));
builder.Services.PostConfigure<Api.Options.AnterOptions>(options =>
{
    string? Pick(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    options.ApiKey = Pick(options.ApiKey, builder.Configuration["Anter__ApiKey"], builder.Configuration["ANTER_API_KEY"]) ?? options.ApiKey;
    options.ProjectId = Pick(options.ProjectId, builder.Configuration["Anter__ProjectId"], builder.Configuration["ANTER_PROJECT_ID"]) ?? options.ProjectId;
    options.AgentSlug = Pick(options.AgentSlug, builder.Configuration["Anter__AgentSlug"], builder.Configuration["ANTER_AGENT_SLUG"]) ?? options.AgentSlug;
    options.BaseUrl = Pick(options.BaseUrl, builder.Configuration["Anter__BaseUrl"], builder.Configuration["ANTER_BASE_URL"]) ?? options.BaseUrl;
});
builder.Services.AddHttpClient<Api.Service.Ai.IAnterAgentRunnerClient, Api.Service.Ai.AnterAgentRunnerClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Configure reverse proxy / forwarded headers support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;

    // Do NOT clear KnownProxies or KnownIPNetworks — keep default loopback trust (127.0.0.1, ::1).
    // Allow explicitly trusting reverse proxy addresses or CIDR ranges from configuration:
    var configuredNetworks = builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>();
    if (configuredNetworks != null)
    {
        foreach (var net in configuredNetworks)
        {
            if (System.Net.IPNetwork.TryParse(net, out var ipNet))
            {
                options.KnownIPNetworks.Add(ipNet);
            }
        }
    }

    var configuredProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>();
    if (configuredProxies != null)
    {
        foreach (var proxy in configuredProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }
});

// Rate limiting: multi-tier protection (IP guard + per-email account guard)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("invitation-preview", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy("invoice-pdf-link", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Chained Limiter Level 1: Global IP limiter for auth endpoints (loosened to 30 req/15 min)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter($"auth_ip_{ip}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 1000 : 30,
                Window = TimeSpan.FromMinutes(15),
                SegmentsPerWindow = 3,
                QueueLimit = 0
            });
        }

        return RateLimitPartition.GetNoLimiter("unlimited");
    });

    // Chained Limiter Level 2: Strict per-email account limits
    options.AddPolicy("otp-send", httpContext =>
    {
        var email = httpContext.Items["rate_limit_email"] as string;
        var key = !string.IsNullOrEmpty(email) ? $"send_email_{email}" : $"send_ip_{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 100 : 3,
            Window = TimeSpan.FromMinutes(15),
            SegmentsPerWindow = 3,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.AddPolicy("otp-verify", httpContext =>
    {
        var email = httpContext.Items["rate_limit_email"] as string;
        var key = !string.IsNullOrEmpty(email) ? $"verify_email_{email}" : $"verify_ip_{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Environment.IsDevelopment() ? 100 : 10,
            Window = TimeSpan.FromMinutes(15),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Frontend");
app.UseMiddleware<Api.Service.D1Sync.D1SyncMiddleware>();

// Pre-rate-limiting middleware: protect against large-body DoS and extract normalized email asynchronously
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") &&
        context.Request.ContentType != null &&
        context.Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        // Low Finding 5: Max-body guard to prevent unbounded buffering into memory before rate limiting
        if (context.Request.ContentLength is > 32 * 1024)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Payload Too Large",
                status = 413,
                detail = "Request body exceeds maximum allowed size of 32 KB."
            });
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(body) && body.Length <= 32 * 1024)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("email", out var emailProp))
                {
                    var email = emailProp.GetString();
                    if (!string.IsNullOrWhiteSpace(email) && email.Length <= 256 && email.Contains('@'))
                    {
                        context.Items["rate_limit_email"] = email.Trim().ToLowerInvariant();
                    }
                }
            }
            catch
            {
            }
        }
    }

    await next();
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<Api.Middleware.TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapMcp("/mcp").RequireAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

Api.Filters.TenantEndpointValidator.ValidateAllEndpoints(typeof(Program).Assembly);

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var tenantContext = services.GetRequiredService<Core.Tenancy.ITenantContext>();
    using (tenantContext.Impersonate(1, "ph-financials", Core.Domain.CompanyMemberRole.Owner))
    {
        try
        {
            var apiDbContext = services.GetRequiredService<ApiDbContext>();
            var identityDbContext = services.GetRequiredService<ApplicationIdentityDbContext>();
            
            Console.WriteLine("====== Starting Database Initialization ======");

        if (apiDbContext.Database.IsSqlite())
        {
            var apiCreator = (RelationalDatabaseCreator)apiDbContext.GetService<IRelationalDatabaseCreator>();
            var identityCreator = (RelationalDatabaseCreator)identityDbContext.GetService<IRelationalDatabaseCreator>();

            bool isNewDatabase = !apiCreator.Exists() || !apiCreator.HasTables();
            if (!apiCreator.Exists())
            {
                apiCreator.Create();
                Console.WriteLine("✓ SQLite database file created.");
            }

            if (!apiCreator.HasTables())
            {
                Console.WriteLine("Creating SQLite tables for ApiDbContext...");
                apiCreator.CreateTables();
                Console.WriteLine("✓ ApiDbContext tables created.");
            }

            // Apply SQLite schema upgrader (multi-tenancy columns, tables, indexes)
            Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(apiDbContext.Database.GetDbConnection());

            var connection = identityDbContext.Database.GetDbConnection();
            bool identityTablesExist = false;
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AspNetUsers';";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                identityTablesExist = (count > 0);
            }

            if (!identityTablesExist)
            {
                Console.WriteLine("Creating SQLite tables for ApplicationIdentityDbContext...");
                identityCreator.CreateTables();
                Console.WriteLine("✓ ApplicationIdentityDbContext tables created.");
            }

            // Ensure RefreshTokens table exists
            bool refreshTokensTableExists = false;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RefreshTokens';";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                refreshTokensTableExists = (count > 0);
            }
            if (!refreshTokensTableExists)
            {
                Console.WriteLine("Creating RefreshTokens table in SQLite...");
                identityDbContext.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""RefreshTokens"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""UserId"" TEXT NOT NULL,
                        ""TokenHash"" TEXT NOT NULL,
                        ""ExpiresAt"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        ""CreatedByIp"" TEXT NULL,
                        ""RevokedAt"" TEXT NULL,
                        ""ReplacedByTokenHash"" TEXT NULL,
                        ""UserAgent"" TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_TokenHash"" ON ""RefreshTokens"" (""TokenHash"");
                    CREATE INDEX IF NOT EXISTS ""IX_RefreshTokens_UserId"" ON ""RefreshTokens"" (""UserId"");
                ");
                Console.WriteLine("✓ RefreshTokens table created.");
            }

            // On Cloudflare the Worker restores every table from D1 once the container is up, replacing whatever is
            // here; seeding demo data first only delays start-up (minutes on the smallest container).
            bool restoredFromD1 = string.Equals(app.Configuration["RESTORE_FROM_D1"], "true", StringComparison.OrdinalIgnoreCase);
            if (isNewDatabase && restoredFromD1)
            {
                Console.WriteLine("Skipping demo seed: data is restored from D1.");
            }
            else if (isNewDatabase)
            {
                var seeder = services.GetRequiredService<DatabaseSeeder>();
                Console.WriteLine("Seeding SQLite database...");
                seeder.Seed();
                Console.WriteLine("✓ SQLite database seeding completed successfully.");
            }

            // Start-up writes on Cloudflare never reach D1 (only request writes are synced) and the restore replaces these
            // tables moments later, so there the default roles and admin users come from D1 and its migrations instead.
            if (!restoredFromD1)
            {
                // Ensure security permissions and roles are initialized
                var securityService = services.GetRequiredService<Services.Security.ISecurityService>();
                securityService.EnsureDefaultPermissionsAndRoles();
                Console.WriteLine("✓ Security permissions and default roles verified.");

                // Ensure admin users exist in Identity and Domain
                var userManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
                var adminService = services.GetRequiredService<Services.Administration.IAdministrationService>();
                var secService = services.GetRequiredService<Services.Security.ISecurityService>();
                var adminRole = secService.GetRole("SystemAdministrators") ?? secService.GetRole("Administrator");

                var adminEmails = new[]
                {
                    (Email: "admin@accountgo.ph", First: "Admin", Last: "User"),
                    (Email: "marvinperez@gmail.com", First: "Marvin", Last: "Perez"),
                    (Email: "hello@marvinperez.com", First: "Marvin", Last: "Perez")
                };

                foreach (var (email, first, last) in adminEmails)
                {
                    var existingUser = userManager.FindByEmailAsync(email).GetAwaiter().GetResult();
                    if (existingUser == null)
                    {
                        var newUser = new ApplicationUser
                        {
                            UserName = email,
                            Email = email,
                            EmailConfirmed = true
                        };
                        var createResult = userManager.CreateAsync(newUser).GetAwaiter().GetResult();
                        if (createResult.Succeeded)
                        {
                            Console.WriteLine($"✓ Admin user '{email}' initialized in Identity.");
                        }
                    }

                    var domainUser = secService.GetUser(email);
                    if (domainUser == null)
                    {
                        var newDomainUser = new Core.Domain.Security.User
                        {
                            EmailAddress = email,
                            UserName = email,
                            Firstname = first,
                            Lastname = last
                        };
                        adminService.SaveUser(newDomainUser);
                        if (adminRole != null && email.Equals("marvinperez@gmail.com", StringComparison.OrdinalIgnoreCase))
                        {
                            var freshUser = secService.GetUser(email);
                            if (freshUser != null)
                            {
                                secService.AddUserInRole(freshUser.Id, adminRole.Id);
                            }
                        }
                        Console.WriteLine($"✓ Domain user '{email}' initialized.");
                    }
                }

            }

            // Run one-time post-seed membership and role backfill -> Version 2
            Api.Data.Schema.SqliteSchemaUpgrader.UpgradePostSeed(apiDbContext.Database.GetDbConnection());
        }
        else
        {
            // Ensure database exists before running migrations with retry logic
            try
            {
                var dbCreator = apiDbContext.GetService<IRelationalDatabaseCreator>();
                var strategy = apiDbContext.Database.CreateExecutionStrategy();
                strategy.Execute(() =>
                {
                    if (!dbCreator.Exists())
                    {
                        Console.WriteLine("Database does not exist. Creating...");
                        dbCreator.Create();
                        Console.WriteLine("✓ Database created successfully.");
                    }
                    else
                    {
                        Console.WriteLine("✓ Database already exists.");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: Failed to ensure database exists: {ex.Message}");
                throw;
            }
            
            try
            {
                Console.WriteLine("Applying ApiDb migrations...");
                apiDbContext.Database.Migrate();
                Console.WriteLine("✓ ApiDb migrations completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: ApiDb migrations failed: {ex.Message}");
                throw;
            }

            try
            {
                Console.WriteLine("Applying IdentityDb migrations...");
                identityDbContext.Database.Migrate();
                Console.WriteLine("✓ IdentityDb migrations completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: IdentityDb migrations failed: {ex.Message}");
                throw;
            }

            try
            {
                var seeder = services.GetRequiredService<DatabaseSeeder>();
                Console.WriteLine("Seeding database...");
                seeder.Seed();
                Console.WriteLine("✓ Database seeding completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: Database seeding failed: {ex.Message}");
                throw;
            }
        }

        Console.WriteLine("====== Database Initialization Completed Successfully ======");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ FATAL ERROR: Database initialization failed - Application startup aborted.");
        Console.WriteLine($"Details: {ex}");
        throw;
    }
    }
}

// The Docker build runs the API once with --prepare-schema to create the empty database the image ships with, so a new
// container on Cloudflare starts with its tables in place instead of creating them on a fraction of a vCPU.
if (args.Contains("--prepare-schema"))
{
    Console.WriteLine("✓ Schema prepared; exiting without serving requests.");
    return;
}

app.Run();

public partial class Program { }
