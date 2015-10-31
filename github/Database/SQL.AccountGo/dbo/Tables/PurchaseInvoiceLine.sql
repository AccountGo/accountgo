CREATE TABLE [dbo].[PurchaseInvoiceLine] (
    [Id]                        INT             IDENTITY (1, 1) NOT NULL,
    [PurchaseInvoiceHeaderId]   INT             NOT NULL,
    [ItemId]                    INT             NOT NULL,
    [TaxId]                     INT             NULL,
    [MeasurementId]             INT             NOT NULL,
    [InventoryControlJournalId] INT             NULL,
    [Quantity]                  DECIMAL (18, 2) NOT NULL,
    [ReceivedQuantity]          DECIMAL (18, 2) NULL,
    [Cost]                      DECIMAL (18, 2) NULL,
    [Discount]                  DECIMAL (18, 2) NULL,
    [Amount]                    DECIMAL (18, 2) NOT NULL,
    [CreatedBy]                 NVARCHAR (MAX)  NULL,
    [CreatedOn]                 DATETIME        NOT NULL,
    [ModifiedBy]                NVARCHAR (MAX)  NULL,
    [ModifiedOn]                DATETIME        NOT NULL,
    CONSTRAINT [PK_dbo.PurchaseInvoiceLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.InventoryControlJournal_InventoryControlJournalId] FOREIGN KEY ([InventoryControlJournalId]) REFERENCES [dbo].[InventoryControlJournal] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.PurchaseInvoiceHeader_PurchaseInvoiceHeaderId] FOREIGN KEY ([PurchaseInvoiceHeaderId]) REFERENCES [dbo].[PurchaseInvoiceHeader] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.PurchaseInvoiceLine_dbo.Tax_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [dbo].[Tax] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_InventoryControlJournalId]
    ON [dbo].[PurchaseInvoiceLine]([InventoryControlJournalId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ItemId]
    ON [dbo].[PurchaseInvoiceLine]([ItemId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[PurchaseInvoiceLine]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoiceHeaderId]
    ON [dbo].[PurchaseInvoiceLine]([PurchaseInvoiceHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TaxId]
    ON [dbo].[PurchaseInvoiceLine]([TaxId] ASC);

