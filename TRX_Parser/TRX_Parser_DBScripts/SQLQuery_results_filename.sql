
USE [AQAresults]
GO

/****** Object:  Table [dbo].[results_filename]    Script Date: 7/11/2019 5:00:22 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[results_filename](
	[resultfileid] [int] IDENTITY(1,1) NOT NULL,
	[fileName]  [varchar](255) NULL ,
	Primary Key (resultfileid)
) 
GO