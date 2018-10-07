CREATE TABLE [dbo].[SalesInvoiceLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesInvoiceHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [InventoryControlJournalId] [int] NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [Amount] [decimal](18,2) NOT NULL,
   [SalesOrderLineId] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesInvoiceLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_InventoryControlJournalId] ON [dbo].[SalesInvoiceLine] ([InventoryControlJournalId])
CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[SalesInvoiceLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[SalesInvoiceLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceHeaderId] ON [dbo].[SalesInvoiceLine] ([SalesInvoiceHeaderId])

GO
