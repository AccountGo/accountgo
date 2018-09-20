CREATE TABLE [dbo].[TaxGroup] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Description] [nvarchar](max) NULL,
   [TaxAppliedToShipping] [bit] NOT NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.TaxGroup] PRIMARY KEY CLUSTERED ([Id])
)


GO
