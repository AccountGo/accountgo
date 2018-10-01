ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Company_CompanyId]
   FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Company] ([Id])

GO
ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_GoodsReceiptNoteClearingAccountId]
   FOREIGN KEY([GoodsReceiptNoteClearingAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_PayableAccountId]
   FOREIGN KEY([PayableAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_PurchaseDiscountAccountId]
   FOREIGN KEY([PurchaseDiscountAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_SalesDiscountAccountId]
   FOREIGN KEY([SalesDiscountAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
ALTER TABLE [dbo].[GeneralLedgerSetting] WITH CHECK ADD CONSTRAINT [FK_dbo.GeneralLedgerSetting_dbo.Account_ShippingChargeAccountId]
   FOREIGN KEY([ShippingChargeAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
