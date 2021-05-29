USE [AQAresults]
GO

/****** Object:  Table [dbo].[result_perf_Json]    Script Date: 4/16/2019 1:47:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET ANSI_PADDING ON
GO

CREATE TABLE [dbo].[perf_results](
    [testresultid] [bigint] Not NULL IDENTITY(1, 1), 
	[testName] [varchar](max) NULL,
	[testSummary] [varchar](max) NULL,
	[durationinms] [int] NULL,
	[testStatus] [varchar](255) NULL,
	[fileName] [varchar](max) NULL,
	[resultfileid] [int] FOREIGN KEY REFERENCES results_filename(resultfileid),
	PRIMARY KEY (testresultid)
) 

GO

SET ANSI_PADDING OFF
GO
