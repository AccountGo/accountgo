CREATE TABLE [dbo].[AspNetUsers] (
   [Id] [nvarchar](450) NOT NULL,
   [AccessFailedCount] [int] NOT NULL,
   [ConcurrencyStamp] [nvarchar](max) NULL,
   [Email] [nvarchar](256) NULL,
   [EmailConfirmed] [bit] NOT NULL,
   [LockoutEnabled] [bit] NOT NULL,
   [LockoutEnd] [datetimeoffset] NULL,
   [NormalizedEmail] [nvarchar](256) NULL,
   [NormalizedUserName] [nvarchar](256) NULL,
   [PasswordHash] [nvarchar](max) NULL,
   [PhoneNumber] [nvarchar](max) NULL,
   [PhoneNumberConfirmed] [bit] NOT NULL,
   [SecurityStamp] [nvarchar](max) NULL,
   [TwoFactorEnabled] [bit] NOT NULL,
   [UserName] [nvarchar](256) NULL

   ,CONSTRAINT [PK_AspNetUsers] PRIMARY KEY CLUSTERED ([Id])
)

CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex] ON [dbo].[AspNetUsers] ([UserName])

GO
