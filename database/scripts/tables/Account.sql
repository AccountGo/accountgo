CREATE TABLE [dbo].[Account] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CompanyId] [int] NULL,
   [AccountClassId] [int] NOT NULL,
   [ParentAccountId] [int] NULL,
   [DrOrCrSide] [int] NOT NULL,
   [AccountCode] [nvarchar](50) NOT NULL,
   [AccountName] [nvarchar](200) NOT NULL,
   [Description] [nvarchar](200) NULL,
   [IsCash] [bit] NOT NULL,
   [IsContraAccount] [bit] NOT NULL
       DEFAULT ((0)),
   [RowVersion] [timestamp] NOT NULL

   ,CONSTRAINT [PK_Account] PRIMARY KEY CLUSTERED ([Id])
   ,CONSTRAINT [UQ__Account__38D0C56A397A42BB] UNIQUE NONCLUSTERED ([AccountCode])
)

CREATE NONCLUSTERED INDEX [IX_AccountClassId] ON [dbo].[Account] ([AccountClassId])
CREATE NONCLUSTERED INDEX [IX_ParentAccountId] ON [dbo].[Account] ([ParentAccountId])

GO
