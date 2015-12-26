CREATE TABLE [AuditLog](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Username] [nvarchar](50) NOT NULL,
	[AuditEventDateUTC] [datetime2](7) NOT NULL,
	[AuditEventType] [int] NOT NULL,
	[TableName] [nvarchar](100) NOT NULL,
	[RecordId] [nvarchar](100) NOT NULL,
	[FieldName] [nvarchar](100) NOT NULL,
	[OriginalValue] [nvarchar](max) NULL,
	[NewValue] [nvarchar](max) NULL,
 CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
