CREATE TABLE [dbo].[PurchaseInvoiceHeader] (
    [Id]                    INT            IDENTITY (1, 1) NOT NULL,
    [VendorId]              INT            NULL,
    [GeneralLedgerHeaderId] INT            NULL,
    [Date]                  DATETIME       NOT NULL,
    [No]                    NVARCHAR (MAX) NULL,
    [VendorInvoiceNo]       NVARCHAR (MAX) NOT NULL,
    [Description]           NVARCHAR (MAX) NULL,
    [PaymentTermId] INT NULL, 
    [ReferenceNo] NVARCHAR(MAX) NULL, 
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.PurchaseInvoiceHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.Vendor_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [dbo].[Vendor] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseInvoiceHeader_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm]([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[PurchaseInvoiceHeader]([GeneralLedgerHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_VendorId]
    ON [dbo].[PurchaseInvoiceHeader]([VendorId] ASC);

