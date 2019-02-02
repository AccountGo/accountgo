ALTER TABLE [dbo].[JournalEntryHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.JournalEntryHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId]
   FOREIGN KEY([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])

GO
ALTER TABLE [dbo].[JournalEntryHeader] WITH CHECK ADD CONSTRAINT [FK_dbo.JournalEntryHeader_dbo.Party_PartyId]
   FOREIGN KEY([PartyId]) REFERENCES [dbo].[Party] ([Id])

GO
