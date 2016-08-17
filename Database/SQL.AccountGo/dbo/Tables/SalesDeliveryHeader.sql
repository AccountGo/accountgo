CREATE TABLE [dbo].[SalesDeliveryHeader] (
    [Id]                    INT            IDENTITY (1, 1) NOT NULL,
    [PaymentTermId]         INT            NULL,
    [CustomerId]            INT            NULL,
    [GeneralLedgerHeaderId] INT            NULL,
    [No]                    NVARCHAR (MAX) NULL,
    [Date]                  DATETIME       NOT NULL,
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.SalesDeliveryHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.SalesDeliveryHeader_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id]),
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesDeliveryHeader]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[SalesDeliveryHeader]([GeneralLedgerHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PaymentTermId]
    ON [dbo].[SalesDeliveryHeader]([PaymentTermId] ASC);

