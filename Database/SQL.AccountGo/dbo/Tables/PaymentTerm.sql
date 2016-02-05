CREATE TABLE [dbo].[PaymentTerm] (
    [Id]           INT            IDENTITY (1, 1) NOT NULL,
    [Description]  NVARCHAR (MAX) NULL,
    [PaymentType]  INT            NOT NULL,
    [DueAfterDays] INT            NULL,
    [IsActive]     BIT            NOT NULL,
    CONSTRAINT [PK_dbo.PaymentTerm] PRIMARY KEY CLUSTERED ([Id] ASC)
);

