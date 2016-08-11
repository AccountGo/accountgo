CREATE TABLE [dbo].[SalesOrderHeader] (
    [Id]            INT            IDENTITY (1, 1) NOT NULL,
    [CustomerId]    INT            NULL,
    [PaymentTermId] INT            NULL,
    [No]            NVARCHAR (MAX) NULL,
    [ReferenceNo]   NVARCHAR (MAX) NULL,
    [Date]          DATETIME       NOT NULL,
    [Status] INT NULL, 
    CONSTRAINT [PK_dbo.SalesOrderHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesOrderHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
    CONSTRAINT [FK_dbo.SalesOrderHeader_dbo.PaymentTerm_PaymentTermId] FOREIGN KEY ([PaymentTermId]) REFERENCES [dbo].[PaymentTerm] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesOrderHeader]([CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PaymentTermId]
    ON [dbo].[SalesOrderHeader]([PaymentTermId] ASC);

