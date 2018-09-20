CREATE TABLE [dbo].[PurchaseOrderLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PurchaseOrderHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [Cost] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.PurchaseOrderLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[PurchaseOrderLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[PurchaseOrderLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_PurchaseOrderHeaderId] ON [dbo].[PurchaseOrderLine] ([PurchaseOrderHeaderId])

GO
