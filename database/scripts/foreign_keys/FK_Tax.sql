ALTER TABLE [dbo].[Tax] WITH CHECK ADD CONSTRAINT [FK_dbo.Tax_dbo.Account_PurchasingAccountId]
   FOREIGN KEY([PurchasingAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[Tax] WITH CHECK ADD CONSTRAINT [FK_dbo.Tax_dbo.Account_SalesAccountId]
   FOREIGN KEY([SalesAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
