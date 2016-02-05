CREATE TABLE [dbo].[JournalEntryHeader] (
    [Id]                    INT            IDENTITY (1, 1) NOT NULL,
    [GeneralLedgerHeaderId] INT            NULL,
    [PartyId]               INT            NULL,
    [VoucherType]           INT            NULL,
    [Date]                  DATETIME       NOT NULL,
    [Memo]                  NVARCHAR (MAX) NULL,
    [ReferenceNo]           NVARCHAR (MAX) NULL,
	[Posted]				BIT NULL,
    CONSTRAINT [PK_dbo.JournalEntryHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.JournalEntryHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.JournalEntryHeader_dbo.Party_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Party] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[JournalEntryHeader]([GeneralLedgerHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PartyId]
    ON [dbo].[JournalEntryHeader]([PartyId] ASC);

