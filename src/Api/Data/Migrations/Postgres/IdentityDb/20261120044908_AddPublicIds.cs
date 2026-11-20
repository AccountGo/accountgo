using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.IdentityDb
{
    /// <inheritdoc />
    public partial class AddPublicIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "UserWebAuthnCredentials",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TrustedDevices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "RefreshTokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");


            foreach (var sql in Api.Data.PublicIdBackfillSql.ForIdentity(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.CreateIndex(
                name: "IX_UserWebAuthnCredentials_PublicId",
                table: "UserWebAuthnCredentials",
                column: "PublicId",
                unique: true,
                filter: "\"PublicId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_PublicId",
                table: "TrustedDevices",
                column: "PublicId",
                unique: true,
                filter: "\"PublicId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_PublicId",
                table: "RefreshTokens",
                column: "PublicId",
                unique: true,
                filter: "\"PublicId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserWebAuthnCredentials_PublicId",
                table: "UserWebAuthnCredentials");

            migrationBuilder.DropIndex(
                name: "IX_TrustedDevices_PublicId",
                table: "TrustedDevices");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_PublicId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "UserWebAuthnCredentials");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TrustedDevices");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "RefreshTokens");
        }
    }
}
