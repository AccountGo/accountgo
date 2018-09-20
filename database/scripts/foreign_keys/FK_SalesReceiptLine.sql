ALTER TABLE [dbo].[SalesReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.Account_AccountToCreditId]
   FOREIGN KEY([AccountToCreditId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[SalesReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.SalesInvoiceLine_SalesInvoiceLineId]
   FOREIGN KEY([SalesInvoiceLineId]) REFERENCES [dbo].[SalesInvoiceLine] ([Id])

GO
ALTER TABLE [dbo].[SalesReceiptLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.SalesReceiptHeader_SalesReceiptHeaderId]
   FOREIGN KEY([SalesReceiptHeaderId]) REFERENCES [dbo].[SalesReceiptHeader] ([Id])
   ON DELETE CASCADE

GO
