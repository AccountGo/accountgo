CREATE TABLE [dbo].[Measurement] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Code] [nvarchar](max) NULL,
   [Description] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.Measurement] PRIMARY KEY CLUSTERED ([Id])
)


GO
