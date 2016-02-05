CREATE TABLE [dbo].[Account] (
    [Id]              INT            IDENTITY (1, 1) NOT NULL,
	[CompanyId]		  INT            NULL,
    [AccountClassId]  INT            NOT NULL,
    [ParentAccountId] INT            NULL,
	[DrOrCrSide] INT            NOT NULL,
    [AccountCode]     NVARCHAR (50)  UNIQUE NOT NULL,
    [AccountName]     NVARCHAR (200) NOT NULL,
    [Description]     NVARCHAR (200) NULL,
    [IsCash]          BIT            NOT NULL,
	[IsContraAccount] BIT            NOT NULL DEFAULT(0),
    [RowVersion]  ROWVERSION     NOT NULL,
    CONSTRAINT [PK_Account] PRIMARY KEY CLUSTERED ([Id] ASC),
	CONSTRAINT [FK_Account_CompanyId_Company_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Company] ([Id]),
    CONSTRAINT [FK_Account_ParentAccountId_Account_AccountId] FOREIGN KEY ([ParentAccountId]) REFERENCES [Account] ([Id]),
    CONSTRAINT [FK_Account_AccountClassId_AccountClass_AccountClassId] FOREIGN KEY ([AccountClassId]) REFERENCES [AccountClass] ([Id]) ON DELETE CASCADE
);

GO


CREATE NONCLUSTERED INDEX [IX_AccountClassId]
    ON [dbo].[Account]([AccountClassId] ASC);

GO


CREATE NONCLUSTERED INDEX [IX_ParentAccountId]
    ON [dbo].[Account]([ParentAccountId] ASC);

GO