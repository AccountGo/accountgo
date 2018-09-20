CREATE TABLE [dbo].[User] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Lastname] [nvarchar](50) NULL,
   [Firstname] [nvarchar](50) NULL,
   [UserName] [nvarchar](50) NULL,
   [EmailAddress] [nvarchar](50) NULL
       DEFAULT ((0))

   ,CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([Id])
)


GO
