CREATE TABLE [dbo].[AspNetUserLogins] (
   [LoginProvider] [nvarchar](450) NOT NULL,
   [ProviderKey] [nvarchar](450) NOT NULL,
   [ProviderDisplayName] [nvarchar](max) NULL,
   [UserId] [nvarchar](450) NOT NULL

   ,CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY CLUSTERED ([LoginProvider], [ProviderKey])
)


GO
