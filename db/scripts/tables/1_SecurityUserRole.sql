CREATE TABLE [dbo].[SecurityUserRole] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SecurityRoleId] [int] NULL,
   [UserId] [int] NULL

   ,CONSTRAINT [PK_SecurityUserRole] PRIMARY KEY CLUSTERED ([Id])
)


GO
