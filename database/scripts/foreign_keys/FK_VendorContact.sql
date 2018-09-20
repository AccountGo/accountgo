ALTER TABLE [dbo].[VendorContact] WITH CHECK ADD CONSTRAINT [FK_dbo.VendorContact_dbo.Vendor_Id]
   FOREIGN KEY([VendorId]) REFERENCES [dbo].[Account] ([Id])

GO
