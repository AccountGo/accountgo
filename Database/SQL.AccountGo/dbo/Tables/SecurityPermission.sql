CREATE TABLE [dbo].[SecurityPermission] (
	[Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NULL,
	[DisplayName] NVARCHAR(100) NULL,
    [SecurityGroupId] INT NULL
	CONSTRAINT [PK_SecurityPermission] PRIMARY KEY CLUSTERED ([Id] ASC), 
    CONSTRAINT [FK_SecurityPermission_SecurityGroup] FOREIGN KEY ([SecurityGroupId]) REFERENCES [dbo].[SecurityGroup] ([Id])
)
