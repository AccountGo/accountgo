CREATE USER [dbo] WITHOUT LOGIN WITH DEFAULT_SCHEMA = dbo
/*ALTER ROLE db_owner ADD MEMBER dbo*/ exec sp_addrolemember 'db_owner', 'dbo'
GO
