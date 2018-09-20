ALTER TABLE [dbo].[SalesDeliveryHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.Customer_CustomerId]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
ALTER TABLE [dbo].[SalesDeliveryHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[SalesDeliveryHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.PaymentTerm_PaymentTermId]
   FOREIGN KEY([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])

GO
