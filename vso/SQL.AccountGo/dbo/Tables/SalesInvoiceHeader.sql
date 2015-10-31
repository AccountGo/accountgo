CREATE TABLE [dbo].[SalesInvoiceHeader] (
    [Id]                     INT             IDENTITY (1, 1) NOT NULL,
    [CustomerId]             INT             NOT NULL,
    [GeneralLedgerHeaderId]  INT             NULL,
    [SalesDeliveryHeaderId]  INT             NULL,
    [No]                     NVARCHAR (MAX)  NULL,
    [Date]                   DATETIME        NOT NULL,
    [ShippingHandlingCharge] DECIMAL (18, 2) NOT NULL,
    [Status]                 INT             NOT NULL,
    [CreatedBy]              NVARCHAR (MAX)  NULL,
    [CreatedOn]              DATETIME        NOT NULL,
    [ModifiedBy]             NVARCHAR (MAX)  NULL,
    [ModifiedOn]             DATETIME        NOT NULL,
    CONSTRAINT [PK_dbo.SalesInvoiceHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.SalesInvoiceHeader_dbo.SalesDeliveryHeader_SalesDeliveryHeaderId] FOREIGN KEY ([SalesDeliveryHeaderId]) REFERENCES [dbo].[SalesDeliveryHeader] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesInvoiceHeader]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[SalesInvoiceHeader]([GeneralLedgerHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SalesDeliveryHeaderId]
    ON [dbo].[SalesInvoiceHeader]([SalesDeliveryHeaderId] ASC);

