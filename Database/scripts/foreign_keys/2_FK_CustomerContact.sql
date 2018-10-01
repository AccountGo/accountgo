ALTER TABLE [dbo].[CustomerContact] WITH CHECK ADD CONSTRAINT [FK_dbo.CustomerContact_dbo.Customer_Id]
   FOREIGN KEY([CustomerId]) REFERENCES [dbo].[Customer] ([Id])

GO
