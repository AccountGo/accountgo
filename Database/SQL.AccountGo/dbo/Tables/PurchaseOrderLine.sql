CREATE TABLE [dbo].[PurchaseOrderLine] (
    [Id]                    INT             IDENTITY (1, 1) NOT NULL,
    [PurchaseOrderHeaderId] INT             NOT NULL,
    [ItemId]                INT             NOT NULL,
    [MeasurementId]         INT             NOT NULL,
    [Quantity]              DECIMAL (18, 2) NOT NULL,
    [Cost]                  DECIMAL (18, 2) NOT NULL,
    [Discount]              DECIMAL (18, 2) NOT NULL,
    [Amount]                DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.PurchaseOrderLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseOrderLine_dbo.PurchaseOrderHeader_PurchaseOrderHeaderId] FOREIGN KEY ([PurchaseOrderHeaderId]) REFERENCES [dbo].[PurchaseOrderHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[PurchaseOrderLine]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[PurchaseOrderLine]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseOrderHeaderId]
    ON [dbo].[PurchaseOrderLine]([PurchaseOrderHeaderId] ASC);

