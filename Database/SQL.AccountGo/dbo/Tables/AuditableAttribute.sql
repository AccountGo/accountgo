CREATE TABLE [AuditableAttribute](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AuditableEntityId] [int] NOT NULL,
	[AttributeName] [nvarchar](50) NOT NULL,
    [EnableAudit] BIT NOT NULL DEFAULT 1, 
    CONSTRAINT [PK_AuditableAttribute] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY], 
    CONSTRAINT [FK_AuditableAttribute_AuditableEntity] FOREIGN KEY ([AuditableEntityId]) REFERENCES [AuditableEntity]([Id])
) ON [PRIMARY]
