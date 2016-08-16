CREATE TABLE [dbo].[SalesQuoteHeader] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [CustomerId] INT            NOT NULL,
    [Date]       DATETIME       NOT NULL,
    [PaymentTermId] INT NULL, 
    [ReferenceNo] NVARCHAR(MAX) NULL, 
    [No] NVARCHAR(MAX) NULL, 
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.SalesQuoteHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesQuoteHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesQuoteHeader_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm]([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesQuoteHeader]([CustomerId] ASC);

