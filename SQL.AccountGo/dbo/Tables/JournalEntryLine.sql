CREATE TABLE [dbo].[JournalEntryLine] (
    [Id]                   INT             IDENTITY (1, 1) NOT NULL,
    [JournalEntryHeaderId] INT             NOT NULL,
    [AccountId]            INT             NOT NULL,
    [DrCr]                 INT             NOT NULL,
    [Amount]               DECIMAL (18, 2) NOT NULL,
    [Memo]                 NVARCHAR (MAX)  NULL,
    CONSTRAINT [PK_dbo.JournalEntryLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.JournalEntryLine_dbo.Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.JournalEntryLine_dbo.JournalEntryHeader_JournalEntryHeaderId] FOREIGN KEY ([JournalEntryHeaderId]) REFERENCES [dbo].[JournalEntryHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountId]
    ON [dbo].[JournalEntryLine]([AccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_JournalEntryHeaderId]
    ON [dbo].[JournalEntryLine]([JournalEntryHeaderId] ASC);

