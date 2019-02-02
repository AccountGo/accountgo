ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Account_AccountsReceivableAccountId]
   FOREIGN KEY([AccountsReceivableAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Account_CustomerAdvancesAccountId]
   FOREIGN KEY([CustomerAdvancesAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Party_PartyId]
   FOREIGN KEY([PartyId]) REFERENCES [dbo].[Party] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Contact_PrimaryContactId]
   FOREIGN KEY([PrimaryContactId]) REFERENCES [dbo].[Contact] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Account_PromptPaymentDiscountAccountId]
   FOREIGN KEY([PromptPaymentDiscountAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Account_SalesAccountId]
   FOREIGN KEY([SalesAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.Account_SalesDiscountAccountId]
   FOREIGN KEY([SalesDiscountAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Customer] WITH CHECK ADD CONSTRAINT [FK_dbo.Customer_dbo.TaxGroup_TaxGroupId]
   FOREIGN KEY([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id])

GO
