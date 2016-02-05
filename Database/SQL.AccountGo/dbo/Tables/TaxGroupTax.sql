CREATE TABLE [dbo].[TaxGroupTax] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [TaxId]      INT            NOT NULL,
    [TaxGroupId] INT            NOT NULL,
    CONSTRAINT [PK_dbo.TaxGroupTax] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.TaxGroupTax_dbo.Tax_TaxId] FOREIGN KEY ([TaxId]) REFERENCES [dbo].[Tax] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.TaxGroupTax_dbo.TaxGroup_TaxGroupId] FOREIGN KEY ([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_TaxGroupId]
    ON [dbo].[TaxGroupTax]([TaxGroupId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TaxId]
    ON [dbo].[TaxGroupTax]([TaxId] ASC);

