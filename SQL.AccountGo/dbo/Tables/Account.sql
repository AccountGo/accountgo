CREATE TABLE [dbo].[Account] (
    [Id]              INT            IDENTITY (1, 1) NOT NULL,
    [AccountClassId]  INT            NOT NULL,
    [ParentAccountId] INT            NULL,
    [AccountType]     INT            NOT NULL,
    [AccountCode]     NVARCHAR (50)  NOT NULL,
    [AccountName]     NVARCHAR (200) NOT NULL,
    [Description]     NVARCHAR (200) NULL,
    [IsCash]          BIT            NOT NULL,
    [AuditTimestamp]  ROWVERSION     NOT NULL,
    [CreatedBy]       NVARCHAR (MAX) NULL,
    [CreatedOn]       DATETIME       NOT NULL,
    [ModifiedBy]      NVARCHAR (MAX) NULL,
    [ModifiedOn]      DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.Account] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Account_dbo.Account_ParentAccountId] FOREIGN KEY ([ParentAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Account_dbo.AccountClass_AccountClassId] FOREIGN KEY ([AccountClassId]) REFERENCES [dbo].[AccountClass] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountClassId]
    ON [dbo].[Account]([AccountClassId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ParentAccountId]
    ON [dbo].[Account]([ParentAccountId] ASC);

