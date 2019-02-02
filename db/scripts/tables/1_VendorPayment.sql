CREATE TABLE [dbo].[VendorPayment] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [VendorId] [int] NOT NULL,
   [PurchaseInvoiceHeaderId] [int] NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.VendorPayment] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[VendorPayment] ([GeneralLedgerHeaderId])
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoiceHeaderId] ON [dbo].[VendorPayment] ([PurchaseInvoiceHeaderId])
CREATE NONCLUSTERED INDEX [IX_VendorId] ON [dbo].[VendorPayment] ([VendorId])

GO
