/********************************************************************
*DML Scripts to load data in the ff tables:
SecurityGroup
SecurityRole
SecurityPermission
*********************************************************************/

/*******************************************************************
--- Inserts Data in SecurityGroup Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [dbo].[SecurityGroup]) 

BEGIN
SET IDENTITY_INSERT [dbo].[SecurityGroup] ON

INSERT INTO [dbo].[SecurityGroup] (Id, GroupName)
VALUES	(1, 'Sales'),
		(2, 'Purchasing'),
		(3, 'Items'),
		(4, 'Financials'),
		(5, 'Administration')

SET IDENTITY_INSERT [dbo].[SecurityGroup] OFF
END

IF EXISTS(SELECT 1 FROM [dbo].[SecurityGroup] ) 
BEGIN
	PRINT 'Data for table SecurityGroup uploaded successfully'
END
GO

/*******************************************************************
--- Inserts Data in SecurityRole Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [dbo].[SecurityRole]) 

BEGIN
SET IDENTITY_INSERT [dbo].[SecurityRole] ON

INSERT INTO [dbo].[SecurityRole] (Id, RoleName, SysAdmin)
VALUES	(1, 'SystemAdministrator', 1),
		(2, 'Guest', 0)

SET IDENTITY_INSERT [dbo].[SecurityRole] OFF
END

IF EXISTS(SELECT 1 FROM [dbo].[SecurityRole]) 
BEGIN
	PRINT 'Data for table SecurityRole uploaded successfully'
END
GO

/*******************************************************************
--- Inserts Data in SecurityPermission Table ---
********************************************************************/
IF NOT EXISTS (SELECT 1 FROM [dbo].[SecurityPermission]) 

BEGIN
SET IDENTITY_INSERT [dbo].[SecurityPermission] ON

INSERT INTO [dbo].[SecurityPermission] (Id, PermissionName, DisplayName, SecurityGroupId)
VALUES		(1, 'EditSalesOrder', 'Edit Sales Order', 1)

SET IDENTITY_INSERT [dbo].[SecurityPermission] OFF
END
GO
IF EXISTS(SELECT 1 FROM [dbo].[SecurityPermission]) 
BEGIN
	PRINT 'Data for table SecurityPermission uploaded successfully'
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetUsers]) 
BEGIN
SET IDENTITY_INSERT [dbo].[AspNetUsers] ON
INSERT [dbo].[AspNetUsers] ([Id], [Email], [EmailConfirmed], [PasswordHash], [SecurityStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEndDateUtc], [LockoutEnabled], [AccessFailedCount], [UserName]) 
VALUES (N'c12b7ec4-72ab-4960-918a-1a397b15dfd7', N'admin@email.com', 0, N'AMAeZqmeyoa5vcvxJ3f3B6YEgLImAbzrlleAXKX2zer/C0TVx0J6gjzyo47yU5YwGw==', N'8b035264-31e2-4b8f-8ea1-2c608debcd14', NULL, 0, 0, NULL, 1, 0, N'admin')
SET IDENTITY_INSERT [dbo].[AspNetUsers] OFF
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[User]) 
BEGIN
SET IDENTITY_INSERT [dbo].[User] ON 
INSERT [dbo].[User] ([Id], [Lastname], [Firstname], [UserName], [EmailAddress]) VALUES (1, N'System', N'Administrator', N'admin', 'admin@email.com')
SET IDENTITY_INSERT [dbo].[User] OFF
END


