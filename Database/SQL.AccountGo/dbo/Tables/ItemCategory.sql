CREATE TABLE [dbo].[ItemCategory] (
    [Id]                       INT            IDENTITY (1, 1) NOT NULL,
    [ItemType]                 INT            NOT NULL,
    [MeasurementId]            INT            NULL,
    [SalesAccountId]           INT            NULL,
    [InventoryAccountId]       INT            NULL,
    [CostOfGoodsSoldAccountId] INT            NULL,
    [AdjustmentAccountId]      INT            NULL,
    [AssemblyAccountId]        INT            NULL,
    [Name]                     NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_dbo.ItemCategory] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_AdjustmentAccountId] FOREIGN KEY ([AdjustmentAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_AssemblyAccountId] FOREIGN KEY ([AssemblyAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_CostOfGoodsSoldAccountId] FOREIGN KEY ([CostOfGoodsSoldAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_InventoryAccountId] FOREIGN KEY ([InventoryAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Account_SalesAccountId] FOREIGN KEY ([SalesAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.ItemCategory_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AdjustmentAccountId]
    ON [dbo].[ItemCategory]([AdjustmentAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_AssemblyAccountId]
    ON [dbo].[ItemCategory]([AssemblyAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_CostOfGoodsSoldAccountId]
    ON [dbo].[ItemCategory]([CostOfGoodsSoldAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_InventoryAccountId]
    ON [dbo].[ItemCategory]([InventoryAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MeasurementId]
    ON [dbo].[ItemCategory]([MeasurementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesAccountId]
    ON [dbo].[ItemCategory]([SalesAccountId] ASC);

