ALTER TABLE [dbo].[SalesOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesOrderLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[SalesOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesOrderLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[SalesOrderLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesOrderLine_dbo.SalesOrderHeader_SalesOrderHeaderId]
   FOREIGN KEY([SalesOrderHeaderId]) REFERENCES [dbo].[SalesOrderHeader] ([Id])
   ON DELETE CASCADE

GO
