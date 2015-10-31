CREATE TABLE [dbo].[PurchaseReceiptLine] (
    [Id]                        INT             IDENTITY (1, 1) NOT NULL,
    [PurchaseReceiptHeaderId]   INT             NOT NULL,
    [ItemId]                    INT             NOT NULL,
    [TaxId]                     INT             NULL,
    [InventoryControlJournalId] INT             NULL,
    [PurchaseOrderLineId]       INT             NULL,
    [MeasurementId]             INT             NOT NULL,
    [Quantity]                  DECIMAL (18, 2) NOT NULL,
    [ReceivedQuantity]          DECIMAL (18, 2) NOT NULL,
    [Cost]                      DECIMAL (18, 2) NOT NULL,
    [Discount]                  DECIMAL (18, 2) NOT NULL,
    [Amount]                    DECIMAL (18, 2) NOT NULL,
    [CreatedBy]                 NVARCHAR (MAX)  NULL,
    [CreatedOn]                 DATETIME        NOT NULL,
    [ModifiedBy]                NVARCHAR (MAX)  NULL,
    [ModifiedOn]                DATETIME        NOT NULL,
    CONSTRAINT [PK_dbo.PurchaseReceiptLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.InventoryControlJournal_InventoryControlJournalId] FOREIGN KEY ([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseOrderLine_PurchaseOrderLineId] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [dbo].[PurchaseOrderLine] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseReceiptHeader_PurchaseReceiptHeaderId] FOREIGN KEY ([PurchaseReceiptHeaderId]) REFERENCES [dbo].[PurchaseReceiptHeader] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Tax_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [dbo].[Tax] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_InventoryControlJournalId]
    ON [dbo].[PurchaseReceiptLine]([InventoryControlJournalId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[PurchaseReceiptLine]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[PurchaseReceiptLine]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseOrderLineId]
    ON [dbo].[PurchaseReceiptLine]([PurchaseOrderLineId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseReceiptHeaderId]
    ON [dbo].[PurchaseReceiptLine]([PurchaseReceiptHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TaxId]
    ON [dbo].[PurchaseReceiptLine]([TaxId] ASC);

