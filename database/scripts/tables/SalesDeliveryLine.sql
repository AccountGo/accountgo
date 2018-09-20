CREATE TABLE [dbo].[SalesDeliveryLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesDeliveryHeaderId] [int] NOT NULL,
   [ItemId] [int] NULL,
   [MeasurementId] [int] NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [Price] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [SalesInvoiceLineId] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesDeliveryLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[SalesDeliveryLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[SalesDeliveryLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_SalesDeliveryHeaderId] ON [dbo].[SalesDeliveryLine] ([SalesDeliveryHeaderId])

GO
