USE [AQAresults]
GO

/****** Object:  Table [dbo].[result_fields]    Script Date: 7/9/2019 2:40:23 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[result_fields](
    [testresultid] [bigint] Not NULL IDENTITY(1, 1), 
	[testName] [varchar](max) NULL,
	[outcome] [varchar](255) NULL,
	[testresult] [int] NULL,
	[startTime] [datetime2](7) NULL,
	[endTime] [datetime2](7) NULL,
	[isAborted] [varchar](255) NULL,
	[duration] [int] NULL,
	[className] [varchar](max) NULL,
	[component] [varchar](255) NULL,
	[errorMessage] [varchar](max) NULL,
	[errorStackTrace] [varchar](max) NULL,
	[filename] [varchar](255) NULL,
	[resultfileid] [int] FOREIGN KEY REFERENCES results_filename(resultfileid),
	[executionDate] [datetime2](7) NULL,
	[releaseName] [varchar](255) NULL,
	PRIMARY KEY (testresultid)
) 
GO
