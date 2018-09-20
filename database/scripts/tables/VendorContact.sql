CREATE TABLE [dbo].[VendorContact] (
   [Id] [int] NOT NULL
      IDENTITY (1,1),
   [ContactId] [int] NULL,
   [VendorId] [int] NULL

   ,CONSTRAINT [PK__VendorCo__3214EC0788E4511D] PRIMARY KEY CLUSTERED ([Id])
)


GO
