ALTER TABLE [dbo].[PurchaseOrderHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
ALTER TABLE [dbo].[PurchaseOrderHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.Vendor_VendorId]
   FOREIGN KEY([VendorId]) REFERENCES [dbo].[Vendor] ([Id])

GO
