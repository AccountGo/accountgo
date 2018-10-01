CREATE TABLE [dbo].[ItemTaxGroupTax] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [TaxId] [int] NOT NULL,
   [ItemTaxGroupId] [int] NOT NULL,
   [IsExempt] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.ItemTaxGroupTax] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_ItemTaxGroupId] ON [dbo].[ItemTaxGroupTax] ([ItemTaxGroupId])
CREATE NONCLUSTERED INDEX [IX_TaxId] ON [dbo].[ItemTaxGroupTax] ([TaxId])

GO
