CREATE TABLE [dbo].[AspNetRoles] (
   [Id] [nvarchar](450) NOT NULL,
   [ConcurrencyStamp] [nvarchar](max) NULL,
   [Name] [nvarchar](256) NULL,
   [NormalizedName] [nvarchar](256) NULL

   ,CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id])
)

CREATE UNIQUE NONCLUSTERED INDEX [RoleNameIndex] ON [dbo].[AspNetRoles] ([Name])

GO
