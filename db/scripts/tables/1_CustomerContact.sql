CREATE TABLE [dbo].[CustomerContact] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ContactId] [int] NULL,
   [CustomerId] [int] NULL

   ,CONSTRAINT [PK__Customer__3214EC07671C33E8] PRIMARY KEY CLUSTERED ([Id])
)


GO
