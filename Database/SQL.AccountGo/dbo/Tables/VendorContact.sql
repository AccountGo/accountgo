CREATE TABLE [dbo].[VendorContact]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ContactId] INT NULL, 
    [VendorId] INT NULL, 
	    CONSTRAINT [FK_dbo.VendorContact_dbo.Vendor_Id] FOREIGN KEY (VendorId) REFERENCES [dbo].[Account] ([Id])
 
)
