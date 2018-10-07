ALTER TABLE [dbo].[PurchaseReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.InventoryControlJournal_InventoryControlJournalId]
   FOREIGN KEY([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id])

GO
ALTER TABLE [dbo].[PurchaseReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[PurchaseReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])

GO
ALTER TABLE [dbo].[PurchaseReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseInvoiceLine_PurchaseInvoiceLineId]
   FOREIGN KEY([PurchaseInvoiceLineId]) REFERENCES [dbo].[PurchaseInvoiceLine] ([Id])

GO
ALTER TABLE [dbo].[PurchaseReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseReceiptHeader_PurchaseReceiptHeaderId]
   FOREIGN KEY([PurchaseReceiptHeaderId]) REFERENCES [dbo].[PurchaseReceiptHeader] ([Id])
   ON DELETE CASCADE

GO
