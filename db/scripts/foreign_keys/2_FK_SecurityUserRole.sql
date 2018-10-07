ALTER TABLE [dbo].[SecurityUserRole] WITH CHECK ADD CONSTRAINT [FK_SecurityUserRole_SecurityRole]
   FOREIGN KEY([SecurityRoleId]) REFERENCES [dbo].[SecurityRole] ([Id])

GO
ALTER TABLE [dbo].[SecurityUserRole] WITH CHECK ADD CONSTRAINT [FK_SecurityUserRole_User]
   FOREIGN KEY([UserId]) REFERENCES [dbo].[User] ([Id])

GO
