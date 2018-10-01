CREATE TABLE [dbo].[AuditLog] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Username] [nvarchar](50) NOT NULL,
   [AuditEventDateUTC] [datetime2] NOT NULL,
   [AuditEventType] [int] NOT NULL,
   [TableName] [nvarchar](100) NOT NULL,
   [RecordId] [nvarchar](100) NOT NULL,
   [FieldName] [nvarchar](100) NOT NULL,
   [OriginalValue] [nvarchar](max) NULL,
   [NewValue] [nvarchar](max) NULL

   ,CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([Id])
)


GO
