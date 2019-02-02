CREATE TABLE [dbo].[SalesReceiptLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesReceiptHeaderId] [int] NOT NULL,
   [SalesInvoiceLineId] [int] NULL,
   [ItemId] [int] NULL,
   [AccountToCreditId] [int] NULL,
   [MeasurementId] [int] NULL,
   [Quantity] [decimal](18,2) NULL,
   [Discount] [decimal](18,2) NULL,
   [Amount] [decimal](18,2) NULL,
   [AmountPaid] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.SalesReceiptLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountToCreditId] ON [dbo].[SalesReceiptLine] ([AccountToCreditId])
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceLineId] ON [dbo].[SalesReceiptLine] ([SalesInvoiceLineId])
CREATE NONCLUSTERED INDEX [IX_SalesReceiptHeaderId] ON [dbo].[SalesReceiptLine] ([SalesReceiptHeaderId])

GO
