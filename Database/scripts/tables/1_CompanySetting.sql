CREATE TABLE [dbo].[CompanySetting] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CompanyId] [int] NOT NULL

   ,CONSTRAINT [PK_dbo.CompanySetting] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CompanyId] ON [dbo].[CompanySetting] ([CompanyId])

GO
