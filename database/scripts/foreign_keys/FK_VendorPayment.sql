ALTER TABLE [dbo].[VendorPayment] WITH CHECK ADD CONSTRAINT [FK_dbo.VendorPayment_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[VendorPayment] WITH CHECK ADD CONSTRAINT [FK_dbo.VendorPayment_dbo.PurchaseInvoiceHeader_PurchaseInvoiceHeaderId]
   FOREIGN KEY([PurchaseInvoiceHeaderId]) REFERENCES [dbo].[PurchaseInvoiceHeader] ([Id])

GO
ALTER TABLE [dbo].[VendorPayment] WITH CHECK ADD CONSTRAINT [FK_dbo.VendorPayment_dbo.Vendor_VendorId]
   FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendor] ([Id])

GO
