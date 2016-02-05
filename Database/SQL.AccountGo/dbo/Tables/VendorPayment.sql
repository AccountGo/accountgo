CREATE TABLE [dbo].[VendorPayment] (
    [Id]                      INT             IDENTITY (1, 1) NOT NULL,
    [VendorId]                INT             NOT NULL,
    [PurchaseInvoiceHeaderId] INT             NULL,
    [GeneralLedgerHeaderId]   INT             NULL,
    [No]                      NVARCHAR (MAX)  NULL,
    [Date]                    DATETIME        NOT NULL,
    [Amount]                  DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.VendorPayment] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.VendorPayment_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.VendorPayment_dbo.PurchaseInvoiceHeader_PurchaseInvoiceHeaderId] FOREIGN KEY ([PurchaseInvoiceHeaderId]) REFERENCES [dbo].[PurchaseInvoiceHeader] ([Id]),
    CONSTRAINT [FK_dbo.VendorPayment_dbo.Vendor_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [dbo].[Vendor] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[VendorPayment]([GeneralLedgerHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoiceHeaderId]
    ON [dbo].[VendorPayment]([PurchaseInvoiceHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_VendorId]
    ON [dbo].[VendorPayment]([VendorId] ASC);

