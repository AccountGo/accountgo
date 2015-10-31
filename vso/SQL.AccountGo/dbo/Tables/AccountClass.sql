CREATE TABLE [dbo].[AccountClass] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [Name]          NVARCHAR (MAX) NULL,
    [NormalBalance] NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_dbo.AccountClass] PRIMARY KEY CLUSTERED ([Id] ASC)
);

