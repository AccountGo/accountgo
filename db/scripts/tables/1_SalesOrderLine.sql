CREATE TABLE [dbo].[SalesOrderLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesOrderHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.SalesOrderLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[SalesOrderLine] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[SalesOrderLine] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_SalesOrderHeaderId] ON [dbo].[SalesOrderLine] ([SalesOrderHeaderId])

GO
