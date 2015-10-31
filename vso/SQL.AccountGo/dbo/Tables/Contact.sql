CREATE TABLE [dbo].[Contact] (
    [Id]          INT            NOT NULL,
    [ContactType] INT            NOT NULL,
    [PartyId]     INT            NULL,
    [FirstName]   NVARCHAR (MAX) NULL,
    [LastName]    NVARCHAR (MAX) NULL,
    [MiddleName]  NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_dbo.Contact] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.Contact_dbo.Party_Id] FOREIGN KEY ([Id]) REFERENCES [dbo].[Party] ([Id]),
    CONSTRAINT [FK_dbo.Contact_dbo.Party_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Party] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_Id]
    ON [dbo].[Contact]([Id] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PartyId]
    ON [dbo].[Contact]([PartyId] ASC);

