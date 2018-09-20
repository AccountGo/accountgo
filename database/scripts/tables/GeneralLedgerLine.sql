CREATE TABLE [dbo].[GeneralLedgerLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [GeneralLedgerHeaderId] [int] NOT NULL,
   [AccountId] [int] NOT NULL,
   [DrCr] [int] NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.GeneralLedgerLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountId] ON [dbo].[GeneralLedgerLine] ([AccountId])
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[GeneralLedgerLine] ([GeneralLedgerHeaderId])

GO
