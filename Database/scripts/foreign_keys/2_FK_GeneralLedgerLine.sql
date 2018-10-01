ALTER TABLE [dbo].[GeneralLedgerLine] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerLine_dbo.Account_AccountId]
   FOREIGN KEY([AccountId]) REFERENCES [dbo].[Account] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[GeneralLedgerLine] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerLine_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])
   ON DELETE CASCADE

GO
