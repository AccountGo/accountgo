ALTER TABLE [dbo].[Contact] WITH CHECK ADD CONSTRAINT [FK_dbo.Contact_dbo.Party_PartyId]
   FOREIGN KEY([PartyId]) REFERENCES [dbo].[Party] ([Id])

GO
