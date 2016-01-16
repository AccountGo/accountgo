IF NOT EXISTS (SELECT 1 FROM [dbo].[AccountClass]) 
BEGIN
SET IDENTITY_INSERT [dbo].[AccountClass] ON
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (1, N'Assets', N'Dr')
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (2, N'Liabilities', N'Cr')
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (3, N'Equity', N'Cr')
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (4, N'Revenue', N'Cr')
INSERT [dbo].[AccountClass] ([Id], [Name], [NormalBalance]) VALUES (5, N'Expense', N'Dr')
SET IDENTITY_INSERT [dbo].[AccountClass] OFF
END

-- Chart of Accounts --
-- [AccountType] - Dr = 1, Cr = 2
SET IDENTITY_INSERT [dbo].[Account] ON 
GO

--ASSETS
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (1, 1, NULL, 1, N'10000', N'Assets', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (2, 1, 1, 1, N'11000', N'Current assets', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (3, 1, 2, 1, N'11100', N'Cash', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (4, 1, 3, 1, N'11110', N'Checking account', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (5, 1, 3, 1, N'11120', N'Deposit account', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (6, 1, 3, 1, N'11130', N'Petty cash', NULL, 1, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (7, 1, 2, 1, N'11200', N'Net accounts receivable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (8, 1, 7, 1, N'11210', N'Accounts receivable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (9, 1, 7, 2, N'11220', N'Provision against receivables', NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (10, 1, 2, 1, N'11300', N'Prepayments', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (11, 1, 10, 1, N'11310', N'Prepaid rent', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (12, 1, 10, 1, N'11320', N'Prepaid wages', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (13, 1, 2, 1, N'11400', N'Other receivables', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (14, 1, 13, 1, N'11410', N'Salary advance', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (15, 1, 13, 1, N'11420', N'Deposits with customers', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (16, 1, 13, 1, N'11500', N'Notes receivable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (17, 1, 13, 1, N'11500', N'Notes receivable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (18, 1, 1, 1, N'12000', N'Non-current assets', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (19, 1, 18, 1, N'12100', N'Property, plant and equipment', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (20, 1, 19, 1, N'12110', N'Vehicles-Cost', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (21, 1, 19, 1, N'12120', N'Equipment-Cost', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (22, 1, 19, 1, N'12130', N'Furniture-Cost', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (23, 1, 19, 2, N'12150', N'Accumulated depreciation', NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (24, 1, 23, 2, N'12160', N'Vehicles-accumulated depreciation', NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (25, 1, 23, 2, N'12170', N'Equipment-accumulated depreciation', NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (26, 1, 23, 2, N'12180', N'Furniture-accumulated depreciation', NULL, 0, 1, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (27, 1, 18, 2, N'12200', N'Intangibles', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (28, 1, 18, 1, N'12300', N'Investments', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

--EQUITY
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (29, 3, NULL, 2, N'20000', N'Shareholder''s Equity', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (30, 3, 29, 2, N'21000', N'Share Capital', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (31, 3, 29, 2, N'22000', N'Additional paid-up capital', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (32, 3, 29, 2, N'23000', N'Retained earnings', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (74, 3, 29, 2, N'24000', N'Unrealized gains and losses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (33, 3, 29, 2, N'25000', N'Treasury stock', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

--LIABILITY
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (34, 2, NULL, 2, N'30000', N'Liabilities', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (35, 2, 34, 2, N'31000', N'Non-current liabilities', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (36, 2, 35, 2, N'31100', N'Bonds Payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (37, 2, 36, 2, N'31110', N'Bond-1', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (38, 2, 36, 2, N'31120', N'Bond-2', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (39, 2, 35, 2, N'31200', N'Long-term bank loans payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (40, 2, 34, 2, N'32000', N'Current liabilities', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (41, 2, 40, 2, N'32200', N'Current loans payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (42, 2, 40, 2, N'32300', N'Current notes payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (43, 2, 40, 2, N'32400', N'Accounts payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (44, 2, 40, 2, N'32500', N'Salary payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (45, 2, 40, 2, N'32600', N'Taxes payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (46, 2, 40, 2, N'32700', N'Utilities payable', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (47, 2, 40, 2, N'32800', N'Accrued expenses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

-- REVENUE
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (48, 4, NULL, 2, N'40000', N'Total income', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (49, 4, 48, 2, N'41000', N'Total revenue', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (50, 4, 49, 2, N'41100', N'Gross sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (51, 4, 50, 2, N'41110', N'Product-A sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (52, 4, 50, 2, N'41120', N'Service-B sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (53, 4, 49, 1, N'41200', N'Sales returns and allowances', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (54, 4, 53, 1, N'41210', N'Product-A sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (55, 4, 53, 1, N'41220', N'Service-B sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (56, 4, 49, 2, N'41500', N'Other income', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

--EXPENSE
INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (57, 5, NULL, 1, N'50000', N'Total Expenses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (58, 5, 57, 1, N'51000', N'Cost of sales', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (59, 5, 58, 1, N'51100', N'Product-A costs', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (60, 5, 59, 1, N'51110', N'Materials', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (61, 5, 59, 1, N'51120', N'Labor', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (62, 5, 59, 1, N'51130', N'Depreciation', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (63, 5, 58, 1, N'51200', N'Service-B costs', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (64, 5, 57, 1, N'52000', N'Selling, admin and general expenses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (65, 5, 64, 1, N'52100', N'Delivery vehicles cost', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (66, 5, 64, 1, N'52200', N'Fuel', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (67, 5, 64, 1, N'52300', N'Salaries', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (68, 5, 67, 1, N'52310', N'Salaries-selling', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (69, 5, 67, 1, N'52320', N'Salaries-admin', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (70, 5, 57, 1, N'52600', N'Rent', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (71, 5, 57, 1, N'52700', N'Depreciation-SGA', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (72, 5, 57, 1, N'52800', N'Other expenses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

INSERT [dbo].[Account] ([Id], [AccountClassId], [ParentAccountId], [AccountType], [AccountCode], [AccountName], [Description], [IsCash], [IsContraAccount], [CreatedBy], [CreatedOn], [ModifiedBy], [ModifiedOn]) 
VALUES (73, 5, 57, 1, N'52900', N'Losses', NULL, 0, 0, N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime), N'System', CAST(N'2016-01-01 00:00:00.00' AS DateTime))
GO

SET IDENTITY_INSERT [dbo].[Account] OFF
GO

/*
Account #	Summary	Nature	Normal or Contra	Sign	Account Name
10000	✓	Asset	Normal	Debit	Assets
11000	✓	Asset	Normal	Debit	Current assets
11100	✓	Asset	Normal	Debit	Cash
11110		Asset	Normal	Debit	Checking account
11120		Asset	Normal	Debit	Deposit account
11130		Asset	Normal	Debit	Petty cash
11200	✓	Asset	Normal	Debit	Net accounts receivable
11210		Asset	Normal	Debit	Accounts receivable
11220		Asset	Contra-account	Credit	Provision against receivables
11300	✓	Asset	Normal	Debit	Prepayments
11310		Asset	Normal	Debit	Prepaid rent
11320		Asset	Normal	Debit	Prepaid wages
11400	✓	Asset	Normal	Debit	Other receivables
11410		Asset	Normal	Debit	Salary advance
11420		Asset	Normal	Debit	Deposits with customers
11500		Asset	Normal	Debit	Notes receivable
12000	✓	Asset	Normal	Debit	Non-current assets
12100	✓	Asset	Normal	Debit	Property, plant and equipment
12110		Asset	Normal	Debit	Vehicles-Cost
12120		Asset	Normal	Debit	Equipment-Cost
12130		Asset	Normal	Debit	Furniture-Cost
12150	✓	Asset	Contra-account	Credit	Accumulated depreciation
12160		Asset	Contra-account	Credit	Vehicles-accumulated depreciation
12170		Asset	Contra-account	Credit	Equipment-accumulated depreciation
12180		Asset	Contra-account	Credit	Furniture-accumulated depreciation
12200		Asset	Normal	Credit	Intangibles
12300		Asset	Normal	Debit	Investments
20000	✓	Equity	Normal	Credit	Shareholder's Equity
21000		Equity	Normal	Credit	Share Capital
22000		Equity	Normal	Credit	Additional paid-up capital
23000		Equity	Normal	Credit	Retained earnings
24000		Equity	Normal	Credit	Unrealized gains and losses
25000		Equity	Contra-account	Debit	Treasury stock
30000	✓	Liability	Normal	Credit	Liabilities
31000	✓	Liability	Normal	Credit	Non-current liabilities
31100	✓	Liability	Normal	Credit	Bonds Payable
31110		Liability	Normal	Credit	Bond-1
31120		Liability	Normal	Credit	Bond-2
31200		Liability	Normal	Credit	Long-term bank loans payable
32000	✓	Liability	Normal	Credit	Current liabilities
32200		Liability	Normal	Credit	Current loans payable
32300		Liability	Normal	Credit	Current notes payable
32400		Liability	Normal	Credit	Accounts payable
32500		Liability	Normal	Credit	Salary payable
32600		Liability	Normal	Credit	Taxes payable
32700		Liability	Normal	Credit	Utilities payable
32800		Liability	Normal	Credit	Accrued expenses
40000	✓	Revenue	Normal	Credit	Total income
41000	✓	Revenue	Normal	Credit	Total revenue
41100	✓	Revenue	Normal	Credit	Gross sales
41110		Revenue	Normal	Credit	Product-A sales
41120		Revenue	Normal	Credit	Service-B sales
41200	✓	Revenue	Normal	Debit	Sales returns and allowances
41210		Revenue	Normal	Debit	Product-A sales
41220		Revenue	Normal	Debit	Service-B sales
41500		Revenue	Normal	Credit	Other income
50000	✓	Expense	Normal	Debit	Total expenses
51000	✓	Expense	Normal	Debit	Cost of sales
51100	✓	Expense	Normal	Debit	Product-A costs
51110		Expense	Normal	Debit	Materials
51120		Expense	Normal	Debit	Labor
51130		Expense	Normal	Debit	Depreciation
51200		Expense	Normal	Debit	Service-B costs
52000	✓	Expense	Normal	Debit	Selling, admin and general expenses
52100		Expense	Normal	Debit	Delivery vehicles cost
52200		Expense	Normal	Debit	Fuel
52300	✓	Expense	Normal	Debit	Salaries
52310		Expense	Normal	Debit	Salaries-selling
52320		Expense	Normal	Debit	Salaries-admin
52600		Expense	Normal	Debit	Rent
52700		Expense	Normal	Debit	Depreciation-SGA
52800		Expense	Normal	Debit	Other expenses
52900		Expense	Normal	Debit	Losses
*/