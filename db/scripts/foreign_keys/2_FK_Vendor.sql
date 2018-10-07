ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.Account_AccountsPayableAccountId]
   FOREIGN KEY([AccountsPayableAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.Party_PartyId]
   FOREIGN KEY([PartyId]) REFERENCES [dbo].[Party] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.Contact_PrimaryContactId]
   FOREIGN KEY([PrimaryContactId]) REFERENCES [dbo].[Contact] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.Account_PurchaseAccountId]
   FOREIGN KEY([PurchaseAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.Account_PurchaseDiscountAccountId]
   FOREIGN KEY([PurchaseDiscountAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Vendor] WITH CHECK ADD CONSTRAINT [FK_dbo.Vendor_dbo.TaxGroup_TaxGroupId]
   FOREIGN KEY([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id])

GO
