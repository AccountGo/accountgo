CREATE TABLE [dbo].[ItemTaxGroup] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [Name]          NVARCHAR (MAX) NULL,
    [IsFullyExempt] BIT            NOT NULL,
    [CreatedBy]     NVARCHAR (MAX) NULL,
    [CreatedOn]     DATETIME       NOT NULL,
    [ModifiedBy]    NVARCHAR (MAX) NULL,
    [ModifiedOn]    DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.ItemTaxGroup] PRIMARY KEY CLUSTERED ([Id] ASC)
);

