CREATE TABLE [dbo].[CustomerAllocation] (
    [Id]                   INT             IDENTITY (1, 1) NOT NULL,
    [CustomerId]           INT             NOT NULL,
    [SalesInvoiceHeaderId] INT             NOT NULL,
    [SalesReceiptHeaderId] INT             NOT NULL,
    [Date]                 DATETIME        NOT NULL,
    [Amount]               DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.CustomerAllocation] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.CustomerAllocation_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.CustomerAllocation_dbo.SalesInvoiceHeader_SalesInvoiceHeaderId] FOREIGN KEY ([SalesInvoiceHeaderId]) REFERENCES [dbo].[SalesInvoiceHeader] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_dbo.CustomerAllocation_dbo.SalesReceiptHeader_SalesReceiptHeaderId] FOREIGN KEY ([SalesReceiptHeaderId]) REFERENCES [dbo].[SalesReceiptHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[CustomerAllocation]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceHeaderId]
    ON [dbo].[CustomerAllocation]([SalesInvoiceHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesReceiptHeaderId]
    ON [dbo].[CustomerAllocation]([SalesReceiptHeaderId] ASC);

