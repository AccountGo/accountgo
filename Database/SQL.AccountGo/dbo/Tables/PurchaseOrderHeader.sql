CREATE TABLE [dbo].[PurchaseOrderHeader] (
    [Id]                      INT            IDENTITY (1, 1) NOT NULL,
    [VendorId]                INT            NOT NULL,
    [No]                      NVARCHAR (MAX) NULL,
    [Date]                    DATETIME       NOT NULL,
    [Description]             NVARCHAR (MAX) NULL,
    [PurchaseInvoiceHeaderId] INT NULL, 
    [PaymentTermId] INT NULL, 
    [ReferenceNo] NVARCHAR(MAX) NULL, 
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.PurchaseOrderHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.Vendor_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [dbo].[Vendor] ([Id]), 
    CONSTRAINT [FK_dbo.PurchaseOrderHeader_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm]([Id])
);


GO
