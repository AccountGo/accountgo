//-----------------------------------------------------------------------
// <copyright file="IRepository.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
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
        Task InsertAsync(T entity);
        void Update(T entity);
        Task UpdateAsync(T enitity);
        void Delete(T entity);
        Task DeleteAsync(T entity);
        IQueryable<T> Table { get; }

        IQueryable<T> GetAllIncluding(params System.Linq.Expressions.Expression<Func<T, object>>[] includeProperties);
    }
}
