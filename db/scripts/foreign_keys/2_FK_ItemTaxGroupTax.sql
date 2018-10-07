ALTER TABLE [dbo].[ItemTaxGroupTax] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemTaxGroupTax_dbo.ItemTaxGroup_ItemTaxGroupId]
   FOREIGN KEY([ItemTaxGroupId]) REFERENCES [dbo].[ItemTaxGroup] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[ItemTaxGroupTax] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemTaxGroupTax_dbo.Tax_TaxId]
   FOREIGN KEY([TaxId]) REFERENCES [dbo].[Tax] ([Id])
   ON DELETE CASCADE

GO
