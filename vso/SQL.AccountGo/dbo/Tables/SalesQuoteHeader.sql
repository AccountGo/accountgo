CREATE TABLE [dbo].[SalesQuoteHeader] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [CustomerId] INT            NOT NULL,
    [Date]       DATETIME       NOT NULL,
    [CreatedBy]  NVARCHAR (MAX) NULL,
    [CreatedOn]  DATETIME       NOT NULL,
    [ModifiedBy] NVARCHAR (MAX) NULL,
    [ModifiedOn] DATETIME       NOT NULL,
    CONSTRAINT [PK_dbo.SalesQuoteHeader] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesQuoteHeader_dbo.Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerId]
    ON [dbo].[SalesQuoteHeader]([CustomerId] ASC);

