ALTER TABLE [dbo].[JournalEntryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.JournalEntryLine_dbo.Account_AccountId]
   FOREIGN KEY([AccountId]) REFERENCES [dbo].[Account] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[JournalEntryLine] WITH CHECK ADD CONSTRAINT [FK_dbo.JournalEntryLine_dbo.JournalEntryHeader_JournalEntryHeaderId]
   FOREIGN KEY([JournalEntryHeaderId]) REFERENCES [dbo].[JournalEntryHeader] ([Id])
   ON DELETE CASCADE

GO
