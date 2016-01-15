CREATE TABLE [dbo].[MainContraAccount]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [MainAccountId] INT NOT NULL, 
    [RelatedContraAccountId] INT NOT NULL, 
    CONSTRAINT [FK_MainContraAccount_MainAccountId_Account_AccountId] FOREIGN KEY ([MainAccountId]) REFERENCES [Account]([Id]),
	CONSTRAINT [FK_MainContraAccount_RelatedContraAccountId_Account_AccountId] FOREIGN KEY ([RelatedContraAccountId]) REFERENCES [Account]([Id])
)
