ALTER TABLE [dbo].[SalesDeliveryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[SalesDeliveryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[SalesDeliveryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.SalesDeliveryHeader_SalesDeliveryHeaderId]
   FOREIGN KEY([SalesDeliveryHeaderId]) REFERENCES [dbo].[SalesDeliveryHeader] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[SalesDeliveryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.SalesInvoiceLine_SalesInvoiceLineId]
   FOREIGN KEY([SalesInvoiceLineId]) REFERENCES [dbo].[SalesInvoiceLine] ([Id])

GO
