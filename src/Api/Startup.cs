using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Core.Utilities;
using Api.Data;
using Dto.Contracts;
using Api.ApiServices;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Api.Data.Repositories;
using Core.Data;

namespace Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddControllers()
                .AddNewtonsoftJson(options => 
                    {
                        options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
                        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                    }
                );

            string connectionString = Configuration["Data:DevelopmentConnection:ConnectionString"];
            // These environment variables can be overriden from launchSettings.json.
            string dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost";
            string dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
            string dbUserPassword= System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "Str0ngPassword";
            string dbName= System.Environment.GetEnvironmentVariable("DBNAME") ?? "accountgodb";

            connectionString = String.Format(Configuration["Database:ConnectionString"], dbServer, dbUserID, dbUserPassword, dbName);

            System.Console.WriteLine("DB Connection String: " + connectionString);

            services
                //.AddEntityFrameworkSqlServer()
                .AddDbContext<Data.ApiDbContext>(options => options.UseSqlServer(connectionString))
                .AddDbContext<Data.ApplicationIdentityDbContext>(options => options.UseSqlServer(connectionString));

            //Add Identity & JWT authentication
            //Identity
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
                .AddSignInManager()
                .AddRoles<IdentityRole>();

            services.AddJWTService(Configuration);

            services.AddTransient(s => ClaimsPrincipal.Current);
            services.AddTransient(s => s.GetService<IHttpContextAccessor>().HttpContext.User);
            // Add cors
            services.AddCors(o => o.AddPolicy("AllowAll", builder =>
            {
                builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
            }));

            // generic repository
            services.AddScoped(typeof(Core.Data.IRepository<>), typeof(Data.EfRepository<>));
            services.AddTransient<IEfTransaction, EfTransaction>();

            services.AddTransient<IDateTimeProvider, DateTimeProvider>();

            // custom repositories
            services.AddScoped(typeof(Core.Data.ISalesOrderRepository), typeof(Data.SalesOrderRepository));
            services.AddScoped(typeof(Core.Data.IPurchaseOrderRepository), typeof(Data.PurchaseOrderRepository));
            services.AddScoped(typeof(Core.Data.ISecurityRepository), typeof(Data.Repositories.SecurityRepository));

            // domain services
            services.AddScoped(typeof(Services.Sales.ISalesService), typeof(Services.Sales.SalesService));
            services.AddScoped(typeof(Services.Financial.IFinancialService), typeof(Services.Financial.FinancialService));
            services.AddScoped(typeof(Services.Inventory.IInventoryService), typeof(Services.Inventory.InventoryService));
            services.AddScoped(typeof(Services.Purchasing.IPurchasingService), typeof(Services.Purchasing.PurchasingService));
            services.AddScoped(typeof(Services.Administration.IAdministrationService), typeof(Services.Administration.AdministrationService));
            services.AddScoped(typeof(Services.Security.ISecurityService), typeof(Services.Security.SecurityService));
            services.AddScoped(typeof(Services.TaxSystem.ITaxService), typeof(Services.TaxSystem.TaxService));

            services.AddScoped<IUserAccount, UserAccountService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // app.UseHttpsRedirection();

            app.UseCors("AllowAll");
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
