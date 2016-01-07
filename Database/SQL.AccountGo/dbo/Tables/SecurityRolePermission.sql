CREATE TABLE [dbo].[SecurityRolePermission] (
	[Id] INT IDENTITY(1,1) NOT NULL,
	[SecurityRoleId] INT NULL,
    [SecurityPermissionId] INT NULL
	CONSTRAINT [PK_SecurityRolePermission] PRIMARY KEY CLUSTERED ([Id] ASC),
	CONSTRAINT [FK_SecurityRolePermission_SecurityRole] FOREIGN KEY ([SecurityRoleId]) REFERENCES [dbo].[SecurityRole] ([Id]), 
    CONSTRAINT [FK_SecurityRolePermission_SecurityPermission] FOREIGN KEY ([SecurityPermissionId]) REFERENCES [dbo].[SecurityPermission]([Id])
)
