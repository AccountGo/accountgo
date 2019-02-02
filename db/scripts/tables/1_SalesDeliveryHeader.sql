CREATE TABLE [dbo].[SalesDeliveryHeader] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PaymentTermId] [int] NULL,
   [CustomerId] [int] NULL,
   [GeneralLedgerHeaderId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [Date] [datetime] NOT NULL,
   [Status] [int] NULL

   ,CONSTRAINT [PK_dbo.SalesDeliveryHeader] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_CustomerId] ON [dbo].[SalesDeliveryHeader] ([CustomerId])
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId] ON [dbo].[SalesDeliveryHeader] ([GeneralLedgerHeaderId])
CREATE NONCLUSTERED INDEX [IX_PaymentTermId] ON [dbo].[SalesDeliveryHeader] ([PaymentTermId])

GO
