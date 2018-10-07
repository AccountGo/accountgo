CREATE TABLE [dbo].[Bank] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Type] [int] NOT NULL,
   [Name] [nvarchar](max) NULL,
   [AccountId] [int] NULL,
   [BankName] [nvarchar](max) NULL,
   [Number] [nvarchar](max) NULL,
   [Address] [nvarchar](max) NULL,
   [IsDefault] [bit] NOT NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.Bank] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountId] ON [dbo].[Bank] ([AccountId])

GO
