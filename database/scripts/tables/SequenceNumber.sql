CREATE TABLE [dbo].[SequenceNumber] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [SequenceNumberType] [int] NOT NULL,
   [Description] [nvarchar](max) NULL,
   [Prefix] [nvarchar](max) NULL,
   [NextNumber] [int] NOT NULL,
   [UsePrefix] [bit] NOT NULL

   ,CONSTRAINT [PK_dbo.SequenceNumber] PRIMARY KEY CLUSTERED ([Id])
)


GO
