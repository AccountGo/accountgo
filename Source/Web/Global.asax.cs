//-----------------------------------------------------------------------
// <copyright file="Global.asax.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Autofac;
using Autofac.Integration.Mvc;
using Autofac.Integration.WebApi;
using Core.Data;
using Data;
using Newtonsoft.Json.Serialization;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using Services.Security;
using Services.TaxSystem;
using System;
using System.Linq;
using System.Net.Http.Formatting;
using System.Reflection;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace Web
{
    public class MvcApplication : System.Web.HttpApplication
    {
        private IContainer container;
        private ContainerBuilder builder;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            JsonMediaTypeFormatter formatter = GlobalConfiguration.Configuration.Formatters.OfType<JsonMediaTypeFormatter>().FirstOrDefault();
            formatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
        }
        protected void Application_BeginRequest(Object sender, EventArgs e)
        {
            DependecyResolver();
        }

        private void DependecyResolver()
        {
            builder = new ContainerBuilder();

            //controllers
            builder.RegisterControllers(Assembly.GetExecutingAssembly());
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly());

            //dbcontext
            builder.Register<IDbContext>(c => new ApplicationContext()).InstancePerLifetimeScope();
            //var dbInitializer = new DbInitializer<ApplicationContext>();
            //System.Data.Entity.Database.SetInitializer<ApplicationContext>(dbInitializer);
            //dbInitializer.InitializeDatabase(new ApplicationContext());
            //if (HttpContext.Current.Request.IsLocal)
            //{
            //    var dbInitializer = new DbInitializer<ApplicationContext>();
            //    Database.SetInitializer<ApplicationContext>(dbInitializer);
            //    dbInitializer.InitializeDatabase(new ApplicationContext());
            //    builder.Register<IDbContext>(c => new ApplicationContext()).InstancePerLifetimeScope();
            //}
            //else
            //{
            //    Database.SetInitializer<ApplicationContext>(null);
            //    builder.Register<IDbContext>(c => new ApplicationContext()).InstancePerLifetimeScope();
            //}

            //generic repositories
            builder.RegisterGeneric(typeof(EfRepository<>)).As(typeof(IRepository<>)).InstancePerLifetimeScope();

            //services
            builder.RegisterType<FinancialService>().As<IFinancialService>().InstancePerLifetimeScope();
            builder.RegisterType<InventoryService>().As<IInventoryService>().InstancePerLifetimeScope();
            builder.RegisterType<SalesService>().As<ISalesService>().InstancePerLifetimeScope();
            builder.RegisterType<PurchasingService>().As<IPurchasingService>().InstancePerLifetimeScope();
            builder.RegisterType<AdministrationService>().As<IAdministrationService>().InstancePerLifetimeScope();
            builder.RegisterType<SecurityService>().As<ISecurityService>().InstancePerLifetimeScope();
            builder.RegisterType<TaxService>().As<ITaxService>().InstancePerLifetimeScope();

            container = builder.Build();
            DependencyResolver.SetResolver(new AutofacDependencyResolver(container));
            GlobalConfiguration.Configuration.DependencyResolver = new AutofacWebApiDependencyResolver(container);
        }
    }
}
