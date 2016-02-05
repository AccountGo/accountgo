CREATE TABLE [dbo].[Company] (
    [Id]			INT            IDENTITY (1, 1) NOT NULL,
	[CompanyCode]	NVARCHAR(20) NULL,
    [Name]			NVARCHAR (MAX) NULL,
    [ShortName]		NVARCHAR (MAX) NULL,
    [Logo]			IMAGE          NULL,
    CONSTRAINT [PK_dbo.Company] PRIMARY KEY CLUSTERED ([Id] ASC)
);

