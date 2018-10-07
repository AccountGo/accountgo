ALTER TABLE [dbo].[SecurityPermission] WITH CHECK ADD CONSTRAINT [FK_SecurityPermission_SecurityGroup]
   FOREIGN KEY([SecurityGroupId]) REFERENCES [dbo].[SecurityGroup] ([Id])

GO
