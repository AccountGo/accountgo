CREATE TABLE [dbo].[Measurement] (
    [Id]          INT            IDENTITY (1, 1) NOT NULL,
    [Code]        NVARCHAR (MAX) NULL,
    [Description] NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_dbo.Measurement] PRIMARY KEY CLUSTERED ([Id] ASC)
);

