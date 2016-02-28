CREATE TABLE [dbo].[Customer] (
    [Id]                             INT            NOT NULL IDENTITY,
	[PartyId]                        INT            NULL,
    [No]                             NVARCHAR (MAX) NULL,
    [PrimaryContactId]               INT            NULL,
    [TaxGroupId]                     INT            NULL,
    [AccountsReceivableAccountId]    INT            NULL,
    [SalesAccountId]                 INT            NULL,
    [SalesDiscountAccountId]         INT            NULL,
	[CustomerAdvancesAccountId] INT            NULL,
    [PromptPaymentDiscountAccountId] INT            NULL,
    [PaymentTermId]                  INT            NULL,
    CONSTRAINT [PK_dbo.Customer] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Customer_dbo.Account_AccountsReceivableAccountId] FOREIGN KEY ([AccountsReceivableAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.Account_PromptPaymentDiscountAccountId] FOREIGN KEY ([PromptPaymentDiscountAccountId]) REFERENCES [dbo].[Account] ([Id]),
	CONSTRAINT [FK_dbo.Customer_dbo.Account_CustomerAdvancesAccountId] FOREIGN KEY ([CustomerAdvancesAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.Account_SalesAccountId] FOREIGN KEY ([SalesAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.Account_SalesDiscountAccountId] FOREIGN KEY ([SalesDiscountAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.Contact_PrimaryContactId] FOREIGN KEY ([PrimaryContactId]) REFERENCES [dbo].[Contact] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.Party_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Party] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id]),
    CONSTRAINT [FK_dbo.Customer_dbo.TaxGroup_TaxGroupId] FOREIGN KEY ([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountsReceivableAccountId]
    ON [dbo].[Customer]([AccountsReceivableAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Id]
    ON [dbo].[Customer]([Id] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PaymentTermId]
    ON [dbo].[Customer]([PaymentTermId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PrimaryContactId]
    ON [dbo].[Customer]([PrimaryContactId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PromptPaymentDiscountAccountId]
    ON [dbo].[Customer]([PromptPaymentDiscountAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesAccountId]
    ON [dbo].[Customer]([SalesAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesDiscountAccountId]
    ON [dbo].[Customer]([SalesDiscountAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TaxGroupId]
    ON [dbo].[Customer]([TaxGroupId] ASC);

