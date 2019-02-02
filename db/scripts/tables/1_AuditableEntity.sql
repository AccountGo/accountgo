CREATE TABLE [dbo].[AuditableEntity] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [EntityName] [nvarchar](50) NOT NULL,
   [EnableAudit] [bit] NOT NULL
       DEFAULT ((1))

   ,CONSTRAINT [PK_AuditableEntity] PRIMARY KEY CLUSTERED ([Id])
)


GO
