CREATE TABLE [dbo].[Address] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [CreatedBy]  NVARCHAR (MAX) NULL,
    [CreatedOn]  DATETIME       NOT NULL,
    [ModifiedBy] NVARCHAR (MAX) NULL,
    [ModifiedOn] DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.Address] PRIMARY KEY CLUSTERED ([Id] ASC)
);

