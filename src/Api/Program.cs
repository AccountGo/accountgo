using Api.ActionFilters;
using Api.Data;
using Api.Data.Repositories;
using Api.Data.Seed;
using Api.Extensions;
using Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add database context and provide builder environment information to the extended method for debugging purpose
builder.Services.ConfigureSqlContext(builder.Configuration, builder.Environment);

builder.AddServiceDefaults();

// Validation
builder.Services.AddScoped<ValidationFilterAttribute>();

// Mapping
builder.Services.AddAutoMapper(typeof(Program));

// authentication
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddAuthentication();
builder.Services.ConfigureIdentity();
builder.Services.ConfigureJWT(builder.Configuration);
builder.Services.AddScoped<IFinancialService, FinancialService>();
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddControllers()
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
builder.Services.ConfigureCors();

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
builder.Services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
builder.Services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
builder.Services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
builder.Services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));
builder.Services.AddScoped(typeof(Services.Auditing.IAuditableEntityService), typeof(Services.Auditing.AuditableEntityService));
builder.Services.AddScoped(typeof(Services.Auditing.IAuditableAttributeService), typeof(Services.Auditing.AuditableAttributeService));

//seed the database
builder.Services.AddScoped<DatabaseSeeder>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    try
    {
        var apiDbContext = services.GetRequiredService<ApiDbContext>();
        
        // Ensure database exists before running migrations with retry logic
        try
        {
            Console.WriteLine("====== Starting Database Initialization ======");
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
            var identityDbContext = services.GetRequiredService<ApplicationIdentityDbContext>();
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

        Console.WriteLine("====== Database Initialization Completed Successfully ======");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ FATAL ERROR: Database initialization failed - Application startup aborted.");
        Console.WriteLine($"Details: {ex}");
        throw;
    }
}

app.Run();
