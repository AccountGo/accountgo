CREATE TABLE [dbo].[SalesOrderLine] (
    [Id]                 INT             IDENTITY (1, 1) NOT NULL,
    [SalesOrderHeaderId] INT             NOT NULL,
    [ItemId]             INT             NOT NULL,
    [MeasurementId]      INT             NOT NULL,
    [Quantity]           DECIMAL (18, 2) NOT NULL,
    [Discount]           DECIMAL (18, 2) NOT NULL,
    [Amount]             DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.SalesOrderLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesOrderLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]),
    CONSTRAINT [FK_dbo.SalesOrderLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.SalesOrderLine_dbo.SalesOrderHeader_SalesOrderHeaderId] FOREIGN KEY ([SalesOrderHeaderId]) REFERENCES [dbo].[SalesOrderHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[SalesOrderLine]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[SalesOrderLine]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesOrderHeaderId]
    ON [dbo].[SalesOrderLine]([SalesOrderHeaderId] ASC);

