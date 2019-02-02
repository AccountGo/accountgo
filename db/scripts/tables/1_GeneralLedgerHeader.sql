CREATE TABLE [dbo].[GeneralLedgerHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Date] [datetime] NOT NULL,
   [DocumentType] [int] NOT NULL,
   [Description] [nvarchar](max) NULL

   ,CONSTRAINT [PK_dbo.GeneralLedgerHeader] PRIMARY KEY CLUSTERED ([Id])
)


GO
