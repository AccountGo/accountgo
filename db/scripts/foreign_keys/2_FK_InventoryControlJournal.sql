ALTER TABLE [dbo].[InventoryControlJournal] WITH CHECK ADD CONSTRAINT [FK_dbo.InventoryControlJournal_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[InventoryControlJournal] WITH CHECK ADD CONSTRAINT [FK_dbo.InventoryControlJournal_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])
   ON DELETE CASCADE

GO
