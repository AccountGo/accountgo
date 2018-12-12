using Core.Domain.Security;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace Api.Data.Migrations.ApiDb
{
    // ReSharper disable once UnusedMember.Global
    [DbContext(typeof(ApiDbContext))]
    [Migration("20181130185044_InitialCreateData")]
    public class InitialCreateData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                nameof(SecurityGroup),
                new[]
                {
                    nameof(SecurityGroup.Id), nameof(SecurityGroup.Name), nameof(SecurityGroup.DisplayName)
                },
                new object[,]
                {
                    { 1, "AccountsReceivable", "Accounts Receivable"},
                    { 2, "AccountsPayable", "Accounts Payable" },
                    { 3, "Financial", "Financial"},
                    { 4, "SystemAdministration", "System Administration"}
                });

            migrationBuilder.InsertData(
                nameof(SecurityRole),
                new[]
                {
                    nameof(SecurityRole.Id), nameof(SecurityRole.Name), nameof(SecurityRole.DisplayName), nameof(SecurityRole.SysAdmin), nameof(SecurityRole.System)
                },
                new object[,]
                {
                    { 1, "SystemAdministrators", "System Administrators", 1, 1 },
                    { 2, "GeneralUsers", "General Users", 0, 1 }
                });

            migrationBuilder.InsertData(
                nameof(SecurityPermission),
                new[]
                {
                    nameof(SecurityPermission.Id), nameof(SecurityPermission.Name), nameof(SecurityPermission.DisplayName), nameof(SecurityPermission.SecurityGroupId)
                },
                new object[,]
                {
                    { 1, "ManageUsers", "Manage Users", 1 }
                });

            // note: warning requires knowledge of AspNetUser table which is leaked from database
            // todo: create a user registration process that executes on initial setup.
            migrationBuilder.InsertData(
                "AspNetUsers",
                new[]
                {
                "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName"
                },
                new object[,]
                {
                { Guid.NewGuid(), 0, Guid.NewGuid(), "admin@accountgo.ph", 0, 1, "ADMIN@ACCOUNTGO.PH", "ADMIN@ACCOUNTGO.PH", "AQAAAAEAACcQAAAAEOxDmtWUR4F6ZycBAXzB0Wz5c0yduXEQVIgZwGPEOKRdfKq1dmqleAPMjvInBp+xow==", 0, Guid.NewGuid(), 0, "admin@accountgo.ph" }
                });

            migrationBuilder.InsertData(
                nameof(User),
                new[]
                {
                nameof(User.Id), nameof(User.Lastname), nameof(User.Firstname), nameof(User.UserName), nameof(User.EmailAddress)
                },
                new object[,]
                {
                { 1, "System", "Admin", "admin@accountgo.ph", "admin@accountgo.ph" }
                });
        }
    }
}
