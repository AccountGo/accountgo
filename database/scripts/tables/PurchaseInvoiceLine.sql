CREATE TABLE [dbo].[PurchaseInvoiceLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PurchaseInvoiceHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [InventoryControlJournalId] [int] NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [ReceivedQuantity] [decimal](18,2) NULL,
   [Cost] [decimal](18,2) NULL,
   [Discount] [decimal](18,2) NULL,
   [Amount] [decimal](18,2) NOT NULL,
   [PurchaseOrderLineId] [int] NULL

   ,CONSTRAINT [PK_dbo.PurchaseInvoiceLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_InventoryControlJournalId] ON [dbo].[PurchaseInvoiceLine] ([InventoryControlJournalId])
CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[PurchaseInvoiceLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[PurchaseInvoiceLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoiceHeaderId] ON [dbo].[PurchaseInvoiceLine] ([PurchaseInvoiceHeaderId])

GO
