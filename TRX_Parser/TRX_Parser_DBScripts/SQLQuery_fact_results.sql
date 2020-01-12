USE [AQAresults]
GO

/****** Object:  Table [dbo].[fact_results]    Script Date: 8/29/2019 1:47:08 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET ANSI_PADDING ON
GO

CREATE TABLE [dbo].[fact_results](
	[testresultid] [bigint] Not NULL IDENTITY(1, 1),
	[persona] [varchar](255) NULL,
	[scenario] [varchar](255) NULL,
	[workflowName] [varchar](max) NULL,
	[testStatus] [varchar](255) NULL,
	[durationinms] [int] NULL,
	[testSummary] [varchar](max) NULL,
	[fileName] [varchar](max) NULL,
	[resultfileid] [int] FOREIGN KEY REFERENCES results_filename(resultfileid),
	PRIMARY KEY (testresultid)
) 

GO

SET ANSI_PADDING OFF
GO