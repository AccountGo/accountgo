ALTER TABLE [dbo].[SalesInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceLine_dbo.InventoryControlJournal_InventoryControlJournalId]
   FOREIGN KEY([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id])

GO
ALTER TABLE [dbo].[SalesInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[SalesInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[SalesInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceLine_dbo.SalesInvoiceHeader_SalesInvoiceHeaderId]
   FOREIGN KEY([SalesInvoiceHeaderId]) REFERENCES [dbo].[SalesInvoiceHeader] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[SalesInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceLine_dbo.SalesOrderLine_SalesOrderLineId]
   FOREIGN KEY([SalesOrderLineId]) REFERENCES [dbo].[SalesOrderLine] ([Id])

GO
