CREATE TABLE [dbo].[PurchaseReceiptLine] (
    [Id]                        INT             IDENTITY (1, 1) NOT NULL,
    [PurchaseReceiptHeaderId]   INT             NOT NULL,
    [ItemId]                    INT             NOT NULL,
    [InventoryControlJournalId] INT             NULL,
    [PurchaseInvoiceLineId]       INT             NULL,
    [MeasurementId]             INT             NOT NULL,
    [Quantity]                  DECIMAL (18, 2) NOT NULL,
    [ReceivedQuantity]          DECIMAL (18, 2) NOT NULL,
    [Cost]                      DECIMAL (18, 2) NOT NULL,
    [Discount]                  DECIMAL (18, 2) NOT NULL,
    [Amount]                    DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.PurchaseReceiptLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.InventoryControlJournal_InventoryControlJournalId] FOREIGN KEY ([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseInvoiceLine_PurchaseInvoiceLineId] FOREIGN KEY ([PurchaseInvoiceLineId]) REFERENCES [dbo].[PurchaseInvoiceLine] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptLine_dbo.PurchaseReceiptHeader_PurchaseReceiptHeaderId] FOREIGN KEY ([PurchaseReceiptHeaderId]) REFERENCES [dbo].[PurchaseReceiptHeader] ([Id]) ON DELETE CASCADE,    
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
    ON [dbo].[PurchaseReceiptLine]([PurchaseInvoiceLineId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseReceiptHeaderId]
    ON [dbo].[PurchaseReceiptLine]([PurchaseReceiptHeaderId] ASC);

