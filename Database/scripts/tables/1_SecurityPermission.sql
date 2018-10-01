CREATE TABLE [dbo].[SecurityPermission] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Name] [nvarchar](100) NULL,
   [DisplayName] [nvarchar](100) NULL,
   [SecurityGroupId] [int] NULL

   ,CONSTRAINT [PK_SecurityPermission] PRIMARY KEY CLUSTERED ([Id])
)


GO
