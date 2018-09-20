ALTER TABLE [dbo].[SalesQuoteLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.Item_ItemId]
   FOREIGN KEY([ItemId]) REFERENCES [dbo].[Item] ([Id])

GO
ALTER TABLE [dbo].[SalesQuoteLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.Measurement_MeasurementId]
   FOREIGN KEY([MeasurementId]) REFERENCES [dbo].[Measurement] ([Id])
   ON DELETE CASCADE

GO
ALTER TABLE [dbo].[SalesQuoteLine] WITH CHECK ADD CONSTRAINT [FK_dbo.SalesQuoteLine_dbo.SalesQuoteHeader_SalesQuoteHeaderId]
   FOREIGN KEY([SalesQuoteHeaderId]) REFERENCES [dbo].[SalesQuoteHeader] ([Id])

GO
