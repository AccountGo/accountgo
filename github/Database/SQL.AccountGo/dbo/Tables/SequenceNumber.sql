CREATE TABLE [dbo].[SequenceNumber] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [SequenceNumberType] INT            NOT NULL,
    [Description]        NVARCHAR (MAX) NULL,
    [Prefix]             NVARCHAR (MAX) NULL,
    [NextNumber]         INT            NOT NULL,
    [UsePrefix]          BIT            NOT NULL,
    CONSTRAINT [PK_dbo.SequenceNumber] PRIMARY KEY CLUSTERED ([Id] ASC)
);

