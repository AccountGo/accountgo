ALTER TABLE [dbo].[SalesInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[SalesInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[SalesInvoiceHeader] WITH CHECK ADD CONSTRAINT [FK_SalesInvoiceHeader_PaymentTerm]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
