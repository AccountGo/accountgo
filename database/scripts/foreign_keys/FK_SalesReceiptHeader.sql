ALTER TABLE [dbo].[SalesReceiptHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.Account_AccountToDebitId]
   FOREIGN KEY([AccountToDebitId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[SalesReceiptHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[SalesReceiptHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
