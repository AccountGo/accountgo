CREATE TABLE [dbo].[Banks] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [Type]       INT            NOT NULL,
    [Name]       NVARCHAR (MAX) NULL,
    [AccountId]  INT            NULL,
    [BankName]   NVARCHAR (MAX) NULL,
    [Number]     NVARCHAR (MAX) NULL,
    [Address]    NVARCHAR (MAX) NULL,
    [IsDefault]  BIT            NOT NULL,
    [IsActive]   BIT            NOT NULL,
    [CreatedBy]  NVARCHAR (MAX) NULL,
    [CreatedOn]  DATETIME       NOT NULL,
    [ModifiedBy] NVARCHAR (MAX) NULL,
    [ModifiedOn] DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.Banks] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Banks_dbo.Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountId]
    ON [dbo].[Banks]([AccountId] ASC);

