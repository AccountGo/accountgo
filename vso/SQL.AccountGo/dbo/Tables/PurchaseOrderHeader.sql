CREATE TABLE [dbo].[PurchaseOrderHeader] (
    [Id]                      INT            IDENTITY (1, 1) NOT NULL,
    [VendorId]                INT            NOT NULL,
    [PurchaseInvoiceHeaderId] INT            NULL,
    [No]                      NVARCHAR (MAX) NULL,
    [Date]                    DATETIME       NOT NULL,
    [Description]             NVARCHAR (MAX) NULL,
    [CreatedBy]               NVARCHAR (MAX) NULL,
    [CreatedOn]               DATETIME       NOT NULL,
    [ModifiedBy]              NVARCHAR (MAX) NULL,
    [ModifiedOn]              DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.PurchaseOrderHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.PurchaseInvoiceHeader_PurchaseInvoiceHeaderId] FOREIGN KEY ([PurchaseInvoiceHeaderId]) REFERENCES [dbo].[PurchaseInvoiceHeader] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.Vendor_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [dbo].[Vendor] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoiceHeaderId]
    ON [dbo].[PurchaseOrderHeader]([PurchaseInvoiceHeaderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_VendorId]
    ON [dbo].[PurchaseOrderHeader]([VendorId] ASC);

