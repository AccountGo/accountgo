CREATE TABLE [dbo].[JournalEntryHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [GeneralLedgerHeaderId] [int] NULL,
   [PartyId] [int] NULL,
   [VoucherType] [int] NULL,
   [Date] [datetime] NOT NULL,
   [Memo] [nvarchar](max) NULL,
   [ReferenceNo] [nvarchar](max) NULL,
   [Posted] [bit] NULL

   ,CONSTRAINT [PK_dbo.JournalEntryHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[JournalEntryHeader] ([GeneralLedgerHeaderId])
CREATE NONCLUSTERED INDEX [IX_PartyId] ON [dbo].[JournalEntryHeader] ([PartyId])

GO
