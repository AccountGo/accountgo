CREATE TABLE [dbo].[ItemTaxGroup] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Name] [nvarchar](max) NULL,
   [IsFullyExempt] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.ItemTaxGroup] PRIMARY KEY CLUSTERED ([Id])
)


GO
