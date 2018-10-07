CREATE TABLE [dbo].[SecurityRolePermission] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SecurityRoleId] [int] NULL,
   [SecurityPermissionId] [int] NULL

   ,CONSTRAINT [PK_SecurityRolePermission] PRIMARY KEY CLUSTERED ([Id])
)


GO
