ALTER TABLE [dbo].[SecurityRolePermission] WITH CHECK ADD CONSTRAINT [FK_SecurityRolePermission_SecurityPermission]
   FOREIGN KEY([SecurityPermissionId]) REFERENCES [dbo].[SecurityPermission] ([Id])

GO
ALTER TABLE [dbo].[SecurityRolePermission] WITH CHECK ADD CONSTRAINT [FK_SecurityRolePermission_SecurityRole]
   FOREIGN KEY([SecurityRoleId]) REFERENCES [dbo].[SecurityRole] ([Id])

GO
