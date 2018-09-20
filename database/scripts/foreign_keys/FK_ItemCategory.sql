ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_AdjustmentAccountId]
   FOREIGN KEY([AdjustmentAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_AssemblyAccountId]
   FOREIGN KEY([AssemblyAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_CostOfGoodsSoldAccountId]
   FOREIGN KEY([CostOfGoodsSoldAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_InventoryAccountId]
   FOREIGN KEY([InventoryAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[ItemCategory] WITH CHECK ADD CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_SalesAccountId]
   FOREIGN KEY([SalesAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
