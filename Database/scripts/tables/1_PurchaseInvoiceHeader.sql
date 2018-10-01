CREATE TABLE [dbo].[PurchaseInvoiceHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [VendorId] [int] NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [Date] [datetime] NOT NULL,
   [No] [nvarchar](max) NULL,
   [VendorInvoiceNo] [nvarchar](max) NOT NULL,
   [Description] [nvarchar](max) NULL,
   [PaymentTermId] [int] NULL,
   [ReferenceNo] [nvarchar](max) NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.PurchaseInvoiceHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[PurchaseInvoiceHeader] ([GeneralLedgerHeaderId])
CREATE NONCLUSTERED INDEX [IX_VendorId] ON [dbo].[PurchaseInvoiceHeader] ([VendorId])

GO
