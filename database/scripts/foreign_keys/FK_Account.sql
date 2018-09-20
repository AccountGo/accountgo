ALTER TABLE [dbo].[Account] WITH CHECK ADD CONSTRAINT [FK_Account_AccountClassId_AccountClass_AccountClassId]
   FOREIGN KEY([AccountClassId]) REFERENCES [dbo].[AccountClass] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[Account] WITH CHECK ADD CONSTRAINT [FK_Account_CompanyId_Company_CompanyId]
   FOREIGN KEY([CompanyId]) REFERENCES [dbo].[Company] ([Id])

GO
ALTER TABLE [dbo].[Account] WITH CHECK ADD CONSTRAINT [FK_Account_ParentAccountId_Account_AccountId]
   FOREIGN KEY([ParentAccountId]) REFERENCES [dbo].[Account] ([Id])

GO
