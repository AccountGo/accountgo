ALTER TABLE [dbo].[AuditableAttribute] WITH CHECK ADD CONSTRAINT [FK_AuditableAttribute_AuditableEntity]
   FOREIGN KEY([AuditableEntityId]) REFERENCES [dbo].[AuditableEntity] ([Id])

GO
