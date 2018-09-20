CREATE TABLE [dbo].[SalesReceiptHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CustomerId] [int] NOT NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [AccountToDebitId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [Amount] [decimal](18,2) NOT NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesReceiptHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountToDebitId] ON [dbo].[SalesReceiptHeader] ([AccountToDebitId])
CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[SalesReceiptHeader] ([CustomerId])
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[SalesReceiptHeader] ([GeneralLedgerHeaderId])

GO
