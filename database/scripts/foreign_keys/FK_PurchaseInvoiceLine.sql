ALTER TABLE [dbo].[PurchaseInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.InventoryControlJournal_InventoryControlJournalId]
   FOREIGN KEY([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id])

GO
ALTER TABLE [dbo].[PurchaseInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[PurchaseInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[PurchaseInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.PurchaseInvoiceHeader_PurchaseInvoiceHeaderId]
   FOREIGN KEY([PurchaseInvoiceHeaderId]) REFERENCES [dbo].[PurchaseInvoiceHeader] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[PurchaseInvoiceLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.PurchaseOrderLine_PurchaseOrderLineId]
   FOREIGN KEY([PurchaseOrderLineId]) REFERENCES [dbo].[PurchaseOrderLine] ([Id])

GO
