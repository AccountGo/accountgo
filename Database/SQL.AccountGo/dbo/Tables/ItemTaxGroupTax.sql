CREATE TABLE [dbo].[ItemTaxGroupTax] (
    [Id]             INT            IDENTITY (1, 1) NOT NULL,
    [TaxId]          INT            NOT NULL,
    [ItemTaxGroupId] INT            NOT NULL,
    [IsExempt]       BIT            NOT NULL,
    CONSTRAINT [PK_dbo.ItemTaxGroupTax] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.ItemTaxGroupTax_dbo.ItemTaxGroup_ItemTaxGroupId] FOREIGN KEY ([ItemTaxGroupId]) REFERENCES [dbo].[ItemTaxGroup] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.ItemTaxGroupTax_dbo.Tax_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [dbo].[Tax] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_ItemTaxGroupId]
    ON [dbo].[ItemTaxGroupTax]([ItemTaxGroupId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TaxId]
    ON [dbo].[ItemTaxGroupTax]([TaxId] ASC);

