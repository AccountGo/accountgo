CREATE TABLE [dbo].[SalesInvoiceHeader] (
    [Id]                     INT             IDENTITY (1, 1) NOT NULL,
    [CustomerId]             INT             NOT NULL,
    [GeneralLedgerHeaderId]  INT             NULL,
    [No]                     NVARCHAR (MAX)  NULL,
    [Date]                   DATETIME        NOT NULL,
    [ShippingHandlingCharge] DECIMAL (18, 2) NOT NULL,
    [Status]                 INT             NULL,
    [PaymentTermId] INT NULL, 
    [ReferenceNo] NVARCHAR(MAX) NULL, 
    CONSTRAINT [PK_dbo.SalesInvoiceHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]), 
    CONSTRAINT [FK_SalesInvoiceHeader_PaymentTerm] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm]([Id]),
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesInvoiceHeader]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[SalesInvoiceHeader]([GeneralLedgerHeaderId] ASC);

