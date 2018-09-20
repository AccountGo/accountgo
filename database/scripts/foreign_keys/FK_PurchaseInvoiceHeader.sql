ALTER TABLE [dbo].[PurchaseInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[PurchaseInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
ALTER TABLE [dbo].[PurchaseInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.Vendor_VendorId]
   FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendor] ([Id])

GO
