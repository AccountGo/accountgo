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