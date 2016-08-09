CREATE TABLE [dbo].[SalesQuoteLine] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [SalesQuoteHeaderId] INT            NOT NULL,
	[ItemId]             INT             NOT NULL,
    [MeasurementId]      INT             NOT NULL,
    [Quantity]           DECIMAL (18, 2) NOT NULL,
    [Discount]           DECIMAL (18, 2) NOT NULL,
    [Amount]             DECIMAL (18, 2) NOT NULL,
    CONSTRAINT [PK_dbo.SalesQuoteLine] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.SalesQuoteHeader_SalesQuoteHeaderId] FOREIGN KEY ([SalesQuoteHeaderId]) REFERENCES [dbo].[SalesQuoteHeader] ([Id]),
	CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Item] ([Id]),
    CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.Measurement_MeasurementId] FOREIGN KEY ([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_SalesQuoteHeaderId]
    ON [dbo].[SalesQuoteLine]([SalesQuoteHeaderId] ASC);

