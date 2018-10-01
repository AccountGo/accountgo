ALTER TABLE [dbo].[Bank] WITH CHECK ADD CONSTRAINT [FK_dbo.Bank_dbo.Account_AccountId]
   FOREIGN KEY([AccountId]) REFERENCES [dbo].[Account] ([Id])

GO
