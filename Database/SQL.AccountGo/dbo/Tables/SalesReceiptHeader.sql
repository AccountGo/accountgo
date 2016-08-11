CREATE TABLE [dbo].[SalesReceiptHeader] (
    [Id]                    INT             IDENTITY (1, 1) NOT NULL,
    [CustomerId]            INT             NOT NULL,
    [GeneralLedgerHeaderId] INT             NULL,
    [AccountToDebitId]      INT             NULL,
    [No]                    NVARCHAR (MAX)  NULL,
    [Date]                  DATETIME        NOT NULL,
    [Amount]                DECIMAL (18, 2) NOT NULL,
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.SalesReceiptHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.Account_AccountToDebitId] FOREIGN KEY ([AccountToDebitId]) REFERENCES [dbo].[Account] ([Id]),
    CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesReceiptHeader_dbo.GeneralLedgerHeader_GeneralLedgerHeaderId] FOREIGN KEY ([GeneralLedgerHeaderId]) REFERENCES [dbo].[GeneralLedgerHeader] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_AccountToDebitId]
    ON [dbo].[SalesReceiptHeader]([AccountToDebitId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesReceiptHeader]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_GeneralLedgerHeaderId]
    ON [dbo].[SalesReceiptHeader]([GeneralLedgerHeaderId] ASC);

