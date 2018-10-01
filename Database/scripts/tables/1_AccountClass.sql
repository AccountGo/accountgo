CREATE TABLE [dbo].[AccountClass] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Name] [nvarchar](max) NULL,
   [NormalBalance] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.AccountClass] PRIMARY KEY CLUSTERED ([Id])
)


GO
