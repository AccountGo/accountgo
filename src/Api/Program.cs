using System;
using Api.ActionFilters;
using Api.Data;
using Api.Data.Repositories;
using Api.Extensions;
using Api.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Validation
builder.Services.AddScoped<ValidationFilterAttribute>();

// Mapping
builder.Services.AddAutoMapper(typeof(Program));

// authentication
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddAuthentication();
builder.Services.ConfigureIdentity();
builder.Services.ConfigureJWT(builder.Configuration);

// swagger
builder.Services.ConfigureSwagger();

// swagger
builder.Services.ConfigureSwagger();

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

// Add database context
builder.Services.ConfigureSqlContext(builder.Configuration);

// Add cors
// builder.Services.AddCors(o => o.AddPolicy("AllowAll", builder =>
// {
//     builder
//     .AllowAnyOrigin()
//     .AllowAnyMethod()
//     .AllowAnyHeader();
// }));

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
builder.Services.AddScoped(typeof(Services.Financial.IFinancialService), typeof(Services.Financial.FinancialService));
builder.Services.AddScoped(typeof(Services.Inventory.IInventoryService), typeof(Services.Inventory.InventoryService));
builder.Services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
builder.Services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
builder.Services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
builder.Services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));

// background jobs
builder.Services.ConfigureHangFire(builder.Configuration);
builder.Services.AddSingleton<Services.BackgroundJobs.ExpiryCheckJobService>();


// background jobs
builder.Services.ConfigureHangFire(builder.Configuration);
builder.Services.AddSingleton<Services.BackgroundJobs.ExpiryCheckJobService>();

var app = builder.Build();

// Configure the HTTP request pipeline.

//app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard();
var jobService = app.Services.GetRequiredService<Services.BackgroundJobs.ExpiryCheckJobService>();
jobService.AddExpiryCheckJob();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(s =>
    {
        s.SwaggerEndpoint("/swagger/v1/swagger.json", "Good Deed Books API v1");
    });
}

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var apiDbContext = services.GetRequiredService<ApiDbContext>();
    if (!apiDbContext.Database.GetAppliedMigrations().Any())
    {
        apiDbContext.Database.Migrate();
    }

    var identityDbContext = services.GetRequiredService<ApplicationIdentityDbContext>();
    if (!identityDbContext.Database.GetAppliedMigrations().Any())
    {
        identityDbContext.Database.Migrate();
    }
}

app.Run();
