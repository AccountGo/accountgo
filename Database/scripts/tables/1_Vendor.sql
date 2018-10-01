CREATE TABLE [dbo].[Vendor] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PartyId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [AccountsPayableAccountId] [int] NULL,
   [PurchaseAccountId] [int] NULL,
   [PurchaseDiscountAccountId] [int] NULL,
   [PrimaryContactId] [int] NULL,
   [PaymentTermId] [int] NULL,
   [TaxGroupId] [int] NULL

   ,CONSTRAINT [PK_dbo.Vendor] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountsPayableAccountId] ON [dbo].[Vendor] ([AccountsPayableAccountId])
CREATE NONCLUSTERED INDEX [IX_Id] ON [dbo].[Vendor] ([Id])
CREATE NONCLUSTERED INDEX [IX_PaymentTermId] ON [dbo].[Vendor] ([PaymentTermId])
CREATE NONCLUSTERED INDEX [IX_PrimaryContactId] ON [dbo].[Vendor] ([PrimaryContactId])
CREATE NONCLUSTERED INDEX [IX_PurchaseAccountId] ON [dbo].[Vendor] ([PurchaseAccountId])
CREATE NONCLUSTERED INDEX [IX_PurchaseDiscountAccountId] ON [dbo].[Vendor] ([PurchaseDiscountAccountId])

GO
