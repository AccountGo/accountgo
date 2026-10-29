// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api.Data.DataProtection
{
    public class DbXmlRepository : IXmlRepository
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<DbXmlRepository> _logger;

        public DbXmlRepository(IServiceProvider services, ILogger<DbXmlRepository> logger)
        {
            _services = services;
            _logger = logger;
        }

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            try
            {
                using var scope = _services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                var keys = context.DataProtectionKeys.ToList();
                return keys.Select(k => XElement.Parse(k.Xml)).ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve DataProtection keys from database. Returning empty key set.");
                return Array.Empty<XElement>();
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            try
            {
                using var scope = _services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                var existing = context.DataProtectionKeys.FirstOrDefault(k => k.FriendlyName == friendlyName);
                if (existing != null)
                {
                    existing.Xml = element.ToString(SaveOptions.DisableFormatting);
                }
                else
                {
                    context.DataProtectionKeys.Add(new DataProtectionKeyEntity
                    {
                        FriendlyName = friendlyName,
                        Xml = element.ToString(SaveOptions.DisableFormatting)
                    });
                }
                context.SaveChanges();
                _logger.LogInformation("Stored DataProtection key '{FriendlyName}' to database.", friendlyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist DataProtection key '{FriendlyName}' to database.", friendlyName);
                throw;
            }
        }
    }
}
