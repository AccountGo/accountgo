//-----------------------------------------------------------------------
// <copyright file="ApplicationContext.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace Data
{
    //[DbConfigurationType("Data.Configuration, Data")]
    public class ApplicationContext : DbContext, IDbContext
    {
        public string UserName
        {
            get { return System.Threading.Thread.CurrentPrincipal.Identity.Name; }
        }
        
        public ApplicationContext() 
            : this("ApplicationContext")
        {
        }
        public ApplicationContext(string name) 
            : base(name)
        {
            Database.SetInitializer<ApplicationContext>(null); //uncomment this line to disable code first
        }

        public DbSet<AccountClass> AccountClasses { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<GeneralLedgerHeader> GeneralLedgerHeaders { get; set; }
        public DbSet<GeneralLedgerLine> GeneralLedgerLines{ get; set; }
        public DbSet<JournalEntryHeader> JournalEntryHeaders { get; set; }
        public DbSet<JournalEntryLine> JournalEntryLines { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Party> Parties { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<Measurement> Measurements { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<ItemCategory> ItemCategories { get; set; }        
        public DbSet<SalesQuoteHeader> SalesQuoteHeaders { get; set; }
        public DbSet<SalesQuoteLine> SalesQuoteLines { get; set; }
        public DbSet<SalesOrderHeader> SalesOrderHeaders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<SalesDeliveryHeader> SalesDeliveryHeaders { get; set; }
        public DbSet<SalesDeliveryLine> SalesDeliveryLines { get; set; }
        public DbSet<SalesReceiptHeader> SalesReceiptHeaders { get; set; }
        public DbSet<SalesReceiptLine> SalesReceiptLines { get; set; }
        public DbSet<SalesInvoiceHeader> SalesInvoiceHeaders { get; set; }
        public DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public DbSet<PurchaseOrderHeader> PurchaseOrderHeaders { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<PurchaseReceiptHeader> PurchaseReceiptHeaders { get; set; }
        public DbSet<PurchaseReceiptLine> PurchaseReceiptLines { get; set; }
        public DbSet<PurchaseInvoiceHeader> PurchaseInvoiceHeaders { get; set; }
        public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public DbSet<SequenceNumber> SequenceNumbers { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<CompanySetting> CompanySettings { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<FiscalYear> FiscalYears { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<TaxGroup> TaxGroups { get; set; }
        public DbSet<ItemTaxGroup> ItemTaxGroups { get; set; }
        public DbSet<TaxGroupTax> TaxGroupTax { get; set; }
        public DbSet<ItemTaxGroupTax> ItemTaxGroupTax { get; set; }
        public DbSet<GeneralLedgerSetting> GeneralLedgerSettings { get; set; }
        public DbSet<PaymentTerm> PaymentTerms { get; set; }
        public DbSet<Bank> Banks { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<AuditableEntity> AuditableEntities { get; set; }
        public virtual DbSet<AuditableAttribute> AuditableAttributes { get; set; }
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<SecurityRole> SecurityRoles { get; set; }
        public virtual DbSet<SecurityPermission> SecurityPermissions { get; set; }
        public virtual DbSet<SecurityUserRole> SecurityUserRoles { get; set; }
        public virtual DbSet<SecurityGroup> SecurityGroups { get; set; }
        public virtual DbSet<SecurityRolePermission> SecurityRolePermissions { get; set; }
        public virtual DbSet<MainContraAccount> MainContraAccounts { get; set; }

        #region Methods

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Conventions.Remove<System.Data.Entity.ModelConfiguration.Conventions.OneToManyCascadeDeleteConvention>();
            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges()
        {
            SaveAuditLog(UserName);

            // CAN BE USE IN THE FUTURE : Track Created and Modified fields Automatically with Entity Framework Code First

            //var entities = ChangeTracker.Entries().Where(x => x.Entity is BaseEntity && (x.State == EntityState.Added || x.State == EntityState.Modified));

            //var currentUsername = HttpContext.Current != null && HttpContext.Current.User != null
            //    ? HttpContext.Current.User.Identity.Name
            //    : "Anonymous";

            //foreach (var entity in entities)
            //{
            //    if (entity.State == EntityState.Added)
            //    {
            //        ((BaseEntity)entity.Entity).DateCreated = DateTime.Now;
            //        ((BaseEntity)entity.Entity).UserCreated = currentUsername;
            //    }

            //    ((BaseEntity)entity.Entity).DateModified = DateTime.Now;
            //    ((BaseEntity)entity.Entity).UserModified = currentUsername;
            //}

            var ret = base.SaveChanges();

            UpdateAuditLogRecordId();

            return ret;
        }

        /// <summary>
        /// Create database script
        /// </summary>
        /// <returns>SQL to generate database</returns>
        public string CreateDatabaseScript()
        {
            return ((IObjectContextAdapter)this).ObjectContext.CreateDatabaseScript();
        }

        /// <summary>
        /// Get DbSet
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <returns>DbSet</returns>
        public new IDbSet<TEntity> Set<TEntity>() where TEntity : BaseEntity
        {
            return base.Set<TEntity>();
        }

        /// <summary>
        /// Execute stores procedure and load a list of entities at the end
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="commandText">Command text</param>
        /// <param name="parameters">Parameters</param>
        /// <returns>Entities</returns>
        public IList<TEntity> ExecuteStoredProcedureList<TEntity>(string commandText, params object[] parameters) where TEntity : BaseEntity, new()
        {
            //add parameters to command
            if (parameters != null && parameters.Length > 0)
            {
                for (int i = 0; i <= parameters.Length - 1; i++)
                {
                    var p = parameters[i] as DbParameter;
                    if (p == null)
                        throw new Exception("Not support parameter type");

                    commandText += i == 0 ? " " : ", ";

                    commandText += "@" + p.ParameterName;
                    if (p.Direction == ParameterDirection.InputOutput || p.Direction == ParameterDirection.Output)
                    {
                        //output parameter
                        commandText += " output";
                    }
                }
            }

            var result = this.Database.SqlQuery<TEntity>(commandText, parameters).ToList();

            bool acd = this.Configuration.AutoDetectChangesEnabled;
            try
            {
                this.Configuration.AutoDetectChangesEnabled = false;

                for (int i = 0; i < result.Count; i++)
                    result[i] = AttachEntityToContext(result[i]);
            }
            finally
            {
                this.Configuration.AutoDetectChangesEnabled = acd;
            }

            return result;
        }

        /// <summary>
        /// Creates a raw SQL query that will return elements of the given generic type.  The type can be any type that has properties that match the names of the columns returned from the query, or can be a simple primitive type. The type does not have to be an entity type. The results of this query are never tracked by the context even if the type of object returned is an entity type.
        /// </summary>
        /// <typeparam name="TElement">The type of object returned by the query.</typeparam>
        /// <param name="sql">The SQL query string.</param>
        /// <param name="parameters">The parameters to apply to the SQL query string.</param>
        /// <returns>Result</returns>
        public IEnumerable<TElement> SqlQuery<TElement>(string sql, params object[] parameters)
        {
            return this.Database.SqlQuery<TElement>(sql, parameters);
        }

        /// <summary>
        /// Executes the given DDL/DML command against the database.
        /// </summary>
        /// <param name="sql">The command string</param>
        /// <param name="doNotEnsureTransaction">false - the transaction creation is not ensured; true - the transaction creation is ensured.</param>
        /// <param name="timeout">Timeout value, in seconds. A null value indicates that the default value of the underlying provider will be used</param>
        /// <param name="parameters">The parameters to apply to the command string.</param>
        /// <returns>The result returned by the database after executing the command.</returns>
        public int ExecuteSqlCommand(string sql, bool doNotEnsureTransaction = false, int? timeout = null, params object[] parameters)
        {
            int? previousTimeout = null;
            if (timeout.HasValue)
            {
                //store previous timeout
                previousTimeout = ((IObjectContextAdapter)this).ObjectContext.CommandTimeout;
                ((IObjectContextAdapter)this).ObjectContext.CommandTimeout = timeout;
            }

            var transactionalBehavior = doNotEnsureTransaction
                ? TransactionalBehavior.DoNotEnsureTransaction
                : TransactionalBehavior.EnsureTransaction;
            var result = this.Database.ExecuteSqlCommand(transactionalBehavior, sql, parameters);

            if (timeout.HasValue)
            {
                //Set previous timeout back
                ((IObjectContextAdapter)this).ObjectContext.CommandTimeout = previousTimeout;
            }

            return result;
        }

        #endregion

        protected virtual TEntity AttachEntityToContext<TEntity>(TEntity entity) where TEntity : BaseEntity, new()
        {
            //little hack here until Entity Framework really supports stored procedures
            //otherwise, navigation properties of loaded entities are not loaded until an entity is attached to the context
            var alreadyAttached = Set<TEntity>().Local.FirstOrDefault(x => x.Id == entity.Id);
            if (alreadyAttached == null)
            {
                //attach new entity
                Set<TEntity>().Attach(entity);
                return entity;
            }

            //entity is already loaded
            return alreadyAttached;
        }

        #region Audit Logs
        private void SaveAuditLog(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                var dbEntityEntries = this.ChangeTracker.Entries().Where(p => p.State == System.Data.Entity.EntityState.Modified || p.State == System.Data.Entity.EntityState.Added || p.State == System.Data.Entity.EntityState.Deleted);
                foreach (var dbEntityEntry in dbEntityEntries)
                {
                    try
                    {
                        var auditLogs = AuditLogHelper.GetChangesForAuditLog(dbEntityEntry, username, this);
                        foreach (var auditlog in auditLogs)
                            if (auditlog != null)
                                this.AuditLogs.Add(auditlog);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }

        private void UpdateAuditLogRecordId()
        {
            foreach (var entity in AuditLogHelper.addedEntities)
            {
                if (this.ChangeTracker.Entries().Contains(entity.Value))
                {
                    string keyName = entity.Value.Entity.GetType().GetProperties().Single(p => p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), false).Count() > 0).Name;
                    string recid = entity.Value.CurrentValues.GetValue<object>(keyName).ToString();
                    var auditLog = this.AuditLogs.Where(log => log.AuditEventDateUTC == entity.Key).FirstOrDefault();
                    if (auditLog != null)
                    {
                        auditLog.RecordId = recid;
                        base.SaveChanges();
                    }
                }
            }
        }
        #endregion
    }
}
