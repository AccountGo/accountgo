ALTER TABLE [dbo].[TaxGroupTax] WITH CHECK ADD CONSTRAINT [FK_dbo.TaxGroupTax_dbo.TaxGroup_TaxGroupId]
   FOREIGN KEY([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[TaxGroupTax] WITH CHECK ADD CONSTRAINT [FK_dbo.TaxGroupTax_dbo.Tax_TaxId]
   FOREIGN KEY([TaxId]) REFERENCES [dbo].[Tax] ([Id])
   ON DELETE CASCADE

GO
