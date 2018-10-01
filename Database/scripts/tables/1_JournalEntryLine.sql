CREATE TABLE [dbo].[JournalEntryLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [JournalEntryHeaderId] [int] NOT NULL,
   [AccountId] [int] NOT NULL,
   [DrCr] [int] NOT NULL,
   [Amount] [decimal](18,2) NOT NULL,
   [Memo] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.JournalEntryLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountId] ON [dbo].[JournalEntryLine] ([AccountId])
CREATE NONCLUSTERED INDEX [IX_JournalEntryHeaderId] ON [dbo].[JournalEntryLine] ([JournalEntryHeaderId])

GO
