CREATE TABLE [dbo].[PaymentTerm] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [Description] [nvarchar](max) NULL,
   [PaymentType] [int] NOT NULL,
   [DueAfterDays] [int] NULL,
   [IsActive] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.PaymentTerm] PRIMARY KEY CLUSTERED ([Id])
)


GO
