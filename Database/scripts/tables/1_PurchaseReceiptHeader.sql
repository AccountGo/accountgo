CREATE TABLE [dbo].[PurchaseReceiptHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [VendorId] [int] NOT NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [Date] [datetime] NOT NULL,
   [No] [nvarchar](max) NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.PurchaseReceiptHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[PurchaseReceiptHeader] ([GeneralLedgerHeaderId])
CREATE NONCLUSTERED INDEX [IX_VendorId] ON [dbo].[PurchaseReceiptHeader] ([VendorId])

GO
