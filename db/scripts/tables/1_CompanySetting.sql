CREATE TABLE [dbo].[CompanySetting] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CompanyId] [int] NOT NULL,
   [ReportingFramework] [int] NOT NULL CONSTRAINT [DF_CompanySetting_ReportingFramework] DEFAULT 1,
   [TaxRegime] [int] NOT NULL CONSTRAINT [DF_CompanySetting_TaxRegime] DEFAULT 0,
   [FiscalYearPattern] [int] NOT NULL CONSTRAINT [DF_CompanySetting_FiscalYearPattern] DEFAULT 1,
   [FiscalYearStartMonth] [int] NULL,
   [BooksStartDate] [date] NOT NULL CONSTRAINT [DF_CompanySetting_BooksStartDate] DEFAULT '1970-01-01',
   [IndustryTemplate] [int] NOT NULL CONSTRAINT [DF_CompanySetting_IndustryTemplate] DEFAULT 1,
   [InventoryCostingMethod] [int] NOT NULL CONSTRAINT [DF_CompanySetting_InventoryCostingMethod] DEFAULT 0,
   [CoaTemplateId] [nvarchar](100) NOT NULL CONSTRAINT [DF_CompanySetting_CoaTemplateId] DEFAULT 'legacy-default',
   [OnboardingChecklistDismissedAt] [datetime2] NULL,
   [OnboardingChecklistJson] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.CompanySetting] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CompanyId] ON [dbo].[CompanySetting] ([CompanyId])

GO
