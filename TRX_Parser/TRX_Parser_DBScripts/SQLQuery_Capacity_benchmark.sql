USE [AQAresults]
GO

/****** Object:  Table [dbo].[capacity_benchmarks]    Script Date: 7/15/2019 2:15:15 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[capacity_benchmarks](
    [testid] [bigint] Not NULL IDENTITY(1, 1), 
	[testName] [varchar](max) Not NULL,
	[acceptableDurationInMS] [int] NULL,
	PRIMARY KEY (testid)
) 
GO

