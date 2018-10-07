CREATE TABLE [dbo].[Log] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [TimeStamp] [datetime2] NULL,
   [Level] [nvarchar](100) NULL,
   [Logger] [nvarchar](max) NULL,
   [Message] [nvarchar](max) NULL,
   [Username] [nvarchar](max) NULL,
   [CallSite] [nvarchar](max) NULL,
   [Thread] [nvarchar](max) NULL,
   [Exception] [nvarchar](max) NULL,
   [StackTrace] [nvarchar](max) NULL

   ,CONSTRAINT [PK_Log] PRIMARY KEY CLUSTERED ([Id])
)


GO
