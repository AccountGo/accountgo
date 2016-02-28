CREATE TABLE [dbo].[Vendor] (
    [Id]                        INT            NOT NULL IDENTITY,
	[PartyId]                   INT            NULL,
    [No]                        NVARCHAR (MAX) NULL,
    [AccountsPayableAccountId]  INT            NULL,
    [PurchaseAccountId]         INT            NULL,
    [PurchaseDiscountAccountId] INT            NULL,
    [PrimaryContactId]          INT            NULL,
    [PaymentTermId]             INT            NULL,
    [TaxGroupId]				INT				NULL, 
    CONSTRAINT [PK_dbo.Vendor] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Vendor_dbo.Account_AccountsPayableAccountId] FOREIGN KEY ([AccountsPayableAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Vendor_dbo.Account_PurchaseAccountId] FOREIGN KEY ([PurchaseAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Vendor_dbo.Account_PurchaseDiscountAccountId] FOREIGN KEY ([PurchaseDiscountAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.Vendor_dbo.Contact_PrimaryContactId] FOREIGN KEY ([PrimaryContactId]) REFERENCES [dbo].[Contact] ([Id]),
    CONSTRAINT [FK_dbo.Vendor_dbo.Party_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Party] ([Id]),
    CONSTRAINT [FK_dbo.Vendor_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id]),
	CONSTRAINT [FK_dbo.Vendor_dbo.TaxGroup_TaxGroupId] FOREIGN KEY ([TaxGroupId]) REFERENCES [dbo].[TaxGroup] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountsPayableAccountId]
    ON [dbo].[Vendor]([AccountsPayableAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Id]
    ON [dbo].[Vendor]([Id] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PaymentTermId]
    ON [dbo].[Vendor]([PaymentTermId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PrimaryContactId]
    ON [dbo].[Vendor]([PrimaryContactId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseAccountId]
    ON [dbo].[Vendor]([PurchaseAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseDiscountAccountId]
    ON [dbo].[Vendor]([PurchaseDiscountAccountId] ASC);

