CREATE TABLE [dbo].[Address] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [No] [nvarchar](10) NULL,
   [Street] [nvarchar](255) NULL,
   [City] [nvarchar](255) NULL

   ,CONSTRAINT [PK_dbo.Address] PRIMARY KEY CLUSTERED ([Id])
)


GO
