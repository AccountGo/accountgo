CREATE TABLE [dbo].[AuditableAttribute] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [AuditableEntityId] [int] NOT NULL,
   [AttributeName] [nvarchar](50) NOT NULL,
   [EnableAudit] [bit] NOT NULL
       DEFAULT ((1))

   ,CONSTRAINT [PK_AuditableAttribute] PRIMARY KEY CLUSTERED ([Id])
)


GO
