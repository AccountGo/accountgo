CREATE TABLE [dbo].[Party] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [PartyType]  INT            NOT NULL,
    [Name]       NVARCHAR (MAX) NULL,
    [Email]      NVARCHAR (MAX) NULL,
    [Website]    NVARCHAR (MAX) NULL,
    [Phone]      NVARCHAR (MAX) NULL,
    [Fax]        NVARCHAR (MAX) NULL,
    [IsActive]   BIT            NOT NULL,
    CONSTRAINT [PK_dbo.Party] PRIMARY KEY CLUSTERED ([Id] ASC)
);

