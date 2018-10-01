CREATE TABLE [dbo].[SalesInvoiceHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CustomerId] [int] NOT NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [ShippingHandlingCharge] [decimal](18,2) NOT NULL,
   [Status] [int] NULL,
   [PaymentTermId] [int] NULL,
   [ReferenceNo] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.SalesInvoiceHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[SalesInvoiceHeader] ([CustomerId])
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[SalesInvoiceHeader] ([GeneralLedgerHeaderId])

GO
