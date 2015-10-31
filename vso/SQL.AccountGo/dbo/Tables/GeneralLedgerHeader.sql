CREATE TABLE [dbo].[GeneralLedgerHeader] (
    [Id]           INT            IDENTITY (1, 1) NOT NULL,
    [Date]         DATETIME       NOT NULL,
    [DocumentType] INT            NOT NULL,
    [Description]  NVARCHAR (MAX) NULL,
    [CreatedBy]    NVARCHAR (MAX) NULL,
    [CreatedOn]    DATETIME       NOT NULL,
    [ModifiedBy]   NVARCHAR (MAX) NULL,
    [ModifiedOn]   DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.GeneralLedgerHeader] PRIMARY KEY CLUSTERED ([Id] ASC)
);

