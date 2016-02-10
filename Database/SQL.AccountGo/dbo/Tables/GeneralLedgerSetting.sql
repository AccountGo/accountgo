CREATE TABLE [dbo].[GeneralLedgerSetting] (
    [Id]                                INT            IDENTITY (1, 1) NOT NULL,
    [CompanyId]                         INT            NULL,
    [PayableAccountId]                  INT            NULL,
    [PurchaseDiscountAccountId]         INT            NULL,
    [GoodsReceiptNoteClearingAccountId] INT            NULL,
    [SalesDiscountAccountId]            INT            NULL,
    [ShippingChargeAccountId]           INT            NULL,
	[PermanentAccountId]				INT			   NULL,
	[IncomeSummaryAccountId]			INT			   NULL,
    CONSTRAINT [PK_dbo.GeneralLedgerSetting] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_GoodsReceiptNoteClearingAccountId] FOREIGN KEY ([GoodsReceiptNoteClearingAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_PayableAccountId] FOREIGN KEY ([PayableAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_PurchaseDiscountAccountId] FOREIGN KEY ([PurchaseDiscountAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_SalesDiscountAccountId] FOREIGN KEY ([SalesDiscountAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_ShippingChargeAccountId] FOREIGN KEY ([ShippingChargeAccountId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Company_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Company] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CompanyId]
    ON [dbo].[GeneralLedgerSetting]([CompanyId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GoodsReceiptNoteClearingAccountId]
    ON [dbo].[GeneralLedgerSetting]([GoodsReceiptNoteClearingAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PayableAccountId]
    ON [dbo].[GeneralLedgerSetting]([PayableAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseDiscountAccountId]
    ON [dbo].[GeneralLedgerSetting]([PurchaseDiscountAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesDiscountAccountId]
    ON [dbo].[GeneralLedgerSetting]([SalesDiscountAccountId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ShippingChargeAccountId]
    ON [dbo].[GeneralLedgerSetting]([ShippingChargeAccountId] ASC);

