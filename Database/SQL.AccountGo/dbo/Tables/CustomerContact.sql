CREATE TABLE [dbo].[CustomerContact]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ContactId] INT NULL, 
    [CustomerId] INT NULL, 
    CONSTRAINT [FK_dbo.CustomerContact_dbo.Customer_Id] FOREIGN KEY (CustomerId) REFERENCES [Customer](Id) 
)
