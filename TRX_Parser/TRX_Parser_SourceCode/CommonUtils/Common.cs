using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Xml;
using Newtonsoft.Json;


namespace CommonUtils
{
	public enum PlatformType { Unknown, OMS, EMS, PMA }
	public enum EmsTable { Unknown, LatencyWithParameters, AccountBook }
	public enum TestResult { Passed, Failed, Skipped }
	/// <summary>
	/// This class will be used to generate common functions to be used across solution in other projects
	/// </summary>
	public class Common
    {
		Dictionary<string, PlatformType> fileNameKeyToPlatform = new Dictionary<string, PlatformType>();
		Dictionary<string, EmsTable> filePatternToTable = new Dictionary<string, EmsTable>();

		public Common()
		{
            fileNameKeyToPlatform.Add("MainResults", PlatformType.OMS);
            fileNameKeyToPlatform.Add("RerunResults", PlatformType.OMS);
            fileNameKeyToPlatform.Add("Build", PlatformType.OMS);
			fileNameKeyToPlatform.Add("Fact", PlatformType.OMS);
			fileNameKeyToPlatform.Add("Pact", PlatformType.OMS);
			fileNameKeyToPlatform.Add("Ppt", PlatformType.OMS);
			fileNameKeyToPlatform.Add("msixy", PlatformType.EMS);
			fileNameKeyToPlatform.Add("pixy", PlatformType.EMS);
			fileNameKeyToPlatform.Add("AccountBook", PlatformType.EMS);

			filePatternToTable.Add("_OutboundLatency_", EmsTable.LatencyWithParameters);
			filePatternToTable.Add("_RoundTripLatency_", EmsTable.LatencyWithParameters);
			filePatternToTable.Add("_InboundLatency_", EmsTable.LatencyWithParameters);
			filePatternToTable.Add("_AutomatedTradingLatency_", EmsTable.LatencyWithParameters);
			filePatternToTable.Add("AccountBook", EmsTable.AccountBook);
		}

	    // This function is being used to extract the dir location where trx files are being stored and will be used from for parsing
	    public string GetDirInfo(string filename)
	    {
		    string dirinfo = " ";
		    XmlDocument doc = new XmlDocument();
		    doc.Load(filename);

		    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
		    {
			    if (node.Name.Equals("Dirinfo"))
			    {
				    foreach (XmlNode node1ChildNode in node.ChildNodes)
				    {
					    dirinfo = node1ChildNode.InnerText;
						 break;
				    }
			    }
		    }
		    return dirinfo;
	    }

	    // This function is being used to extract the dir location where Json files are being stored and will be used from for parsing
	    public string GetJsonDirInfo(string filename)
	    {
		    string dirinfo = " ";
		    XmlDocument doc = new XmlDocument();
		    doc.Load(filename);

		    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
		    {
			    if (node.Name.Equals("Jsonfiledir"))
			    {
				    foreach (XmlNode node1ChildNode in node.ChildNodes)
				    {
					    dirinfo = node1ChildNode.InnerText;
				    }
			    }
		    }
		    return dirinfo;
	    }

		public string GetXmlDirectoryInfo(string paramsFile)
		{
			return GetDirectoryInfo(paramsFile, "XmlDirInfo");
		}

		private string GetDirectoryInfo(string paramsFile, string nodeName)
		{
			string dirinfo = " ";
			XmlDocument doc = new XmlDocument();
			doc.Load(paramsFile);

			foreach (XmlNode node in doc.DocumentElement.ChildNodes)
			{
				if (node.Name.ToUpper().Equals(nodeName.ToUpper()))
				{
					foreach (XmlNode node1ChildNode in node.ChildNodes)
					{
						dirinfo = node1ChildNode.InnerText;
					}
				}
			}
			return dirinfo;
		}
	   
	    //These functions are being used to extract the requested parameter out of xml file for specific categories
	    public string DbParam(string filename, string requestedparam)
	    {
			const string category = "DB";
			return GetParam(filename, requestedparam, category);
	    }

       //These functions are being used to extract the requested parameter out of xml file for Jira category
       public string JiraParam(string fileName, string requestedParam)
       {
          const string category = "Jira";
          return GetParam(fileName, requestedParam, category);
       }

      // This function will return the jira status for a jiraid
      public string getJiraStatus(string jiraId, string jiraUsername, string jiraPassword)
      {
         string jiraStatus = "";
         System.Net.WebClient wc = new System.Net.WebClient();

         // Create Jira Credentials for Rest Api        
         string mergedCredentials = string.Format("{0}:{1}", jiraUsername, jiraPassword);
         byte[] byteCredentials = System.Text.UTF8Encoding.UTF8.GetBytes(mergedCredentials);
         wc.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(byteCredentials));

         // Create request for Jira rest api
         string request = "https://jira.ezesoft.net/rest/api/2/issue/" + jiraId + "?fields=status";
         string tasks = wc.DownloadString(request);
         dynamic dynObj = JsonConvert.DeserializeObject(tasks);
         foreach (var data in dynObj)
         {
            if (data.Name == "fields")
            {
               foreach (var data1 in data)
               {
                  jiraStatus = (string)data1["status"]["name"];
               }
            }
         }
         return jiraStatus;
      }

		public string GetParam(string filename, string requestedparam, string category)
		{
			string Param = " ";
			XmlDocument doc = new XmlDocument();
			doc.Load(filename);
			foreach (XmlNode node in doc.DocumentElement.ChildNodes)
			{
				if (node.Name.Equals(category))
				{
					foreach (XmlNode node1ChildNode in node.ChildNodes)
					{
						if (node1ChildNode.Name.Equals(requestedparam))
						{
							Param = node1ChildNode.InnerText;
						}
					}
				}
			}
			return Param;
		}

		//This function is being used to create DB connection 
		public SqlConnection GetDbSqlConnection(string datasource, string database, string username, string password)
	    {
		    string connString =String.Format(@"Data Source={0};Initial Catalog={1};Persist Security Info=True;User ID={2};Password={3}", datasource, database, username, password);
		    SqlConnection conn = new SqlConnection(connString);
		    return conn;
	    }

		/// <summary>
		/// This function determines the platform based on the start of the file, this assumes files are split by underscore (_).
		/// </summary>
		/// <param name="fileName"></param>
		public PlatformType DeterminePlatformFromFileName(string fileName)
		{
			var startOfFileName = fileName.Split('_')[0];

			foreach (var pair in fileNameKeyToPlatform)
			{
				if (startOfFileName.ToUpper().Contains(pair.Key.ToUpper()))
				{
					return pair.Value;
				}
			}

			return PlatformType.Unknown;
		}

		/// <summary>
		/// This function determines how many records exist in the result_fields table for the json file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="platform"></param>
		public int GetFileCount(SqlConnection sqlc, string fileName, PlatformType platform)
		{
			//Initialize SQL command for getting results counts from the result_fields table
			SqlCommand getFileNameCount = new SqlCommand();
			string cmdGetFileName = "";

			if (platform == PlatformType.OMS)
			{
				cmdGetFileName = "Select count(*) from results_filename where fileName = @fileName";
			}
			else if (platform == PlatformType.EMS)
			{
				cmdGetFileName = "Select count(*) from ems_results_filename where FileName = @fileName";
			}
			getFileNameCount.Connection = sqlc;
			getFileNameCount.CommandText = cmdGetFileName;
			getFileNameCount.Parameters.AddWithValue("@fileName", fileName);
			Int32 fileCount = (Int32)getFileNameCount.ExecuteScalar();

			return fileCount;
		}

		/// <summary>
		/// This function inserts the file into the results_filename or ems_results_filename table with execution date.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="fileName"></param>
		/// <param name="fileExecutionDate"></param>
		/// <param name="platform"></param>
		public void InsertFile(SqlConnection sqlc, string fileName, DateTime fileExecutionDate, PlatformType platform)
		{
			// Initialize the SQL command for inserting Json file into the results_filename table
			SqlCommand insertFile = new SqlCommand();
			string cmdInsertFile = "";
			if (platform == PlatformType.OMS)
			{
				cmdInsertFile = "INSERT INTO results_filename (fileName, ExecutionDate) Values (@fileName,@ExecutionDate)";
			}
			else if (platform == PlatformType.EMS)
			{
				cmdInsertFile = "INSERT INTO ems_results_filename (fileName, ExecutionDate) Values (@fileName,@ExecutionDate)";
			}
			insertFile.Connection = sqlc;
			insertFile.CommandText = cmdInsertFile;
			insertFile.Parameters.AddWithValue("@fileName", fileName);
			insertFile.Parameters.AddWithValue("@ExecutionDate", fileExecutionDate);
			insertFile.ExecuteNonQuery();
		}

		/// <summary>
		/// This function returns the resultFileId (primary key) for a json file in the results_filename table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="platform"></param>
		public int GetResultFileId(SqlConnection sqlc, string fileName, PlatformType platform)
		{
			SqlCommand getResultFileId = new SqlCommand();
			string cmdGetResultFileid = "";

			if (platform == PlatformType.OMS)
			{
				cmdGetResultFileid = "Select resultFileId from results_filename where filename = @fileName";
			}
			else if (platform == PlatformType.EMS)
			{
				cmdGetResultFileid = "Select resultfileid from ems_results_filename where fileName = @fileName";
			}

			getResultFileId.Connection = sqlc;
			getResultFileId.CommandText = cmdGetResultFileid;
			getResultFileId.Parameters.AddWithValue("@fileName", fileName);
			Int32 resultFileId = (Int32)getResultFileId.ExecuteScalar();

			return resultFileId;
		}

		/// <summary>
		/// This function determines what table the fileName has results for. The fileName includes the type of test.
		/// </summary>
		/// <param name="fileName"></param>
		public EmsTable GetTableOfInterest(string fileName)
		{
			foreach (var pair in filePatternToTable)
			{
				if (fileName.ToUpper().Contains(pair.Key.ToUpper()))
				{
					return pair.Value;
				}
			}
			return EmsTable.Unknown;
		}
	}
}
