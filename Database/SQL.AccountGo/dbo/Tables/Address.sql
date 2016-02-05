CREATE TABLE [dbo].[Address] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
	[No]		 NVARCHAR(10)	NULL,
	[Street]     NVARCHAR(255)	NULL,
	[City]		 NVARCHAR(255)	NULL,
    CONSTRAINT [PK_dbo.Address] PRIMARY KEY CLUSTERED ([Id] ASC)
);

