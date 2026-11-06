namespace Api.Data.Schema;

internal static class SqliteRecordChangesSql
{
    public const string CreateTable = "CREATE TABLE IF NOT EXISTS \"RecordChanges\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"CompanyId\" INTEGER NOT NULL, \"EntityType\" TEXT NOT NULL, \"EntityId\" INTEGER NOT NULL, \"Action\" TEXT NOT NULL, \"ChangesJson\" TEXT NOT NULL, \"ActorUserId\" TEXT NULL, \"ActorKind\" TEXT NOT NULL, \"CorrelationId\" TEXT NOT NULL, \"IpAddress\" TEXT NULL, \"OccurredAt\" TEXT NOT NULL);";
    public const string CompanyTimeIndex = "CREATE INDEX IF NOT EXISTS \"IX_RecordChanges_Company_OccurredAt\" ON \"RecordChanges\" (\"CompanyId\", \"OccurredAt\" DESC);";
    public const string EntityIndex = "CREATE INDEX IF NOT EXISTS \"IX_RecordChanges_Company_Entity\" ON \"RecordChanges\" (\"CompanyId\", \"EntityType\", \"EntityId\");";
    public const string ParentIndex = "CREATE INDEX IF NOT EXISTS \"IX_RecordChanges_Company_Parent\" ON \"RecordChanges\" (\"CompanyId\", \"ParentEntityType\", \"ParentEntityId\");";
    public const string ActorIndex = "CREATE INDEX IF NOT EXISTS \"IX_RecordChanges_Company_Actor_OccurredAt\" ON \"RecordChanges\" (\"CompanyId\", \"ActorUserId\", \"OccurredAt\" DESC);";
    public const string UpdateTrigger = "CREATE TRIGGER IF NOT EXISTS \"TR_RecordChanges_Immutable_Update\" BEFORE UPDATE ON \"RecordChanges\" BEGIN SELECT RAISE(ABORT, 'RecordChanges rows are append-only'); END;";
    public const string DeleteTrigger = "CREATE TRIGGER IF NOT EXISTS \"TR_RecordChanges_Immutable_Delete\" BEFORE DELETE ON \"RecordChanges\" BEGIN SELECT RAISE(ABORT, 'RecordChanges rows are append-only'); END;";
}
