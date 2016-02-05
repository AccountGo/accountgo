CREATE TABLE [dbo].[GeneralLedgerHeader] (
    [Id]           INT            IDENTITY (1, 1) NOT NULL,
    [Date]         DATETIME       NOT NULL,
    [DocumentType] INT            NOT NULL,
    [Description]  NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_dbo.GeneralLedgerHeader] PRIMARY KEY CLUSTERED ([Id] ASC)
);

