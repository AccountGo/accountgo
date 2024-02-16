//-----------------------------------------------------------------------
// <copyright file="IRepository.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

namespace Core.Data
{
    using Domain;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Repository
    /// </summary>
    public partial interface IRepository<T> where T : BaseEntity
    {
        T GetById(object id);
        void Insert(T entity);
        void Update(T entity);
        void Delete(T entity);
        IQueryable<T> Table { get; }

        IQueryable<T> GetAllIncluding(params System.Linq.Expressions.Expression<Func<T, object>>[] includeProperties);
    }

    public interface IEfTransaction
    {
        IDisposable BeginTransaction();
        Task<IDisposable> BeginTransactionAsync();
        void Commit();
        Task CommitAsync();
        void Rollback();
        Task RollbackAsync();
    }
}
