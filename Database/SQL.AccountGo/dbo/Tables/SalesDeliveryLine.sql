CREATE TABLE [dbo].[SalesDeliveryLine] (
    [Id]                    INT             IDENTITY (1, 1) NOT NULL,
    [SalesDeliveryHeaderId] INT             NOT NULL,
    [ItemId]                INT             NULL,
    [MeasurementId]         INT             NULL,
    [Quantity]              DECIMAL (18, 2) NOT NULL,
    [Price]                 DECIMAL (18, 2) NOT NULL,
    [Discount]              DECIMAL (18, 2) NOT NULL,
	[SalesInvoiceLineId]		INT             NULL,
    CONSTRAINT [PK_dbo.SalesDeliveryLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]),
	CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.SalesInvoiceLine_SalesInvoiceLineId] FOREIGN KEY ([SalesInvoiceLineId]) REFERENCES [dbo].[SalesInvoiceLine] ([Id]),
    CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.SalesDeliveryLine_dbo.SalesDeliveryHeader_SalesDeliveryHeaderId] FOREIGN KEY ([SalesDeliveryHeaderId]) REFERENCES [dbo].[SalesDeliveryHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[SalesDeliveryLine]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[SalesDeliveryLine]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesDeliveryHeaderId]
    ON [dbo].[SalesDeliveryLine]([SalesDeliveryHeaderId] ASC);

