CREATE TABLE [dbo].[SalesReceiptLine] (
    [Id]                   INT             IDENTITY (1, 1) NOT NULL,
    [SalesReceiptHeaderId] INT             NOT NULL,
    [SalesInvoiceLineId]   INT             NULL,
    [ItemId]               INT             NULL,
    [AccountToCreditId]    INT             NULL,
    [MeasurementId]        INT             NULL,
    [Quantity]             DECIMAL (18, 2) NULL,
    [Discount]             DECIMAL (18, 2) NULL,
    [Amount]               DECIMAL (18, 2) NULL,
    [AmountPaid]           DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.SalesReceiptLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.Account_AccountToCreditId] FOREIGN KEY ([AccountToCreditId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.SalesInvoiceLine_SalesInvoiceLineId] FOREIGN KEY ([SalesInvoiceLineId]) REFERENCES [dbo].[SalesInvoiceLine] ([Id]),
    CONSTRAINT [FK_dbo.SalesReceiptLine_dbo.SalesReceiptHeader_SalesReceiptHeaderId] FOREIGN KEY ([SalesReceiptHeaderId]) REFERENCES [dbo].[SalesReceiptHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountToCreditId]
    ON [dbo].[SalesReceiptLine]([AccountToCreditId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceLineId]
    ON [dbo].[SalesReceiptLine]([SalesInvoiceLineId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesReceiptHeaderId]
    ON [dbo].[SalesReceiptLine]([SalesReceiptHeaderId] ASC);

