CREATE TABLE [dbo].[Contact] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ContactType] [int] NOT NULL,
   [PartyId] [int] NULL,
   [FirstName] [nvarchar](max) NULL,
   [LastName] [nvarchar](max) NULL,
   [MiddleName] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.Contact] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_Id] ON [dbo].[Contact] ([Id])

GO
