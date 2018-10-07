CREATE TABLE [dbo].[SalesQuoteLine] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SalesQuoteHeaderId] [int] NOT NULL,
   [ItemId] [int] NOT NULL,
   [MeasurementId] [int] NOT NULL,
   [Quantity] [decimal](18,2) NOT NULL,
   [Discount] [decimal](18,2) NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.SalesQuoteLine] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_SalesQuoteHeaderId] ON [dbo].[SalesQuoteLine] ([SalesQuoteHeaderId])

GO
