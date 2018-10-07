CREATE TABLE [dbo].[Company] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CompanyCode] [nvarchar](20) NULL,
   [Name] [nvarchar](max) NULL,
   [ShortName] [nvarchar](max) NULL,
   [Logo] [image] NULL

   ,CONSTRAINT [PK_dbo.Company] PRIMARY KEY CLUSTERED ([Id])
)


GO
