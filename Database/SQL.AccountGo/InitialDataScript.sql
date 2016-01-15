SET IDENTITY_INSERT [dbo].[Measurement] ON 

GO
INSERT [dbo].[Measurement] ([Id], [Code], [Description]) VALUES (1, N'EA', N'Each')
GO
INSERT [dbo].[Measurement] ([Id], [Code], [Description]) VALUES (2, N'PK', N'Pack')
GO
INSERT [dbo].[Measurement] ([Id], [Code], [Description]) VALUES (3, N'MO', N'Monthly')
GO
INSERT [dbo].[Measurement] ([Id], [Code], [Description]) VALUES (4, N'HR', N'Hour')
GO
SET IDENTITY_INSERT [dbo].[Measurement] OFF
GO
SET IDENTITY_INSERT [dbo].[ItemCategory] ON 

GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 4, 1, NULL, NULL, NULL, NULL, NULL, N'Charges', N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, NULL, NULL, NULL, NULL, NULL, N'Components', N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, 4, NULL, NULL, NULL, NULL, NULL, N'Services', N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 1, 1, NULL, NULL, NULL, NULL, NULL, N'Systems', N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemCategory] OFF
GO
SET IDENTITY_INSERT [dbo].[Tax] ON 

GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, NULL, NULL, N'VAT 5%', N'VAT5%', CAST(5.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, NULL, NULL, N'VAT 12%', N'VAT12%', CAST(12.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, NULL, NULL, N'Export Tax 1%', N'exportTax1%', CAST(1.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, NULL, NULL, N'VAT 10%', N'VAT10%', CAST(10.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Tax] OFF
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroup] ON 

GO
INSERT [dbo].[ItemTaxGroup] ([Id], [Name], [IsFullyExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Regular', 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[ItemTaxGroup] ([Id], [Name], [IsFullyExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Preferenced', 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroup] OFF
GO
SET IDENTITY_INSERT [dbo].[Party] ON 

GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 3, NULL, NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.607' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 1, N'ABC Customer', NULL, NULL, NULL, NULL, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, NULL, NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 2, N'ABC Sample Supplier', NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Party] OFF
GO
SET IDENTITY_INSERT [dbo].[PaymentTerm] ON 

GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Payment due within 10 days', 3, 10, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Due 15th Of the Following Month 	', 4, 15, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, N'Cash Only', 2, NULL, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[PaymentTerm] OFF
GO
INSERT [dbo].[Contact] ([Id], [ContactType], [PartyId], [FirstName], [LastName], [MiddleName]) VALUES (1, 1, 2, N'John', N'Doe', NULL)
GO
INSERT [dbo].[Contact] ([Id], [ContactType], [PartyId], [FirstName], [LastName], [MiddleName]) VALUES (3, 2, 4, N'Mary', N'Walter', NULL)
GO
INSERT [dbo].[Vendor] ([Id], [No], [AccountsPayableAccountId], [PurchaseAccountId], [PurchaseDiscountAccountId], [PrimaryContactId], [PaymentTermId]) VALUES (4, N'1', NULL, NULL, NULL, 3, NULL)
GO
SET IDENTITY_INSERT [dbo].[Item] ON 

GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 3, 3, NULL, NULL, NULL, NULL, NULL, NULL, NULL, N'1', NULL, N'HOA Dues', N'HOA Dues', N'HOA Dues', NULL, CAST(350.00 AS Decimal(18, 2)), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, N'2', NULL, N'Car Sticker', N'Car Sticker', N'Car Sticker', CAST(40.00 AS Decimal(18, 2)), CAST(100.00 AS Decimal(18, 2)), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 2, 1, 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, N'3', NULL, N'Optical Mouse', N'Optical Mouse', N'Optical Mouse', CAST(30.00 AS Decimal(18, 2)), CAST(80.00 AS Decimal(18, 2)), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Item] OFF
GO
SET IDENTITY_INSERT [dbo].[TaxGroup] ON 

GO
INSERT [dbo].[TaxGroup] ([Id], [Description], [TaxAppliedToShipping], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'VAT', 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[TaxGroup] ([Id], [Description], [TaxAppliedToShipping], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Export', 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[TaxGroup] OFF
GO
INSERT [dbo].[Customer] ([Id], [No], [PrimaryContactId], [TaxGroupId], [AccountsReceivableAccountId], [SalesAccountId], [SalesDiscountAccountId], [PromptPaymentDiscountAccountId], [PaymentTermId]) VALUES (2, N'1', 1, NULL, 7, NULL, NULL, NULL, NULL)
GO
SET IDENTITY_INSERT [dbo].[Banks] ON 

GO
INSERT [dbo].[Banks] ([Id], [Type], [Name], [AccountId], [BankName], [Number], [Address], [IsDefault], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, N'General Fund', 4, N'GFB', N'1234567890', N'123 Main St.', 1, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[Banks] ([Id], [Type], [Name], [AccountId], [BankName], [Number], [Address], [IsDefault], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 3, N'Petty Cash Account', 6, NULL, NULL, NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Banks] OFF
GO
SET IDENTITY_INSERT [dbo].[Company] ON 

GO
INSERT [dbo].[Company] ([Id], [Name], [ShortName], [Logo], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Financial Solutions Inc.', N'FSI', NULL, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Company] OFF
GO
SET IDENTITY_INSERT [dbo].[GeneralLedgerSetting] ON 

GO
INSERT [dbo].[GeneralLedgerSetting] ([Id], [CompanyId], [PayableAccountId], [PurchaseDiscountAccountId], [GoodsReceiptNoteClearingAccountId], [SalesDiscountAccountId], [ShippingChargeAccountId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, NULL, NULL, 14, 56, 57, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[GeneralLedgerSetting] OFF
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroupTax] ON 

GO
INSERT [dbo].[ItemTaxGroupTax] ([Id], [TaxId], [ItemTaxGroupId], [IsExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 1, 0, N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[ItemTaxGroupTax] ([Id], [TaxId], [ItemTaxGroupId], [IsExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 2, 0, N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroupTax] OFF
GO
SET IDENTITY_INSERT [dbo].[TaxGroupTax] ON 

GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, 2, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[TaxGroupTax] OFF
GO
INSERT [dbo].[AspNetUsers] ([Id], [Email], [EmailConfirmed], [PasswordHash], [SecurityStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEndDateUtc], [LockoutEnabled], [AccessFailedCount], [UserName]) VALUES (N'c12b7ec4-72ab-4960-918a-1a397b15dfd7', N'admin@email.com', 0, N'AMAeZqmeyoa5vcvxJ3f3B6YEgLImAbzrlleAXKX2zer/C0TVx0J6gjzyo47yU5YwGw==', N'8b035264-31e2-4b8f-8ea1-2c608debcd14', NULL, 0, 0, NULL, 1, 0, N'admin')
GO
SET IDENTITY_INSERT [dbo].[FinancialYear] ON 

GO
INSERT [dbo].[FinancialYear] ([Id], [FiscalYearCode], [FiscalYearName], [StartDate], [EndDate], [IsActive]) VALUES (1, N'FY1516', N'FY 2015/2016', CAST(N'2016-01-01 00:00:00.00' AS DateTime), CAST(N'2016-12-31 00:00:00.000' AS DateTime), 1)
GO
SET IDENTITY_INSERT [dbo].[FinancialYear] OFF
GO

SET IDENTITY_INSERT [dbo].[AuditableEntity] ON 

GO
INSERT [dbo].[AuditableEntity] ([Id], [EntityName], [EnableAudit]) VALUES (1, N'Account', 1)
GO
SET IDENTITY_INSERT [dbo].[AuditableEntity] OFF
GO
SET IDENTITY_INSERT [dbo].[AuditableAttribute] ON 

GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (1, 1, N'AccountCode', 1)
GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (2, 1, N'AccountName', 1)
GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (3, 1, N'Description', 1)
GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (5, 1, N'IsCash', 1)
GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (6, 1, N'ParentAccountId', 1)
GO
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (7, 1, N'IsContraAccount', 1)
GO
SET IDENTITY_INSERT [dbo].[AuditableAttribute] OFF
GO