using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Api
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnv;

        public Startup(IHostingEnvironment env)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
            _hostingEnv = env;
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string connectionString = Configuration["Data:DevelopmentConnection:ConnectionString"];
            // These environment variables can be overriden from launchSettings.json.
            string dbServer = System.Environment.GetEnvironmentVariable("DBSERVER") ?? "localhost";
            string dbUserID = System.Environment.GetEnvironmentVariable("DBUSERID") ?? "sa";
            string dbUserPassword= System.Environment.GetEnvironmentVariable("DBPASSWORD") ?? "Str0ngPassword";

            connectionString = String.Format(Configuration["Database:ConnectionString"], dbServer, dbUserID, dbUserPassword);

            System.Console.WriteLine("DB Connection String: " + connectionString);

            services
                .AddEntityFrameworkSqlServer()
                .AddDbContext<Data.ApiDbContext>(options => options.UseSqlServer(connectionString))
                .AddDbContext<Data.ApplicationIdentityDbContext>(options => options.UseSqlServer(connectionString));

            services
                .AddIdentity<Data.ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<Data.ApplicationIdentityDbContext>()
                .AddDefaultTokenProviders();

            // Add framework services.
            services.AddMvc()
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
                    options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                });

            //services.AddSwaggerGen();

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
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
                      
            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseCors("AllowAll");
            app.UseMvc();
            //app.UseSwaggerGen();
            //app.UseSwaggerUi();
        }        
    }
}
