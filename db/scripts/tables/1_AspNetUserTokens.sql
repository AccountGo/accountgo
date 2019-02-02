CREATE TABLE [dbo].[AspNetUserTokens] (
   [UserId] [nvarchar](450) NOT NULL,
   [LoginProvider] [nvarchar](450) NOT NULL,
   [Name] [nvarchar](450) NOT NULL,
   [Value] [nvarchar](max) NULL

   ,CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY CLUSTERED ([UserId], [LoginProvider], [Name])
)


GO
