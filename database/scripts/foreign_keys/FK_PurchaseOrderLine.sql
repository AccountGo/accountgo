ALTER TABLE [dbo].[PurchaseOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[PurchaseOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[PurchaseOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.PurchaseOrderHeader_PurchaseOrderHeaderId]
   FOREIGN KEY([PurchaseOrderHeaderId]) REFERENCES [dbo].[PurchaseOrderHeader] ([Id])
   ON DELETE CASCADE

GO
