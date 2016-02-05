CREATE TABLE [dbo].[SalesQuoteLine] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [SalesQuoteHeaderId] INT            NOT NULL,
    CONSTRAINT [PK_dbo.SalesQuoteLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.SalesQuoteHeader_SalesQuoteHeaderId] FOREIGN KEY ([SalesQuoteHeaderId]) REFERENCES [dbo].[SalesQuoteHeader] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_SalesQuoteHeaderId]
    ON [dbo].[SalesQuoteLine]([SalesQuoteHeaderId] ASC);

