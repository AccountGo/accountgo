CREATE TABLE [dbo].[Customer] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [PartyId] [int] NULL,
   [No] [nvarchar](max) NULL,
   [PrimaryContactId] [int] NULL,
   [TaxGroupId] [int] NULL,
   [AccountsReceivableAccountId] [int] NULL,
   [SalesAccountId] [int] NULL,
   [SalesDiscountAccountId] [int] NULL,
   [CustomerAdvancesAccountId] [int] NULL,
   [PromptPaymentDiscountAccountId] [int] NULL,
   [PaymentTermId] [int] NULL

   ,CONSTRAINT [PK_dbo.Customer] PRIMARY KEY CLUSTERED ([Id])
)

CREATE NONCLUSTERED INDEX [IX_AccountsReceivableAccountId] ON [dbo].[Customer] ([AccountsReceivableAccountId])
CREATE NONCLUSTERED INDEX [IX_Id] ON [dbo].[Customer] ([Id])
CREATE NONCLUSTERED INDEX [IX_PaymentTermId] ON [dbo].[Customer] ([PaymentTermId])
CREATE NONCLUSTERED INDEX [IX_PrimaryContactId] ON [dbo].[Customer] ([PrimaryContactId])
CREATE NONCLUSTERED INDEX [IX_PromptPaymentDiscountAccountId] ON [dbo].[Customer] ([PromptPaymentDiscountAccountId])
CREATE NONCLUSTERED INDEX [IX_SalesAccountId] ON [dbo].[Customer] ([SalesAccountId])
CREATE NONCLUSTERED INDEX [IX_SalesDiscountAccountId] ON [dbo].[Customer] ([SalesDiscountAccountId])
CREATE NONCLUSTERED INDEX [IX_TaxGroupId] ON [dbo].[Customer] ([TaxGroupId])

GO
