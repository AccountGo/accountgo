ALTER TABLE [dbo].[SalesQuoteHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesQuoteHeader_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[SalesQuoteHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesQuoteHeader_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
