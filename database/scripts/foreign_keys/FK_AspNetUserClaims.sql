ALTER TABLE [dbo].[AspNetUserClaims] WITH CHECK ADD CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
   FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id])
   ON DELETE CASCADE

GO
