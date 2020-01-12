USE [AQAresults]
GO

/****** Object:  Table [dbo].[ComponentTeamMap]    Script Date: 9/11/2019 3:17:48 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[ComponentTeamMap](
       [component] [varchar](255) NOT NULL,
       [team] [varchar] (255) FOREIGN KEY REFERENCES teams(team)      
) 
GO
