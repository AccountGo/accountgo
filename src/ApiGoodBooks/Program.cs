

using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

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



var connectionString = builder.Configuration.GetConnectionString("Data:DevelopmentConnection:ConnectionString") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// These environment variables can be overriden from launchSettings.json.
string dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost,1444";
string dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
string dbUserPassword = System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "SqlPassword!";
string dbName = System.Environment.GetEnvironmentVariable("DBNAME") ?? "accountgodb";

connectionString = String.Format(builder.Configuration.GetConnectionString("Database:ConnectionString")!, dbServer, dbUserID, dbUserPassword, dbName);

System.Console.WriteLine("DB Connection String: " + connectionString);

builder.Services
    //.AddEntityFrameworkSqlServer()
    .AddDbContext<Data.ApiDbContext>(options => options.UseSqlServer(connectionString))
    .AddDbContext<Data.ApplicationIdentityDbContext>(options => options.UseSqlServer(connectionString));

builder.Services
    .AddIdentity<Data.ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<Data.ApplicationIdentityDbContext>()
    .AddDefaultTokenProviders();

// Add cors
builder.Services.AddCors(o => o.AddPolicy("AllowAll", builder =>
{
    builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader();
}));

// generic repository
builder.Services.AddScoped(typeof(Core.Data.IRepository<>), typeof(Data.EfRepository<>));

// custom repositories
builder.Services.AddScoped(typeof(Core.Data.ISalesOrderRepository), typeof(Data.SalesOrderRepository));
builder.Services.AddScoped(typeof(Core.Data.IPurchaseOrderRepository), typeof(Data.PurchaseOrderRepository));
builder.Services.AddScoped(typeof(Core.Data.ISecurityRepository), typeof(Data.Repositories.SecurityRepository));

// domain services
builder.Services.AddScoped(typeof(Services.Sales.ISalesService), typeof(Services.Sales.SalesService));
builder.Services.AddScoped(typeof(Services.Financial.IFinancialService), typeof(Services.Financial.FinancialService));
builder.Services.AddScoped(typeof(Services.Inventory.IInventoryService), typeof(Services.Inventory.InventoryService));
builder.Services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
builder.Services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
builder.Services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
builder.Services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.Run();
