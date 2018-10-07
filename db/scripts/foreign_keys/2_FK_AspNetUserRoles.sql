ALTER TABLE [dbo].[AspNetUserRoles] WITH CHECK ADD CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
   FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[AspNetUserRoles] WITH CHECK ADD CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
   FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id])
   ON DELETE CASCADE

GO
