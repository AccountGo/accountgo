using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class DocumentsLibraryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentCaptureLinks_CompanyId_CaptureId",
                table: "DocumentCaptureLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "DocumentCaptures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedBy",
                table: "DocumentCaptures",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "DocumentCaptures",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DocumentCaptures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "DocumentCaptures",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentDate",
                table: "DocumentCaptures",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DocumentCaptures",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiledAt",
                table: "DocumentCaptures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiledBy",
                table: "DocumentCaptures",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DocumentCaptures",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "DocumentCaptures",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "DocumentCaptureLinks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "source");

            migrationBuilder.CreateTable(
                name: "DocumentLibraryRevisions",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLibraryRevisions", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_DocumentLibraryRevisions_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_Filed",
                table: "DocumentCaptures",
                columns: new[] { "CompanyId", "FiledAt" },
                filter: "[FiledAt] IS NOT NULL AND [DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureLinks_Company_Capture_Target",
                table: "DocumentCaptureLinks",
                columns: new[] { "CompanyId", "CaptureId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureLinks_SourceRole",
                table: "DocumentCaptureLinks",
                columns: new[] { "CompanyId", "CaptureId" },
                unique: true,
                filter: "[Role] = 'source'");

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"
                    UPDATE ""DocumentCaptures""
                    SET ""FiledAt"" = ""AcceptedAt"",
                        ""FiledBy"" = ""AcceptedBy""
                    WHERE ""FiledAt"" IS NULL
                      AND ""AcceptedAt"" IS NOT NULL
                      AND ""Kind"" IN ('expense-receipt', 'purchase-invoice', 'sales-invoice')
                      AND EXISTS (
                          SELECT 1 FROM ""DocumentCaptureLinks"" l
                          WHERE l.""CompanyId"" = ""DocumentCaptures"".""CompanyId""
                            AND l.""CaptureId"" = ""DocumentCaptures"".""Id"")
                      AND EXISTS (
                          SELECT 1 FROM ""DocumentCaptureBlobs"" b
                          WHERE b.""CompanyId"" = ""DocumentCaptures"".""CompanyId""
                            AND b.""CaptureId"" = ""DocumentCaptures"".""Id"");");
            }
            else
            {
                migrationBuilder.Sql(@"
                    UPDATE [DocumentCaptures]
                    SET [FiledAt] = [AcceptedAt],
                        [FiledBy] = [AcceptedBy]
                    WHERE [FiledAt] IS NULL
                      AND [AcceptedAt] IS NOT NULL
                      AND [Kind] IN (N'expense-receipt', N'purchase-invoice', N'sales-invoice')
                      AND EXISTS (
                          SELECT 1 FROM [DocumentCaptureLinks] l
                          WHERE l.[CompanyId] = [DocumentCaptures].[CompanyId]
                            AND l.[CaptureId] = [DocumentCaptures].[Id])
                      AND EXISTS (
                          SELECT 1 FROM [DocumentCaptureBlobs] b
                          WHERE b.[CompanyId] = [DocumentCaptures].[CompanyId]
                            AND b.[CaptureId] = [DocumentCaptures].[Id]);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"
                    DROP TABLE IF EXISTS temp.""__DocumentsLibraryRollbackGuard"";
                    CREATE TEMP TABLE ""__DocumentsLibraryRollbackGuard"" (""Id"" INTEGER);
                    CREATE TEMP TRIGGER ""__TR_DocumentsLibraryRollbackGuard""
                    BEFORE INSERT ON ""__DocumentsLibraryRollbackGuard""
                    BEGIN
                        SELECT RAISE(ABORT, 'Cannot roll back DocumentsLibraryFoundation: multi-link captures, non-source links, or document rows exist.');
                    END;
                    INSERT INTO ""__DocumentsLibraryRollbackGuard"" (""Id"")
                    SELECT 1
                    WHERE EXISTS (
                        SELECT 1 FROM ""DocumentCaptureLinks""
                        GROUP BY ""CompanyId"", ""CaptureId""
                        HAVING COUNT(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM ""DocumentCaptureLinks""
                        WHERE ""Role"" <> 'source'
                    ) OR EXISTS (
                        SELECT 1 FROM ""DocumentCaptures""
                        WHERE ""Kind"" = 'document'
                    );
                    DROP TABLE temp.""__DocumentsLibraryRollbackGuard"";
                    DROP TABLE ""DocumentLibraryRevisions"";
                    DROP INDEX IF EXISTS ""IX_DocumentCaptures_Filed"";
                    DROP INDEX IF EXISTS ""IX_DocumentCaptureLinks_Company_Capture_Target"";
                    DROP INDEX IF EXISTS ""IX_DocumentCaptureLinks_SourceRole"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""ArchivedAt"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""ArchivedBy"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""Category"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""DeletedAt"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""DeletedBy"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""DocumentDate"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""FileName"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""FiledAt"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""FiledBy"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""Notes"";
                    ALTER TABLE ""DocumentCaptures"" DROP COLUMN ""Title"";
                    ALTER TABLE ""DocumentCaptureLinks"" DROP COLUMN ""Role"";
                    CREATE UNIQUE INDEX ""IX_DocumentCaptureLinks_CompanyId_CaptureId""
                        ON ""DocumentCaptureLinks"" (""CompanyId"", ""CaptureId"");");
                return;
            }
            else
            {
                migrationBuilder.Sql(@"
                    IF EXISTS (
                        SELECT 1 FROM [DocumentCaptureLinks]
                        GROUP BY [CompanyId], [CaptureId]
                        HAVING COUNT(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM [DocumentCaptureLinks]
                        WHERE [Role] <> N'source'
                    ) OR EXISTS (
                        SELECT 1 FROM [DocumentCaptures]
                        WHERE [Kind] = N'document'
                    )
                        THROW 51000, 'Cannot roll back DocumentsLibraryFoundation: multi-link captures, non-source links, or document rows exist.', 1;");
            }

            migrationBuilder.DropTable(
                name: "DocumentLibraryRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentCaptures_Filed",
                table: "DocumentCaptures");

            migrationBuilder.DropIndex(
                name: "IX_DocumentCaptureLinks_Company_Capture_Target",
                table: "DocumentCaptureLinks");

            migrationBuilder.DropIndex(
                name: "IX_DocumentCaptureLinks_SourceRole",
                table: "DocumentCaptureLinks");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "ArchivedBy",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "DocumentDate",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "FiledAt",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "FiledBy",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "DocumentCaptures");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "DocumentCaptureLinks");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureLinks_CompanyId_CaptureId",
                table: "DocumentCaptureLinks",
                columns: new[] { "CompanyId", "CaptureId" },
                unique: true);
        }
    }
}
