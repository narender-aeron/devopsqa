USE [AQAresults]
GO

/****** Object:  Table [dbo].[failed_tests]    Script Date: 11/12/2019 9:45:11 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[failed_tests](
	[resultfileid] [int] NULL,
	[testName] [varchar](max) NULL,
	[outcome] [varchar](255) NULL,
	[startTime] [datetime2](7) NULL,
	[endTime] [datetime2](7) NULL,
	[component] [varchar](255) NULL,
	[errorMessage] [varchar](max) NULL,
	[filename] [varchar](255) NULL,
	[resourceName] [varchar](max) NULL,
	[triageComment] [varchar](max) NULL,
	[jiraid] [varchar] (255) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[failed_tests]  WITH CHECK ADD FOREIGN KEY([resultfileid])
REFERENCES [dbo].[results_filename] ([resultfileid])
GO


