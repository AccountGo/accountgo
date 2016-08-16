CREATE TABLE [dbo].[PurchaseReceiptHeader] (
    [Id]                    INT            IDENTITY (1, 1) NOT NULL,
    [VendorId]              INT            NOT NULL,
    [GeneralLedgerHeaderId] INT            NULL,
    [Date]                  DATETIME       NOT NULL,
    [No]                    NVARCHAR (MAX) NULL,
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.PurchaseReceiptHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.PurchaseReceiptHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id]),
    CONSTRAINT [FK_dbo.PurchaseReceiptHeader_dbo.Vendor_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [dbo].[Vendor] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[PurchaseReceiptHeader]([GeneralLedgerHeaderId] ASC);
	
GO
CREATE NONCLUSTERED INDEX [IX_VendorId]
    ON [dbo].[PurchaseReceiptHeader]([VendorId] ASC);

