CREATE TABLE [dbo].[TaxGroupTax] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [TaxId] [int] NOT NULL,
   [TaxGroupId] [int] NOT NULL

   ,CONSTRAINT [PK_dbo.TaxGroupTax] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_TaxGroupId] ON [dbo].[TaxGroupTax] ([TaxGroupId])
CREATE NONCLUSTERED INDEX [IX_TaxId] ON [dbo].[TaxGroupTax] ([TaxId])

GO
