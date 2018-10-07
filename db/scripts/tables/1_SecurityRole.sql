CREATE TABLE [dbo].[SecurityRole] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Name] [nvarchar](50) NOT NULL,
   [DisplayName] [nvarchar](50) NULL,
   [SysAdmin] [bit] NOT NULL
       DEFAULT ((0)),
   [System] [bit] NOT NULL
       DEFAULT ((0))

   ,CONSTRAINT [PK_SecurityRole] PRIMARY KEY CLUSTERED ([Id])
)


GO
