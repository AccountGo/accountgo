﻿CREATE TABLE [dbo].[SecurityRole] (
	[Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(50) NOT NULL,
	[DisplayName] NVARCHAR(50) NULL,
    [SysAdmin] BIT NOT NULL DEFAULT 0,
	[System] BIT NOT NULL DEFAULT 0, 
	CONSTRAINT [PK_SecurityRole] PRIMARY KEY CLUSTERED ([Id] ASC)    
     
)
