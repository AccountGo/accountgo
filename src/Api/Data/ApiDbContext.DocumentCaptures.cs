// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public partial class ApiDbContext
{
    public virtual DbSet<DocumentCapture> DocumentCaptures { get; set; } = null!;
    public virtual DbSet<DocumentCaptureBlob> DocumentCaptureBlobs { get; set; } = null!;
    public virtual DbSet<DocumentCaptureAttempt> DocumentCaptureAttempts { get; set; } = null!;
    public virtual DbSet<DocumentCaptureLink> DocumentCaptureLinks { get; set; } = null!;
    public virtual DbSet<PartyAlias> PartyAliases { get; set; } = null!;

    private static void ConfigureDocumentCaptureModel(ModelBuilder builder)
    {
        builder.Entity<DocumentCapture>(entity =>
        {
            entity.ToTable("DocumentCaptures");
            entity.HasKey(capture => capture.Id);
            entity.HasAlternateKey(capture => new { capture.CompanyId, capture.Id });
            entity.Property(capture => capture.PublicId).HasMaxLength(32).IsRequired();
            entity.Property(capture => capture.Kind).HasMaxLength(40).IsRequired();
            entity.Property(capture => capture.Status).HasMaxLength(20).IsRequired();
            entity.Property(capture => capture.SourceHash).HasMaxLength(64).IsRequired();
            entity.Property(capture => capture.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(capture => capture.NormalizationVersion).HasMaxLength(40).IsRequired();
            entity.Property(capture => capture.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(capture => capture.IdempotencyKey).HasMaxLength(80);
            entity.Property(capture => capture.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(capture => capture.DetectedKind).HasMaxLength(40);
            entity.Property(capture => capture.WarningsJson).HasMaxLength(16_384);
            entity.Property(capture => capture.FieldsJson).HasMaxLength(65_536);
            entity.Property(capture => capture.AcceptedFieldsJson).HasMaxLength(65_536);
            entity.Property(capture => capture.RawText).HasMaxLength(8_000);
            entity.Property(capture => capture.ProviderName).HasMaxLength(100);
            entity.Property(capture => capture.ProviderVersion).HasMaxLength(100);
            entity.Property(capture => capture.ErrorCode).HasMaxLength(80);
            entity.Property(capture => capture.CreatedBy).HasMaxLength(256).IsRequired();
            entity.Property(capture => capture.AcceptedBy).HasMaxLength(256);
            entity.Property(capture => capture.RejectedBy).HasMaxLength(256);
            entity.HasIndex(capture => new { capture.CompanyId, capture.PublicId }).IsUnique();
            entity.HasIndex(capture => new { capture.CompanyId, capture.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.HasIndex(capture => new { capture.CompanyId, capture.SourceHash, capture.Kind, capture.SchemaVersion, capture.CreatedAt });
            entity.HasIndex(capture => capture.ExpiresAt)
                .HasDatabaseName("IX_DocumentCaptures_ExpiresAt_Unaccepted")
                .HasFilter("\"AcceptedAt\" IS NULL AND \"RejectedAt\" IS NULL AND \"BytesDeletedAt\" IS NULL");
        });

        builder.Entity<DocumentCaptureBlob>(entity =>
        {
            entity.ToTable("DocumentCaptureBlobs");
            entity.HasKey(blob => blob.CaptureId);
            entity.Property(blob => blob.DocumentBytes).IsRequired();
            entity.HasOne(blob => blob.Capture)
                .WithOne(capture => capture.Blob)
                .HasForeignKey<DocumentCaptureBlob>(blob => new { blob.CompanyId, blob.CaptureId })
                .HasPrincipalKey<DocumentCapture>(capture => new { capture.CompanyId, capture.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentCaptureAttempt>(entity =>
        {
            entity.ToTable("DocumentCaptureAttempts");
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => new { attempt.CompanyId, attempt.CaptureId, attempt.Generation }).IsUnique();
            entity.HasIndex(attempt => attempt.ReservedAt)
                .HasDatabaseName("IX_DocumentCaptureAttempts_ReservedAt");
            entity.HasOne(attempt => attempt.Capture)
                .WithMany(capture => capture.Attempts)
                .HasForeignKey(attempt => new { attempt.CompanyId, attempt.CaptureId })
                .HasPrincipalKey(capture => new { capture.CompanyId, capture.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentCaptureLink>(entity =>
        {
            entity.ToTable("DocumentCaptureLinks");
            entity.HasKey(link => link.Id);
            entity.Property(link => link.TargetType).HasMaxLength(30).IsRequired();
            entity.Property(link => link.LinkedBy).HasMaxLength(256).IsRequired();
            entity.Property(link => link.TaxTreatment).HasMaxLength(64);
            entity.HasIndex(link => new { link.CompanyId, link.CaptureId }).IsUnique();
            entity.HasIndex(link => new { link.CompanyId, link.TargetType, link.TargetId });
            entity.HasOne(link => link.Capture)
                .WithMany(capture => capture.Links)
                .HasForeignKey(link => new { link.CompanyId, link.CaptureId })
                .HasPrincipalKey(capture => new { capture.CompanyId, capture.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PartyAlias>(entity =>
        {
            entity.ToTable("PartyAliases");
            entity.HasKey(alias => alias.Id);
            entity.Property(alias => alias.Role).HasMaxLength(20).IsRequired();
            entity.Property(alias => alias.NormalizedAlias).HasMaxLength(200).IsRequired();
            entity.Property(alias => alias.CreatedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(alias => new { alias.CompanyId, alias.Role, alias.NormalizedAlias }).IsUnique();
            entity.HasIndex(alias => new { alias.CompanyId, alias.PartyId, alias.Role });
            entity.HasOne(alias => alias.Party)
                .WithMany()
                .HasForeignKey(alias => alias.PartyId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
