using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class DocumentCaptureFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentCaptures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: ""),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizationVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExtractionGeneration = table.Column<int>(type: "int", nullable: false),
                    ExtractingStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DetectedKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DetectedDocumentCount = table.Column<int>(type: "int", nullable: true),
                    WarningsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16384, nullable: true),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    AcceptedFieldsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    RawText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BytesDeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FieldsWipedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCaptures", x => x.Id);
                    table.UniqueConstraint("AK_DocumentCaptures_CompanyId_Id", x => new { x.CompanyId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "DocumentCaptureAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    ReservedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCaptureAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentCaptureAttempts_DocumentCaptures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "DocumentCaptures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCaptureBlobs",
                columns: table => new
                {
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    DocumentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCaptureBlobs", x => x.CaptureId);
                    table.ForeignKey(
                        name: "FK_DocumentCaptureBlobs_DocumentCaptures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "DocumentCaptures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCaptureLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCaptureLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentCaptureLinks_DocumentCaptures_CompanyId_CaptureId",
                        columns: x => new { x.CompanyId, x.CaptureId },
                        principalTable: "DocumentCaptures",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureAttempts_CompanyId_CaptureId_Generation",
                table: "DocumentCaptureAttempts",
                columns: new[] { "CompanyId", "CaptureId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureAttempts_CompanyId_ReservedAt",
                table: "DocumentCaptureAttempts",
                columns: new[] { "CompanyId", "ReservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureBlobs_CompanyId_CaptureId",
                table: "DocumentCaptureBlobs",
                columns: new[] { "CompanyId", "CaptureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureLinks_CompanyId_CaptureId",
                table: "DocumentCaptureLinks",
                columns: new[] { "CompanyId", "CaptureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptureLinks_CompanyId_TargetType_TargetId",
                table: "DocumentCaptureLinks",
                columns: new[] { "CompanyId", "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_CompanyId_IdempotencyKey",
                table: "DocumentCaptures",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_CompanyId_PublicId",
                table: "DocumentCaptures",
                columns: new[] { "CompanyId", "PublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_CompanyId_SourceHash_Kind_SchemaVersion_CreatedAt",
                table: "DocumentCaptures",
                columns: new[] { "CompanyId", "SourceHash", "Kind", "SchemaVersion", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_CompanyId_Status_ExpiresAt",
                table: "DocumentCaptures",
                columns: new[] { "CompanyId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCaptures_PublicId",
                table: "DocumentCaptures",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentCaptureAttempts");

            migrationBuilder.DropTable(
                name: "DocumentCaptureBlobs");

            migrationBuilder.DropTable(
                name: "DocumentCaptureLinks");

            migrationBuilder.DropTable(
                name: "DocumentCaptures");
        }
    }
}
