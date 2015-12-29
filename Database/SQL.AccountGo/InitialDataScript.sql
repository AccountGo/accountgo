
SET IDENTITY_INSERT [dbo].[AccountClass] ON 

GO
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (1, N'Assets', N'Dr')
GO
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (2, N'Liabilities', N'Cr')
GO
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (3, N'Equity', N'Cr')
GO
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (4, N'Revenue', N'Cr')
GO
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (5, N'Expense', N'Dr')
GO
SET IDENTITY_INSERT [dbo].[AccountClass] OFF
GO
SET IDENTITY_INSERT [dbo].[Account] ON 

GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, NULL, 2, N'10000', N'Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.430' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.430' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 1, 1, 2, N'10100', N'Current Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 1, 2, 2, N'10110', N'Cash in Hand and in Bank', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 1, 3, 1, N'10111', N'Regular Checking Account', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (5, 1, 3, 1, N'10112', N'Savings Account', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.440' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (6, 1, 3, 1, N'10113', N'Cash in Hand A/C', NULL, 1, N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (7, 1, 2, 1, N'10120', N'Accounts Receivable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (8, 1, 2, 1, N'10130', N'Other Receivables', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (9, 1, 2, 1, N'10121', N'Allowance for Doubtful Accounts', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (10, 1, 2, 1, N'10140', N'Prepaid Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.443' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (11, 1, 2, 1, N'10150', N'Employee Advances', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (12, 1, 2, 1, N'10700', N'Other Current Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (13, 1, 2, 1, N'10800', N'Inventory', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (14, 1, 2, 1, N'10810', N'Goods Received Clearing Account', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (15, 1, 2, 1, N'10900', N'Stocks of work in progress', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (16, 1, 1, 2, N'10200', N'Noncurrent Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (17, 1, 16, 2, N'10300', N'Fixed Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (18, 1, 17, 1, N'13301', N'Furniture and Fixtures', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (19, 1, 17, 1, N'13302', N'Plants & Equipments', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (20, 1, 17, 1, N'13303', N'Vehicles', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.447' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (21, 1, 17, 1, N'13304', N'Buildings', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (22, 1, 17, 1, N'13305', N'Land', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (23, 1, 17, 1, N'14000', N'Accumulated Depreciations', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (24, 1, 17, 1, N'14001', N'Accumulated Depreciation-Furniture and Fixtures', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (25, 1, 17, 1, N'14002', N'Accumulated Depreciation-Equipment', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (26, 1, 17, 1, N'14003', N'Accumulated Depreciation-Vehicles', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (27, 1, 17, 1, N'14004', N'Accumulated Depreciations-Building', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (28, 1, 17, 1, N'14005', N'Accumulated Depreciation-Building Improvements', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (29, 1, 1, 1, N'15000', N'Other Assets', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.450' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (30, 2, NULL, 2, N'20000', N'Liabilities', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (31, 2, 30, 2, N'20100', N'Current Liabilities', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (32, 2, 31, 1, N'20110', N'Accounts Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (33, 2, 31, 1, N'20201', N'Accrued Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (34, 2, 31, 1, N'20202', N'Wages Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (35, 2, 31, 1, N'20203', N'Deductions Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (36, 2, 31, 1, N'20204', N'Health Insurance Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.453' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (37, 2, 31, 1, N'20300', N'Tax Payables', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (38, 2, 31, 1, N'20301', N'Local Payroll Taxes Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (39, 2, 31, 1, N'20302', N'Income Taxes Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (40, 2, 31, 1, N'20303', N'Other Taxes Payable', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (41, 2, 31, 1, N'20304', N'Advances-H.O. Dues', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (42, 2, 31, 1, N'20400', N'Other Current Liabilities', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (43, 2, 30, 1, N'20500', N'Non-current Liabilities', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (44, 2, 30, 1, N'20600', N'Other Long-term Liabilities', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.457' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (45, 3, NULL, 2, N'30000', N'Equity', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (46, 3, 45, 1, N'30100', N'Member Capital', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (47, 3, 45, 1, N'30200', N'Capital Surplus', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (48, 3, 45, 1, N'30300', N'Retained Surplus', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (49, 3, 45, 1, N'30400', N'Accumulated Profits', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (50, 3, 45, 1, N'30500', N'Accumulated Losses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.460' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (51, 3, 45, 1, N'30600', N'Other Reserves', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (52, 4, NULL, 2, N'40000', N'Revenues', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (53, 4, 52, 1, N'40100', N'Sales A/C', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (54, 4, 52, 1, N'40200', N'Home Owners', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.463' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (55, 4, 52, 1, N'40300', N'Interest Income', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (56, 4, 52, 1, N'40400', N'Sales Discounts', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (57, 4, 52, 1, N'40500', N'Shipping and Handling', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (58, 4, 52, 1, N'40600', N'Other Income', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (59, 5, NULL, 2, N'50000', N'Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (60, 5, 59, 2, N'50100', N'General and Admin Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.467' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (61, 5, 60, 1, N'50101', N'Salary Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (62, 5, 60, 1, N'50102', N'Wages Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (63, 5, 60, 1, N'50103', N'Employee Benefit Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (64, 5, 60, 1, N'50104', N'Employee Benefit Expenses-Health Insurance', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.470' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (65, 5, 60, 1, N'50105', N'Employee Benefit Expenses-Pension Plans', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (66, 5, 60, 1, N'50106', N'Employee Benefit Expenses-Profit Sharing Plan', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (67, 5, 60, 1, N'50107', N'Employee Benefit Expenses-Other', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (68, 5, 60, 1, N'50108', N'Repair and Maintenance Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.473' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (69, 5, 60, 1, N'50109', N'Repair and Maintenance Expenses-Office Equipment', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (70, 5, 60, 1, N'50110', N'Repair and Maintenance Expenses-Vehicle', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (71, 5, 60, 1, N'50111', N'Supplies Expenses-Office', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (72, 5, 60, 1, N'50112', N'Telephone Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (73, 5, 60, 1, N'50113', N'Postage Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.477' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (74, 5, 60, 1, N'50114', N'Insurance Expenses-Vehicle', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (75, 5, 60, 1, N'50115', N'Dues and Subscriptions Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (76, 5, 60, 1, N'50116', N'Depreciation Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (77, 5, 60, 1, N'50117', N'Meals and Entertainment Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.480' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (78, 5, 60, 1, N'50118', N'Travel Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.483' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.483' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (79, 5, 60, 1, N'50119', N'Utilities Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.487' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.487' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (80, 5, 60, 1, N'50120', N'Legal and Professional Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.487' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.487' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (81, 5, 60, 1, N'50121', N'Bank Charges', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.490' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.490' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (82, 5, 59, 1, N'50200', N'Purchase A/C', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.490' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.490' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (83, 5, 59, 1, N'50300', N'Cost of Goods Sold', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.493' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.493' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (84, 5, 59, 1, N'50400', N'Purchase Discounts', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.497' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.497' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (85, 5, 59, 1, N'50500', N'Purchase price Variance', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.497' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.497' AS DateTime))
GO
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (86, 5, 59, 1, N'50600', N'Other Expenses', NULL, 0, N'System', CAST(N'2015-12-17 07:14:24.500' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.500' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Account] OFF
GO
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
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 4, 1, 53, 13, 83, 85, 15, N'Charges', N'System', CAST(N'2015-12-17 07:14:40.860' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.860' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, 53, 13, 83, 85, 15, N'Components', N'System', CAST(N'2015-12-17 07:14:41.047' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.047' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, 4, 53, 13, 83, 85, 15, N'Services', N'System', CAST(N'2015-12-17 07:14:41.303' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.303' AS DateTime))
GO
INSERT [dbo].[ItemCategory] ([Id], [ItemType], [MeasurementId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [AdjustmentAccountId], [AssemblyAccountId], [Name], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 1, 1, 53, 13, 83, 85, 15, N'Systems', N'System', CAST(N'2015-12-17 07:14:41.320' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.320' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemCategory] OFF
GO
SET IDENTITY_INSERT [dbo].[Tax] ON 

GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 37, 37, N'VAT 5%', N'VAT5%', CAST(5.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2015-12-17 07:14:39.997' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 37, 37, N'VAT 12%', N'VAT12%', CAST(12.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 37, 37, N'Export Tax 1%', N'exportTax1%', CAST(1.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime))
GO
INSERT [dbo].[Tax] ([Id], [SalesAccountId], [PurchasingAccountId], [TaxName], [TaxCode], [Rate], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 37, 37, N'VAT 10%', N'VAT10%', CAST(10.00 AS Decimal(18, 2)), 1, N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.000' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Tax] OFF
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroup] ON 

GO
INSERT [dbo].[ItemTaxGroup] ([Id], [Name], [IsFullyExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Regular', 0, N'System', CAST(N'2015-12-17 07:14:40.073' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.073' AS DateTime))
GO
INSERT [dbo].[ItemTaxGroup] ([Id], [Name], [IsFullyExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Preferenced', 0, N'System', CAST(N'2015-12-17 07:14:40.073' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.073' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroup] OFF
GO
SET IDENTITY_INSERT [dbo].[Party] ON 

GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 3, NULL, NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2015-12-17 07:14:41.607' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.607' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 1, N'ABC Customer', NULL, NULL, NULL, NULL, 1, N'System', CAST(N'2015-12-17 07:14:41.607' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.607' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, NULL, NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2015-12-17 07:14:42.037' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.037' AS DateTime))
GO
INSERT [dbo].[Party] ([Id], [PartyType], [Name], [Email], [Website], [Phone], [Fax], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (4, 2, N'ABC Sample Supplier', NULL, NULL, NULL, NULL, 0, N'System', CAST(N'2015-12-17 07:14:42.037' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.037' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Party] OFF
GO
SET IDENTITY_INSERT [dbo].[PaymentTerm] ON 

GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Payment due within 10 days', 3, 10, 1, N'System', CAST(N'2015-12-17 07:14:42.230' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.230' AS DateTime))
GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Due 15th Of the Following Month 	', 4, 15, 1, N'System', CAST(N'2015-12-17 07:14:42.233' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.233' AS DateTime))
GO
INSERT [dbo].[PaymentTerm] ([Id], [Description], [PaymentType], [DueAfterDays], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, N'Cash Only', 2, NULL, 1, N'System', CAST(N'2015-12-17 07:14:42.240' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.240' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[PaymentTerm] OFF
GO
INSERT [dbo].[Contact] ([Id], [ContactType], [PartyId], [FirstName], [LastName], [MiddleName]) VALUES (1, 1, 2, N'John', N'Doe', NULL)
GO
INSERT [dbo].[Contact] ([Id], [ContactType], [PartyId], [FirstName], [LastName], [MiddleName]) VALUES (3, 2, 4, N'Mary', N'Walter', NULL)
GO
INSERT [dbo].[Vendor] ([Id], [No], [AccountsPayableAccountId], [PurchaseAccountId], [PurchaseDiscountAccountId], [PrimaryContactId], [PaymentTermId]) VALUES (4, N'1', 32, 82, 84, 3, NULL)
GO
SET IDENTITY_INSERT [dbo].[Item] ON 

GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 3, 3, NULL, NULL, NULL, 54, NULL, NULL, NULL, N'1', NULL, N'HOA Dues', N'HOA Dues', N'HOA Dues', NULL, CAST(350.00 AS Decimal(18, 2)), N'System', CAST(N'2015-12-17 07:14:40.877' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.877' AS DateTime))
GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, 1, 1, NULL, 1, 53, 13, 83, 85, N'2', NULL, N'Car Sticker', N'Car Sticker', N'Car Sticker', CAST(40.00 AS Decimal(18, 2)), CAST(100.00 AS Decimal(18, 2)), N'System', CAST(N'2015-12-17 07:14:41.047' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.047' AS DateTime))
GO
INSERT [dbo].[Item] ([Id], [ItemCategoryId], [SmallestMeasurementId], [SellMeasurementId], [PurchaseMeasurementId], [PreferredVendorId], [ItemTaxGroupId], [SalesAccountId], [InventoryAccountId], [CostOfGoodsSoldAccountId], [InventoryAdjustmentAccountId], [No], [Code], [Description], [PurchaseDescription], [SellDescription], [Cost], [Price], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 2, 1, 1, 1, NULL, 1, 53, 13, 83, 85, N'3', NULL, N'Optical Mouse', N'Optical Mouse', N'Optical Mouse', CAST(30.00 AS Decimal(18, 2)), CAST(80.00 AS Decimal(18, 2)), N'System', CAST(N'2015-12-17 07:14:41.183' AS DateTime), N'System', CAST(N'2015-12-17 07:14:41.183' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Item] OFF
GO
SET IDENTITY_INSERT [dbo].[TaxGroup] ON 

GO
INSERT [dbo].[TaxGroup] ([Id], [Description], [TaxAppliedToShipping], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'VAT', 0, 1, N'System', CAST(N'2015-12-17 07:14:40.057' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.057' AS DateTime))
GO
INSERT [dbo].[TaxGroup] ([Id], [Description], [TaxAppliedToShipping], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, N'Export', 0, 1, N'System', CAST(N'2015-12-17 07:14:40.060' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.060' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[TaxGroup] OFF
GO
INSERT [dbo].[Customer] ([Id], [No], [PrimaryContactId], [TaxGroupId], [AccountsReceivableAccountId], [SalesAccountId], [SalesDiscountAccountId], [PromptPaymentDiscountAccountId], [PaymentTermId]) VALUES (2, N'1', 1, NULL, 7, NULL, NULL, NULL, NULL)
GO
SET IDENTITY_INSERT [dbo].[Banks] ON 

GO
INSERT [dbo].[Banks] ([Id], [Type], [Name], [AccountId], [BankName], [Number], [Address], [IsDefault], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, N'General Fund', 4, N'GFB', N'1234567890', N'123 Main St.', 1, 1, N'System', CAST(N'2015-12-17 07:14:42.343' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.343' AS DateTime))
GO
INSERT [dbo].[Banks] ([Id], [Type], [Name], [AccountId], [BankName], [Number], [Address], [IsDefault], [IsActive], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 3, N'Petty Cash Account', 6, NULL, NULL, NULL, 0, 1, N'System', CAST(N'2015-12-17 07:14:42.377' AS DateTime), N'System', CAST(N'2015-12-17 07:14:42.377' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Banks] OFF
GO
SET IDENTITY_INSERT [dbo].[Company] ON 

GO
INSERT [dbo].[Company] ([Id], [Name], [ShortName], [Logo], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, N'Financial Solutions Inc.', N'FSI', NULL, N'System', CAST(N'2015-12-17 07:14:24.153' AS DateTime), N'System', CAST(N'2015-12-17 07:14:24.153' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[Company] OFF
GO
SET IDENTITY_INSERT [dbo].[GeneralLedgerSetting] ON 

GO
INSERT [dbo].[GeneralLedgerSetting] ([Id], [CompanyId], [PayableAccountId], [PurchaseDiscountAccountId], [GoodsReceiptNoteClearingAccountId], [SalesDiscountAccountId], [ShippingChargeAccountId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, NULL, NULL, 14, 56, 57, N'System', CAST(N'2015-12-17 07:14:39.937' AS DateTime), N'System', CAST(N'2015-12-17 07:14:39.937' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[GeneralLedgerSetting] OFF
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroupTax] ON 

GO
INSERT [dbo].[ItemTaxGroupTax] ([Id], [TaxId], [ItemTaxGroupId], [IsExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 1, 0, N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime))
GO
INSERT [dbo].[ItemTaxGroupTax] ([Id], [TaxId], [ItemTaxGroupId], [IsExempt], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 2, 0, N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.087' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[ItemTaxGroupTax] OFF
GO
SET IDENTITY_INSERT [dbo].[TaxGroupTax] ON 

GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (1, 1, 1, N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime))
GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (2, 2, 1, N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime))
GO
INSERT [dbo].[TaxGroupTax] ([Id], [TaxId], [TaxGroupId], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) VALUES (3, 3, 2, N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime), N'System', CAST(N'2015-12-17 07:14:40.083' AS DateTime))
GO
SET IDENTITY_INSERT [dbo].[TaxGroupTax] OFF
GO
INSERT [dbo].[AspNetUsers] ([Id], [Email], [EmailConfirmed], [PasswordHash], [SecurityStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEndDateUtc], [LockoutEnabled], [AccessFailedCount], [UserName]) VALUES (N'c12b7ec4-72ab-4960-918a-1a397b15dfd7', N'admin@email.com', 0, N'AMAeZqmeyoa5vcvxJ3f3B6YEgLImAbzrlleAXKX2zer/C0TVx0J6gjzyo47yU5YwGw==', N'8b035264-31e2-4b8f-8ea1-2c608debcd14', NULL, 0, 0, NULL, 1, 0, N'admin')
GO
SET IDENTITY_INSERT [dbo].[FinancialYear] ON 

GO
INSERT [dbo].[FinancialYear] ([Id], [FiscalYearCode], [FiscalYearName], [StartDate], [EndDate], [IsActive]) VALUES (1, N'FY1516', N'FY 2015/2016', CAST(N'2015-01-01 00:00:00.000' AS DateTime), CAST(N'2015-12-31 00:00:00.000' AS DateTime), 1)
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
INSERT [dbo].[AuditableAttribute] ([Id], [AuditableEntityId], [AttributeName], [EnableAudit]) VALUES (4, 1, N'IsCash', 1)
GO
SET IDENTITY_INSERT [dbo].[AuditableAttribute] OFF
GO