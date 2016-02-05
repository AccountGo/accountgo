CREATE TABLE [dbo].[GeneralLedgerLine] (
    [Id]                    INT             IDENTITY (1, 1) NOT NULL,
    [GeneralLedgerHeaderId] INT             NOT NULL,
    [AccountId]             INT             NOT NULL,
    [DrCr]                  INT             NOT NULL,
    [Amount]                DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.GeneralLedgerLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.GeneralLedgerLine_dbo.Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.GeneralLedgerLine_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountId]
    ON [dbo].[GeneralLedgerLine]([AccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[GeneralLedgerLine]([GeneralLedgerHeaderId] ASC);

