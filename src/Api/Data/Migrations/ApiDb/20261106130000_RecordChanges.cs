using Api.Data.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.ApiDb;

[DbContext(typeof(ApiDbContext))]
[Migration("20261106130000_RecordChanges")]
public sealed class RecordChangesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite") return;
        migrationBuilder.Sql(SqliteRecordChangesSql.CreateTable);
        migrationBuilder.Sql(SqliteRecordChangesSql.CompanyTimeIndex);
        migrationBuilder.Sql(SqliteRecordChangesSql.EntityIndex);
        migrationBuilder.Sql(SqliteRecordChangesSql.ActorIndex);
        migrationBuilder.Sql(SqliteRecordChangesSql.UpdateTrigger);
        migrationBuilder.Sql(SqliteRecordChangesSql.DeleteTrigger);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite") return;
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_RecordChanges_Immutable_Update\"");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_RecordChanges_Immutable_Delete\"");
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"RecordChanges\"");
    }
}
