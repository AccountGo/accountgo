ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Account_CostOfGoodsSoldAccountId]
   FOREIGN KEY([CostOfGoodsSoldAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Account_InventoryAccountId]
   FOREIGN KEY([InventoryAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Account_InventoryAdjustmentAccountId]
   FOREIGN KEY([InventoryAdjustmentAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.ItemCategory_ItemCategoryId]
   FOREIGN KEY([ItemCategoryId]) REFERENCES [dbo].[ItemCategory] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.ItemTaxGroup_ItemTaxGroupId]
   FOREIGN KEY([ItemTaxGroupId]) REFERENCES [dbo].[ItemTaxGroup] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Vendor_PreferredVendorId]
   FOREIGN KEY([PreferredVendorId]) REFERENCES [dbo].[Vendor] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Measurement_PurchaseMeasurementId]
   FOREIGN KEY([PurchaseMeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Account_SalesAccountId]
   FOREIGN KEY([SalesAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Measurement_SellMeasurementId]
   FOREIGN KEY([SellMeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[Item] WITH CHECK ADD CONSTRAINT [FK_dbo.Item_dbo.Measurement_SmallestMeasurementId]
   FOREIGN KEY([SmallestMeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
