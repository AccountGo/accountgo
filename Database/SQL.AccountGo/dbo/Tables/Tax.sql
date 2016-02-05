CREATE TABLE [dbo].[Tax] (
    [Id]                  INT             IDENTITY (1, 1) NOT NULL,
    [SalesAccountId]      INT             NULL,
    [PurchasingAccountId] INT             NULL,
    [TaxName]             NVARCHAR (50)   NOT NULL,
    [TaxCode]             NVARCHAR (16)   NOT NULL,
    [Rate]                DECIMAL (18, 2) NOT NULL,
    [IsActive]            BIT             NOT NULL,
    CONSTRAINT [PK_dbo.Tax] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Tax_dbo.Account_PurchasingAccountId] FOREIGN KEY ([PurchasingAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Tax_dbo.Account_SalesAccountId] FOREIGN KEY ([SalesAccountId]) REFERENCES [dbo].[Account] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_PurchasingAccountId]
    ON [dbo].[Tax]([PurchasingAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesAccountId]
    ON [dbo].[Tax]([SalesAccountId] ASC);

