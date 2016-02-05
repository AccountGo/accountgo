CREATE TABLE [dbo].[ItemTaxGroup] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [Name]          NVARCHAR (MAX) NULL,
    [IsFullyExempt] BIT            NOT NULL,
    CONSTRAINT [PK_dbo.ItemTaxGroup] PRIMARY KEY CLUSTERED ([Id] ASC)
);

