CREATE TABLE [dbo].[AspNetRoleClaims] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ClaimType] [nvarchar](max) NULL,
   [ClaimValue] [nvarchar](max) NULL,
   [RoleId] [nvarchar](450) NOT NULL

   ,CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY CLUSTERED ([Id])
)


GO
