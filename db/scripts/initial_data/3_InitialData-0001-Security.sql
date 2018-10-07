/********************************************************************
*DML Scripts to load data in the ff tables:
SecurityGroup
SecurityRole
SecurityPermission
*********************************************************************/

/*******************************************************************
--- Inserts Data in SecurityGroup Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [SecurityGroup])
BEGIN
SET IDENTITY_INSERT [SecurityGroup] ON

INSERT INTO [SecurityGroup] (Id, Name, DisplayName)
VALUES	(1, 'AccountsReceivable', 'Accounts Receivable'),
		(2, 'AccountsPayable', 'Accounts Payable'),
		(3, 'Financials', 'Financials'),
		(4, 'SystemAdministration', 'System Administration')

SET IDENTITY_INSERT [SecurityGroup] OFF
END

IF EXISTS(SELECT 1 FROM [SecurityGroup] ) 
BEGIN
	PRINT 'Data for table SecurityGroup uploaded successfully'
END
GO

/*******************************************************************
--- Inserts Data in SecurityRole Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [SecurityRole]) 

BEGIN
SET IDENTITY_INSERT [SecurityRole] ON

INSERT INTO [SecurityRole] (Id, Name, DisplayName, SysAdmin, [System])
VALUES	(1, 'SystemAdministrators', 'System Administrators', 1, 1),
		(2, 'GeneralUsers', 'General Users', 0, 1)

SET IDENTITY_INSERT [SecurityRole] OFF
END

IF EXISTS(SELECT 1 FROM [SecurityRole]) 
BEGIN
	PRINT 'Data for table SecurityRole uploaded successfully'
END
GO

/*******************************************************************
--- Inserts Data in SecurityPermission Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [SecurityPermission]) 
BEGIN
SET IDENTITY_INSERT [SecurityPermission] ON

INSERT INTO [SecurityPermission] (Id, Name, DisplayName, SecurityGroupId)
VALUES		(1, 'ManageUsers', 'Manage Users', 1)

SET IDENTITY_INSERT [SecurityPermission] OFF
END
GO
IF EXISTS(SELECT 1 FROM [SecurityPermission]) 
BEGIN
	PRINT 'Data for table SecurityPermission uploaded successfully'
END
GO

/*******************************************************************
--- Insert initial user. username: admin@accountgo.ph | password: P@ssword1 ---
********************************************************************/
INSERT [dbo].[AspNetUsers] ([Id], [AccessFailedCount], [ConcurrencyStamp], [Email], [EmailConfirmed], [LockoutEnabled], [LockoutEnd], [NormalizedEmail], [NormalizedUserName], [PasswordHash], [PhoneNumber], [PhoneNumberConfirmed], [SecurityStamp], [TwoFactorEnabled], [UserName]) VALUES (N'c2a1983a-6e3f-40a2-b141-0a4e827af44e', 0, N'a1f8ccbc-f77a-4cd3-8d76-f24ed7be2d03', N'admin@accountgo.ph', 0, 1, NULL, N'ADMIN@ACCOUNTGO.PH', N'ADMIN@ACCOUNTGO.PH', N'AQAAAAEAACcQAAAAEOxDmtWUR4F6ZycBAXzB0Wz5c0yduXEQVIgZwGPEOKRdfKq1dmqleAPMjvInBp+xow==', NULL, 0, N'544b121a-1973-4403-9a6f-5a6abcec3bf5', 0, N'admin@accountgo.ph')
GO

SET IDENTITY_INSERT [dbo].[User] ON
GO

INSERT [dbo].[User] ([Id], [Lastname], [Firstname], [UserName], [EmailAddress]) VALUES (1, 'System', 'Administrator', N'admin@accountgo.ph', N'admin@accountgo.ph')
GO

SET IDENTITY_INSERT [dbo].[User] OFF
  
GO