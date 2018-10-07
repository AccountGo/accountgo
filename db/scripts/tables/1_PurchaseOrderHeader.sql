CREATE TABLE [dbo].[PurchaseOrderHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [VendorId] [int] NOT NULL,
   [No] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [Description] [nvarchar](max) NULL,
   [PurchaseInvoiceHeaderId] [int] NULL,
   [PaymentTermId] [int] NULL,
   [ReferenceNo] [nvarchar](max) NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.PurchaseOrderHeader] PRIMARY KEY CLUSTERED ([Id])
)


GO
