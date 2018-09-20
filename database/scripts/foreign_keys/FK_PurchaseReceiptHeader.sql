ALTER TABLE [dbo].[PurchaseReceiptHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[PurchaseReceiptHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseReceiptHeader_dbo.Vendor_VendorId]
   FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendor] ([Id])

GO
