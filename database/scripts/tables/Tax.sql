CREATE TABLE [dbo].[Tax] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesAccountId] [int] NULL,
   [PurchasingAccountId] [int] NULL,
   [TaxName] [nvarchar](50) NOT NULL,
   [TaxCode] [nvarchar](16) NOT NULL,
   [Rate] [decimal](18,2) NOT NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.Tax] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_PurchasingAccountId] ON [dbo].[Tax] ([PurchasingAccountId])
CREATE NONCLUSTERED INDEX [IX_SalesAccountId] ON [dbo].[Tax] ([SalesAccountId])

GO
