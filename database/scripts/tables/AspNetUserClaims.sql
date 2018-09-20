CREATE TABLE [dbo].[AspNetUserClaims] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ClaimType] [nvarchar](max) NULL,
   [ClaimValue] [nvarchar](max) NULL,
   [UserId] [nvarchar](450) NOT NULL

   ,CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY CLUSTERED ([Id])
)


GO
