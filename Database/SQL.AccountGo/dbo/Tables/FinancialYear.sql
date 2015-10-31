CREATE TABLE [dbo].[FinancialYear] (
    [Id]             INT            IDENTITY (1, 1) NOT NULL,
    [FiscalYearCode] NVARCHAR (10)  NOT NULL,
    [FiscalYearName] NVARCHAR (100) NOT NULL,
    [StartDate]      DATETIME       NOT NULL,
    [EndDate]        DATETIME       NOT NULL,
    [IsActive]       BIT            NOT NULL,
    CONSTRAINT [PK_dbo.FinancialYear] PRIMARY KEY CLUSTERED ([Id] ASC)
);

