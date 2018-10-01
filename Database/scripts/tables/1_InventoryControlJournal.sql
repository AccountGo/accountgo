CREATE TABLE [dbo].[InventoryControlJournal] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [DocumentType] [int] NOT NULL,
   [INQty] [decimal](18,2) NULL,
   [OUTQty] [decimal](18,2) NULL,
   [Date] [datetime] NOT NULL,
   [TotalCost] [decimal](18,2) NULL,
   [TotalAmount] [decimal](18,2) NULL,
   [IsReverse] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.InventoryControlJournal] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_ItemId] ON [dbo].[InventoryControlJournal] ([ItemId])
CREATE NONCLUSTERED INDEX [IX_MeasurementId] ON [dbo].[InventoryControlJournal] ([MeasurementId])

GO
