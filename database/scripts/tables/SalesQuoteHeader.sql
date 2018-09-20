CREATE TABLE [dbo].[SalesQuoteHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CustomerId] [int] NOT NULL,
   [Date] [datetime] NOT NULL,
   [PaymentTermId] [int] NULL,
   [ReferenceNo] [nvarchar](max) NULL,
   [No] [nvarchar](max) NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesQuoteHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[SalesQuoteHeader] ([CustomerId])

GO
