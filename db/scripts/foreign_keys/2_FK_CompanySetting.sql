ALTER TABLE [dbo].[CompanySetting] WITH CHECK ADD CONSTRAINT [FK_dbo.CompanySetting_dbo.Company_CompanyId]
   FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Company] ([Id])
   ON DELETE CASCADE

GO
