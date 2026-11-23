// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public partial class ApiDbContext
{
    public virtual DbSet<AssistantPseudonym> AssistantPseudonyms { get; set; } = null!;

    private static void ConfigureAssistantPseudonymModel(ModelBuilder builder)
    {
        builder.Entity<AssistantPseudonym>(entity =>
        {
            entity.ToTable("AssistantPseudonyms");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.UserId).HasMaxLength(450).IsRequired();
            entity.Property(row => row.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Token).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(32).IsRequired();
            entity.Property(row => row.PublicId).HasMaxLength(64).IsRequired();
            entity.Property(row => row.DisplayText).HasMaxLength(500);
            entity.HasIndex(row => new { row.CompanyId, row.UserId, row.SessionId, row.Token })
                .IsUnique()
                .HasDatabaseName("IX_AssistantPseudonyms_CompanyId_UserId_SessionId_Token");
            entity.HasIndex(row => row.ExpiresAt)
                .HasDatabaseName("IX_AssistantPseudonyms_ExpiresAt");
        });
    }
}
