CREATE TABLE [dbo].[Item] (
    [Id]                           INT             IDENTITY (1, 1) NOT NULL,
    [ItemCategoryId]               INT             NULL,
    [SmallestMeasurementId]        INT             NULL,
    [SellMeasurementId]            INT             NULL,
    [PurchaseMeasurementId]        INT             NULL,
    [PreferredVendorId]            INT             NULL,
    [ItemTaxGroupId]               INT             NULL,
    [SalesAccountId]               INT             NULL,
    [InventoryAccountId]           INT             NULL,
    [CostOfGoodsSoldAccountId]     INT             NULL,
    [InventoryAdjustmentAccountId] INT             NULL,
    [No]                           NVARCHAR (MAX)  NULL,
    [Code]                         NVARCHAR (MAX)  NULL,
    [Description]                  NVARCHAR (MAX)  NULL,
    [PurchaseDescription]          NVARCHAR (MAX)  NULL,
    [SellDescription]              NVARCHAR (MAX)  NULL,
    [Cost]                         DECIMAL (18, 2) NULL,
    [Price]                        DECIMAL (18, 2) NULL,
    CONSTRAINT [PK_dbo.Item] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Item_dbo.Account_CostOfGoodsSoldAccountId] FOREIGN KEY ([CostOfGoodsSoldAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Account_InventoryAccountId] FOREIGN KEY ([InventoryAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Account_InventoryAdjustmentAccountId] FOREIGN KEY ([InventoryAdjustmentAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Account_SalesAccountId] FOREIGN KEY ([SalesAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.ItemCategory_ItemCategoryId] FOREIGN KEY ([ItemCategoryId]) REFERENCES [dbo].[ItemCategory] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.ItemTaxGroup_ItemTaxGroupId] FOREIGN KEY ([ItemTaxGroupId]) REFERENCES [dbo].[ItemTaxGroup] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Measurement_PurchaseMeasurementId] FOREIGN KEY ([PurchaseMeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Measurement_SellMeasurementId] FOREIGN KEY ([SellMeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Measurement_SmallestMeasurementId] FOREIGN KEY ([SmallestMeasurementId]) REFERENCES [dbo].[Measurement] ([Id]),
    CONSTRAINT [FK_dbo.Item_dbo.Vendor_PreferredVendorId] FOREIGN KEY ([PreferredVendorId]) REFERENCES [dbo].[Vendor] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CostOfGoodsSoldAccountId]
    ON [dbo].[Item]([CostOfGoodsSoldAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_InventoryAccountId]
    ON [dbo].[Item]([InventoryAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_InventoryAdjustmentAccountId]
    ON [dbo].[Item]([InventoryAdjustmentAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ItemCategoryId]
    ON [dbo].[Item]([ItemCategoryId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ItemTaxGroupId]
    ON [dbo].[Item]([ItemTaxGroupId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PreferredVendorId]
    ON [dbo].[Item]([PreferredVendorId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseMeasurementId]
    ON [dbo].[Item]([PurchaseMeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesAccountId]
    ON [dbo].[Item]([SalesAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SellMeasurementId]
    ON [dbo].[Item]([SellMeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SmallestMeasurementId]
    ON [dbo].[Item]([SmallestMeasurementId] ASC);

