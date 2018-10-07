CREATE TABLE [dbo].[FinancialYear] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [FiscalYearCode] [nvarchar](10) NOT NULL,
   [FiscalYearName] [nvarchar](100) NOT NULL,
   [StartDate] [datetime] NOT NULL,
   [EndDate] [datetime] NOT NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.FinancialYear] PRIMARY KEY CLUSTERED ([Id])
)


GO
