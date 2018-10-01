CREATE TABLE [dbo].[Party] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PartyType] [int] NOT NULL,
   [Name] [nvarchar](max) NULL,
   [Email] [nvarchar](max) NULL,
   [Website] [nvarchar](max) NULL,
   [Phone] [nvarchar](max) NULL,
   [Fax] [nvarchar](max) NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.Party] PRIMARY KEY CLUSTERED ([Id])
)


GO
