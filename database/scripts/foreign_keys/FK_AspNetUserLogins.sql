ALTER TABLE [dbo].[AspNetUserLogins] WITH CHECK ADD CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
   FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id])
   ON DELETE CASCADE

GO
