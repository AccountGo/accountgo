CREATE TABLE [dbo].[AspNetUserRoles] (
   [UserId] [nvarchar](450) NOT NULL,
   [RoleId] [nvarchar](450) NOT NULL

   ,CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY CLUSTERED ([UserId], [RoleId])
)


GO
