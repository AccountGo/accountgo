CREATE TABLE [dbo].[Item] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ItemCategoryId] [int] NULL,
   [SmallestMeasurementId] [int] NULL,
   [SellMeasurementId] [int] NULL,
   [PurchaseMeasurementId] [int] NULL,
   [PreferredVendorId] [int] NULL,
   [ItemTaxGroupId] [int] NULL,
   [SalesAccountId] [int] NULL,
   [InventoryAccountId] [int] NULL,
   [CostOfGoodsSoldAccountId] [int] NULL,
   [InventoryAdjustmentAccountId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [Code] [nvarchar](max) NULL,
   [Description] [nvarchar](max) NULL,
   [PurchaseDescription] [nvarchar](max) NULL,
   [SellDescription] [nvarchar](max) NULL,
   [Cost] [decimal](18,2) NULL,
   [Price] [decimal](18,2) NULL

   ,CONSTRAINT [PK_dbo.Item] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CostOfGoodsSoldAccountId] ON [dbo].[Item] ([CostOfGoodsSoldAccountId])
CREATE NONCLUSTERED INDEX [IX_InventoryAccountId] ON [dbo].[Item] ([InventoryAccountId])
CREATE NONCLUSTERED INDEX [IX_InventoryAdjustmentAccountId] ON [dbo].[Item] ([InventoryAdjustmentAccountId])
CREATE NONCLUSTERED INDEX [IX_ItemCategoryId] ON [dbo].[Item] ([ItemCategoryId])
CREATE NONCLUSTERED INDEX [IX_ItemTaxGroupId] ON [dbo].[Item] ([ItemTaxGroupId])
CREATE NONCLUSTERED INDEX [IX_PreferredVendorId] ON [dbo].[Item] ([PreferredVendorId])
CREATE NONCLUSTERED INDEX [IX_PurchaseMeasurementId] ON [dbo].[Item] ([PurchaseMeasurementId])
CREATE NONCLUSTERED INDEX [IX_SalesAccountId] ON [dbo].[Item] ([SalesAccountId])
CREATE NONCLUSTERED INDEX [IX_SellMeasurementId] ON [dbo].[Item] ([SellMeasurementId])
CREATE NONCLUSTERED INDEX [IX_SmallestMeasurementId] ON [dbo].[Item] ([SmallestMeasurementId])

GO
