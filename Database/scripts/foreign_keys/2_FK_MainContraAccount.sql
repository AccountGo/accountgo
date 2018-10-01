ALTER TABLE [dbo].[MainContraAccount] WITH CHECK ADD CONSTRAINT [FK_MainContraAccount_MainAccountId_Account_AccountId]
   FOREIGN KEY([MainAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[MainContraAccount] WITH CHECK ADD CONSTRAINT [FK_MainContraAccount_RelatedContraAccountId_Account_AccountId]
   FOREIGN KEY([RelatedContraAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
