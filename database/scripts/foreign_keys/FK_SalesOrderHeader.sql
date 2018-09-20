ALTER TABLE [dbo].[SalesOrderHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesOrderHeader_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[SalesOrderHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesOrderHeader_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
