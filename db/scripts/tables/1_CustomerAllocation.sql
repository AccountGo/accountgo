CREATE TABLE [dbo].[CustomerAllocation] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [CustomerId] [int] NOT NULL,
   [SalesInvoiceHeaderId] [int] NOT NULL,
   [SalesReceiptHeaderId] [int] NOT NULL,
   [Date] [datetime] NOT NULL,
   [Amount] [decimal](18,2) NOT NULL

   ,CONSTRAINT [PK_dbo.CustomerAllocation] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[CustomerAllocation] ([CustomerId])
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceHeaderId] ON [dbo].[CustomerAllocation] ([SalesInvoiceHeaderId])
CREATE NONCLUSTERED INDEX [IX_SalesReceiptHeaderId] ON [dbo].[CustomerAllocation] ([SalesReceiptHeaderId])

GO
