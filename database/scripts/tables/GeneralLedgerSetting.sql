CREATE TABLE [dbo].[GeneralLedgerSetting] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CompanyId] [int] NULL,
   [PayableAccountId] [int] NULL,
   [PurchaseDiscountAccountId] [int] NULL,
   [GoodsReceiptNoteClearingAccountId] [int] NULL,
   [SalesDiscountAccountId] [int] NULL,
   [ShippingChargeAccountId] [int] NULL,
   [PermanentAccountId] [int] NULL,
   [IncomeSummaryAccountId] [int] NULL

   ,CONSTRAINT [PK_dbo.GeneralLedgerSetting] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CompanyId] ON [dbo].[GeneralLedgerSetting] ([CompanyId])
CREATE NONCLUSTERED INDEX [IX_GoodsReceiptNoteClearingAccountId] ON [dbo].[GeneralLedgerSetting] ([GoodsReceiptNoteClearingAccountId])
CREATE NONCLUSTERED INDEX [IX_PayableAccountId] ON [dbo].[GeneralLedgerSetting] ([PayableAccountId])
CREATE NONCLUSTERED INDEX [IX_PurchaseDiscountAccountId] ON [dbo].[GeneralLedgerSetting] ([PurchaseDiscountAccountId])
CREATE NONCLUSTERED INDEX [IX_SalesDiscountAccountId] ON [dbo].[GeneralLedgerSetting] ([SalesDiscountAccountId])
CREATE NONCLUSTERED INDEX [IX_ShippingChargeAccountId] ON [dbo].[GeneralLedgerSetting] ([ShippingChargeAccountId])

GO
