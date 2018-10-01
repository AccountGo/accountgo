CREATE TABLE [dbo].[MainContraAccount] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [MainAccountId] [int] NOT NULL,
   [RelatedContraAccountId] [int] NOT NULL

   ,CONSTRAINT [PK__MainCont__3214EC078F29005D] PRIMARY KEY CLUSTERED ([Id])
)


GO
