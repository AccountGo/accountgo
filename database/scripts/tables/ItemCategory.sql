CREATE TABLE [dbo].[ItemCategory] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ItemType] [int] NOT NULL,
   [MeasurementId] [int] NULL,
   [SalesAccountId] [int] NULL,
   [InventoryAccountId] [int] NULL,
   [CostOfGoodsSoldAccountId] [int] NULL,
   [AdjustmentAccountId] [int] NULL,
   [AssemblyAccountId] [int] NULL,
   [Name] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.ItemCategory] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AdjustmentAccountId] ON [dbo].[ItemCategory] ([AdjustmentAccountId])
CREATE NONCLUSTERED INDEX [IX_AssemblyAccountId] ON [dbo].[ItemCategory] ([AssemblyAccountId])
CREATE NONCLUSTERED INDEX [IX_CostOfGoodsSoldAccountId] ON [dbo].[ItemCategory] ([CostOfGoodsSoldAccountId])
CREATE NONCLUSTERED INDEX [IX_InventoryAccountId] ON [dbo].[ItemCategory] ([InventoryAccountId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[ItemCategory] ([MeasurementId])
CREATE NONCLUSTERED INDEX [IX_SalesAccountId] ON [dbo].[ItemCategory] ([SalesAccountId])

GO
