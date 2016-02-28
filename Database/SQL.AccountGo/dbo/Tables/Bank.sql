CREATE TABLE [dbo].[Bank] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [Type]       INT            NOT NULL,
    [Name]       NVARCHAR (MAX) NULL,
    [AccountId]  INT            NULL,
    [BankName]   NVARCHAR (MAX) NULL,
    [Number]     NVARCHAR (MAX) NULL,
    [Address]    NVARCHAR (MAX) NULL,
    [IsDefault]  BIT            NOT NULL,
    [IsActive]   BIT            NOT NULL,
    CONSTRAINT [PK_dbo.Bank] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Bank_dbo.Account_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountId]
    ON [dbo].[Bank]([AccountId] ASC);

