CREATE TABLE [dbo].[TaxGroup] (
    [Id]                   INT            IDENTITY (1, 1) NOT NULL,
    [Description]          NVARCHAR (MAX) NULL,
    [TaxAppliedToShipping] BIT            NOT NULL,
    [IsActive]             BIT            NOT NULL,
    CONSTRAINT [PK_dbo.TaxGroup] PRIMARY KEY CLUSTERED ([Id] ASC)
);

