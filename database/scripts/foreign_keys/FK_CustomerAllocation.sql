ALTER TABLE [dbo].[CustomerAllocation] WITH CHECK ADD CONSTRAINT [FK_dbo.CustomerAllocation_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[CustomerAllocation] WITH CHECK ADD CONSTRAINT [FK_dbo.CustomerAllocation_dbo.SalesInvoiceHeader_SalesInvoiceHeaderId]
   FOREIGN KEY([SalesInvoiceHeaderId]) REFERENCES [dbo].[SalesInvoiceHeader] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[CustomerAllocation] WITH CHECK ADD CONSTRAINT [FK_dbo.CustomerAllocation_dbo.SalesReceiptHeader_SalesReceiptHeaderId]
   FOREIGN KEY([SalesReceiptHeaderId]) REFERENCES [dbo].[SalesReceiptHeader] ([Id])
   ON DELETE CASCADE

GO
