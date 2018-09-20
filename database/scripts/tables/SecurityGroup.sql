CREATE TABLE [dbo].[SecurityGroup] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Name] [nvarchar](50) NOT NULL,
   [DisplayName] [nvarchar](50) NULL

   ,CONSTRAINT [PK_SecurityGroup] PRIMARY KEY CLUSTERED ([Id])
)


GO
