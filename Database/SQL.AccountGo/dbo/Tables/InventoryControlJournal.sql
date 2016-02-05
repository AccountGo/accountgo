CREATE TABLE [dbo].[InventoryControlJournal] (
    [Id]            INT             IDENTITY (1, 1) NOT NULL,
    [ItemId]        INT             NOT NULL,
    [MeasurementId] INT             NOT NULL,
    [DocumentType]  INT             NOT NULL,
    [INQty]         DECIMAL (18, 2) NULL,
    [OUTQty]        DECIMAL (18, 2) NULL,
    [Date]          DATETIME        NOT NULL,
    [TotalCost]     DECIMAL (18, 2) NULL,
    [TotalAmount]   DECIMAL (18, 2) NULL,
    [IsReverse]     BIT             NOT NULL,
    CONSTRAINT [PK_dbo.InventoryControlJournal] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.InventoryControlJournal_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.InventoryControlJournal_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[InventoryControlJournal]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[InventoryControlJournal]([MeasurementId] ASC);

