
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using CommonUtils;
using Newtonsoft.Json;


namespace ParseJson
{
	class ParseJson
	{
		private const string Fact = "fact";
		private const string Pact = "pact";
		private const string Ppt = "ppt";

		static void Main(string[] args)
		{
			Common comutils = new Common();
			string directoryName = new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName;

			// Change parameters.xml file name depending on your environment configuration so that appropriate variables are defined in that file and you dont get run time error
			var path = Path.Combine(directoryName, "parameters.xml");
			string hostname = comutils.DbParam(path, "Host");
			string dbname = comutils.DbParam(path, "Dbname");
			string username = comutils.DbParam(path, "Username");
			string password = comutils.DbParam(path, "Password");

			// Open SQL connection to access the DB
			SqlConnection sqlc = comutils.GetDbSqlConnection(hostname, dbname, username, password);
			sqlc.Open();

			// Get the path of directory from parameters.xml file where trx file exists
			DirectoryInfo di = new DirectoryInfo(comutils.GetJsonDirInfo(path));
			string[] jsonFiles = Directory.GetFiles(di.ToString(), "*.json");

			foreach (string file in jsonFiles)
			{
				System.IO.FileInfo jsonFile = new System.IO.FileInfo(file);
				StreamReader fileStreamReader = new StreamReader(jsonFile.FullName);
				string jsonFileName = jsonFile.Name.ToLower();

				// Determine if the json file has results for the PPT, FACT, or PACT
				string testType = GetTestType(jsonFileName);

				// Get the file count from the results_filename table filtered on file name
				int fileCount = GetJsonFileCount(sqlc, jsonFileName);

				// Get the test results count for a Json File from the result_fields table
				int resultsCount = GetTestResultsCount(sqlc, jsonFileName, testType);

				// Insert the Json file into the results_filename table if it does not exist
				if (fileCount == 0)
				{
					InsertJsonFile(sqlc, jsonFileName);
				}

				// Get the resultfileid (primary key column) value from results_filename table for each file
				int resultFileId = GetResultFileId(sqlc, jsonFileName);

				// If there are no results associated with Json file in result_fileds table then go through Json file, load it, parse it to filter out all the result fields for each test and then insert it into result_perf_Json table
				if (resultsCount == 0)
				{
					LoadTestConfig(fileStreamReader, sqlc, jsonFileName, resultFileId, testType);
				}
			}

			sqlc.Close();
		}


		/// <summary>
		/// This function is to load the Json file, deserialize it and parse the test results into the perf_results or fact_results table depending on the test type.
		/// </summary>
		/// <param name="r"></param>
		/// <param name="sql"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="resultFileId"></param>
		/// <param name="testType"></param>
		public static void LoadTestConfig(StreamReader r, SqlConnection sql, string jsonFileName, Int32 resultFileId, string testType)
		{
			try
			{
				var json = r.ReadToEnd();
				TestCollection testresults = JsonConvert.DeserializeObject<TestCollection>(json);


				if (testresults.Tests != null)
				{
					if (testType.Equals(Fact))
					{
						//Iterate over each test in Json collection object
						foreach (var test in testresults.Tests)
						{
							SqlCommand insertTestResult = new SqlCommand();
							string cmdInsertTestResult =
								"INSERT INTO fact_results (persona, scenario, workflowName, testStatus, durationinms, testSummary, fileName, resultfileid) Values (@persona, @scenario, @workflowName, @testStatus, @durationinms, @testSummary, @fileName, @resultfileid)";
							insertTestResult.Connection = sql;
							insertTestResult.CommandText = cmdInsertTestResult;
							insertTestResult.Parameters.AddWithValue("@persona", test.Persona);
							insertTestResult.Parameters.AddWithValue("@scenario", test.Scenario);
							insertTestResult.Parameters.AddWithValue("@workflowName", test.WorkflowName);
							insertTestResult.Parameters.AddWithValue("@testStatus", test.TestStatus);
							insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
							insertTestResult.Parameters.AddWithValue("@testSummary", test.TestSummary);
							insertTestResult.Parameters.AddWithValue("@fileName", jsonFileName);
							insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
							try
							{
								insertTestResult.ExecuteNonQuery();
							}
							catch (SqlException e)
							{
								Console.WriteLine("Failed to enter results for the test " + test.TestName + " in results file " + jsonFileName);
							}

							Console.WriteLine("Result for test " + test.TestName + " entered in DB for results file" + jsonFileName);
						}
					}

					else
					{
						//Iterate over each test in Json collection object
						foreach (var test in testresults.Tests)
						{
							SqlCommand insertTestResult = new SqlCommand();
							string cmdInsertTestResult = "INSERT INTO perf_results (testName,testSummary,durationinms,testStatus,fileName,resultfileid) Values (@testName, @testSummary, @durationinms, @testStatus, @fileName, @resultfileid)";
							insertTestResult.Connection = sql;
							insertTestResult.CommandText = cmdInsertTestResult;
							insertTestResult.Parameters.AddWithValue("@testName", test.TestName);
							insertTestResult.Parameters.AddWithValue("@testSummary", test.TestSummary);
							insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
							insertTestResult.Parameters.AddWithValue("@testStatus", test.TestStatus);
							insertTestResult.Parameters.AddWithValue("@fileName", jsonFileName);
							insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
							try
							{
								insertTestResult.ExecuteNonQuery();
							}
							catch (SqlException e)
							{
								Console.WriteLine("Failed to enter results for the test " + test.TestName + " in results file " + jsonFileName);
							}

							Console.WriteLine("Result for test " + test.TestName + " entered in DB for results file" + jsonFileName);
						}
					}

				}
			}
			catch (JsonSerializationException e)
			{
				Console.WriteLine(String.Format("Error deserializing JSON file, error message is {0}", e.Message));
			}
		}

		/// <summary>
		/// This function determines what testType the jsonFileName has results for. The jsonFileName includes the type of test.
		/// </summary>
		/// <param name="jsonFileName"></param>
		private static string GetTestType(string jsonFileName)
		{
			if (jsonFileName.Contains(Fact))
			{
				return Fact;
			}

			if (jsonFileName.Contains(Pact))
			{
				return Pact;
			}

			return Ppt;
		}

		/// <summary>
		/// This function determines how many records exist in the result_fields table for the json file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		private static int GetJsonFileCount(SqlConnection sqlc, string jsonFileName)
		{
			//Initialize SQL command for getting results counts from the result_fields table
			SqlCommand getJsonFileName = new SqlCommand();

			string cmdGetJsonFileName = "Select count(*) from results_filename where fileName = @fileName";
			getJsonFileName.Connection = sqlc;
			getJsonFileName.CommandText = cmdGetJsonFileName;
			getJsonFileName.Parameters.AddWithValue("@fileName", jsonFileName);
			Int32 fileCount = (Int32)getJsonFileName.ExecuteScalar();

			return fileCount;
		}

		/// <summary>
		/// This function determines how many records exist in either the perf_results or fact_results tables, depending on the testType, for the json file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="testType"></param>
		private static int GetTestResultsCount(SqlConnection sqlc, string jsonFileName, string testType)
		{
			//Initialize SQL command for getting results counts from the tests results tables
			SqlCommand getResultsCount = new SqlCommand();

			if (testType.Equals(Fact))
			{
				string cmdGetResultsCount = "Select count (*) from fact_results where filename = @fileName";
				getResultsCount.Connection = sqlc;
				getResultsCount.CommandText = cmdGetResultsCount;
				getResultsCount.Parameters.AddWithValue("@fileName", jsonFileName);
				Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();

				return resultsCount;
			}

			else
			{
				string cmdGetResultsCount = "Select count (*) from perf_results where filename = @fileName";
				getResultsCount.Connection = sqlc;
				getResultsCount.CommandText = cmdGetResultsCount;
				getResultsCount.Parameters.AddWithValue("@fileName", jsonFileName);
				Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();

				return resultsCount;
			}
		}

		/// <summary>
		/// This function returns the resultFileId (primary key) for a json file in the results_filename table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		private static int GetResultFileId(SqlConnection sqlc, string jsonFileName)
		{
			SqlCommand getResultFileId = new SqlCommand();
			string cmdGetResultFileid = "Select resultFileId from results_filename where filename = @fileName";
			getResultFileId.Connection = sqlc;
			getResultFileId.CommandText = cmdGetResultFileid;
			getResultFileId.Parameters.AddWithValue("@fileName", jsonFileName);
			Int32 resultFileId = (Int32)getResultFileId.ExecuteScalar();

			return resultFileId;
		}

		/// <summary>
		/// This function inserts the jsonFileName into the results_filename table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		private static void InsertJsonFile(SqlConnection sqlc, string jsonFileName)
		{
			// Initialize the SQL command for inserting Json file into the results_filename table
			SqlCommand insertJsonFile = new SqlCommand();
			string cmdInsertJsonFilename = "INSERT INTO results_filename (fileName) Values (@fileName)";
			insertJsonFile.Connection = sqlc;
			insertJsonFile.CommandText = cmdInsertJsonFilename;
			insertJsonFile.Parameters.AddWithValue("@fileName", jsonFileName);
			insertJsonFile.ExecuteNonQuery();
		}
	}

	// Class to create Json properties for Json files
	public class Test
	{
		/// TestSummary field in Json results file
		public string TestSummary { get; set; }

		/// Duration field in Json results file
		public string DurationInMs { get; set; }

		/// TestStatus field in Json results file
		public string TestStatus { get; set; }

		/// TestName field in Json results file
		public string TestName { get; set; }

		/// PersonaName field in Json results file
		public string Persona { get; set; }

		/// ScenarioName field in Json results file
		public string Scenario { get; set; }

		/// WorkflowName field in Json results file
		public string WorkflowName { get; set; }
	}

	// Class to create list of Tests list object in Json file
	public class TestCollection
	{
		public List<Test> Tests { get; set; }
	}
}