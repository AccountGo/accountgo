CREATE TABLE [dbo].[PurchaseReceiptLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PurchaseReceiptHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [InventoryControlJournalId] [int] NULL,
   [PurchaseInvoiceLineId] [int] NULL,
   [MeasurementId] [int] NOT NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [ReceivedQuantity] [decimal](18,2) NOT NULL,
   [Cost] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.PurchaseReceiptLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_InventoryControlJournalId] ON [dbo].[PurchaseReceiptLine] ([InventoryControlJournalId])
CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[PurchaseReceiptLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[PurchaseReceiptLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_PurchaseOrderLineId] ON [dbo].[PurchaseReceiptLine] ([PurchaseInvoiceLineId])
CREATE NONCLUSTERED INDEX [IX_PurchaseReceiptHeaderId] ON [dbo].[PurchaseReceiptLine] ([PurchaseReceiptHeaderId])

GO
