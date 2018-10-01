CREATE TABLE [dbo].[SalesOrderHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CustomerId] [int] NULL,
   [PaymentTermId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [ReferenceNo] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesOrderHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[SalesOrderHeader] ([CustomerId])
CREATE NONCLUSTERED INDEX [IX_PaymentTermId] ON [dbo].[SalesOrderHeader] ([PaymentTermId])

GO
