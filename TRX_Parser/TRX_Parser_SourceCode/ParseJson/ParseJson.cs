
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

using Newtonsoft.Json;

using CommonUtils;
using ParseJson.Results;

namespace ParseJson
{
	public enum EmsTableCategory { Unknown, LatencyWithParameters }

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
				string jsonFileDate = jsonFileName.Substring(0, jsonFileName.LastIndexOf("."));
				DateTime jsonFileDateTime = Convert.ToDateTime(jsonFileDate.Substring(jsonFileDate.LastIndexOf("_") + 1), System.Globalization.CultureInfo.InvariantCulture);
				DateTime jsonFileExecutionTime = jsonFileDateTime.Date;

				var platform = comutils.DeterminePlatformFromFileName(jsonFileName);

				if (platform == PlatformType.Unknown)
				{
					Console.WriteLine(String.Format("Unable to determine platform for file {0}", jsonFileName));
					continue;
				}

				// Get the file count from the results_filename table filtered on file name
				int fileCount = GetJsonFileCount(sqlc, jsonFileName, platform);

				// Insert the Json file into the results_filename table if it does not exist
				if (fileCount == 0)
				{
					InsertJsonFile(sqlc, jsonFileName, jsonFileExecutionTime, platform);
				}

				// Get the resultfileid (primary key column) value from results_filename table for each file
				int resultFileId = GetResultFileId(sqlc, jsonFileName, platform);

				if (platform == PlatformType.OMS)
				{
                    
                    if (jsonFileName.Contains("perfcounters"))
                    {
                        int perfCount = GetPerfCountersCount(sqlc, jsonFileName, resultFileId);
                        if (perfCount == 0)
                        {
                            LoadPerfCounterConfig(fileStreamReader, sqlc, jsonFileName, resultFileId, jsonFileExecutionTime);
                        }
                    continue;
                    }
					// Determine if the json file has results for the PPT, FACT, or PACT
					string testType = GetTestType(jsonFileName);

					// Get the test results count for a Json File from the result_fields table
					int resultsCount = GetTestResultsCount(sqlc, jsonFileName, resultFileId, testType);

					// If there are no results associated with Json file in result_fileds table then go through Json file, load it, parse it to filter out all the result fields for each test and then insert it into result_perf_Json table
					if (resultsCount == 0)
					{
						LoadTestConfig(fileStreamReader, sqlc, jsonFileName, resultFileId, testType, jsonFileExecutionTime);
					}
				}
				else if (platform == PlatformType.EMS)
				{
					//Determine appropriate table for EMS perf result
					var table = GetTableOfInterest(jsonFileName);

					// Get the test results count for a Json File from the result_fields table
					int resultsCount = GetTestResultsCount(sqlc, jsonFileName, resultFileId, "", table);

					// If there are no results associated with Json file in result_fileds table then go through Json file, load it, parse it to filter out all the result fields for each test and then insert it into result_perf_Json table
					if (resultsCount == 0)
					{
						InsertTestData(fileStreamReader, sqlc, jsonFileName, resultFileId, table, jsonFileExecutionTime);
					}
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
        public static void LoadTestConfig(StreamReader r, SqlConnection sql, string jsonFileName, Int32 resultFileId, string testType, DateTime executionDate)
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
                                "INSERT INTO fact_results (persona, scenario, workflowName, testStatus, durationinms, testSummary, fileName, resultfileid, ExecutionDate) Values (@persona, @scenario, @workflowName, @testStatus, @durationinms, @testSummary, @fileName, @resultfileid, @ExecutionDate)";
                            insertTestResult.Connection = sql;
                            insertTestResult.CommandText = cmdInsertTestResult;
                            insertTestResult.Parameters.AddWithValue("@persona", test.Persona);
                            insertTestResult.Parameters.AddWithValue("@scenario", test.Scenario);
                            insertTestResult.Parameters.AddWithValue("@workflowName", test.WorkflowName);
                            insertTestResult.Parameters.AddWithValue("@testStatus", test.TestStatus);
                            if (test.DurationInMs.Equals(""))
                            {
                                test.DurationInMs = "0";
                                insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
                            }
                            else
                            {
                                insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
                            }
                            insertTestResult.Parameters.AddWithValue("@testSummary", test.TestSummary);
                            insertTestResult.Parameters.AddWithValue("@fileName", jsonFileName);
                            insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                            insertTestResult.Parameters.AddWithValue("@ExecutionDate", executionDate);
                            try
                            {
                                insertTestResult.ExecuteNonQuery();
                            }
                            catch (SqlException e)
                            {
                                Console.WriteLine("Failed to enter results for the persona and testscenerio " + test.Persona + "," + test.Scenario + " in results file " + jsonFileName);
                            }

                            Console.WriteLine("Result for persona and scenerio " + test.Persona + "," + test.Scenario + " entered in DB for results file" + jsonFileName);
                        }
                    }

                    else
                    {
                        //Iterate over each test in Json collection object
                        foreach (var test in testresults.Tests)
                        {
                            SqlCommand insertTestResult = new SqlCommand();
                            string cmdInsertTestResult = "INSERT INTO perf_results (testName,testSummary,durationinms,testStatus,fileName,resultfileid,ExecutionDate) Values (@testName, @testSummary, @durationinms, @testStatus, @fileName, @resultfileid, @ExecutionDate)";
                            insertTestResult.Connection = sql;
                            insertTestResult.CommandText = cmdInsertTestResult;
                            insertTestResult.Parameters.AddWithValue("@testName", test.TestName);
                            insertTestResult.Parameters.AddWithValue("@testSummary", test.TestSummary);
                            if (test.DurationInMs.Equals(""))
                            {
                                test.DurationInMs = "0";
                                insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
                            }
                            else
                            {
                                insertTestResult.Parameters.AddWithValue("@durationinms", Convert.ToInt32(test.DurationInMs));
                            }
                            insertTestResult.Parameters.AddWithValue("@testStatus", test.TestStatus);
                            insertTestResult.Parameters.AddWithValue("@fileName", jsonFileName);
                            insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                            insertTestResult.Parameters.AddWithValue("@ExecutionDate", executionDate);
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
        /// This function is to load the Json file, deserialize it and parse the test results into the perf_counters table.
        /// </summary>
        /// <param name="r"></param>
        /// <param name="sql"></param>
        /// <param name="jsonFileName"></param>
        /// <param name="resultFileId"></param>        
        public static void LoadPerfCounterConfig(StreamReader r, SqlConnection sql, string jsonFileName, Int32 resultFileId, DateTime executionDate)
		{
            try
            {
                var json = r.ReadToEnd();
                TestCollection testResults = JsonConvert.DeserializeObject<TestCollection>(json);

                if (testResults.Tests != null)
                {
                    //Iterate over each test in Json collection object
                    foreach (var test in testResults.Tests)
                    {
                        SqlCommand insertTestResult = new SqlCommand();
                        string cmdInsertTestResult =
                                   "INSERT INTO perf_counters (testName, machineName, performanceCategory, performanceCategoryType, performanceValue, performanceInstance, fileName, resultfileid, ExecutionDate) Values (@testName, @machineName, @performanceCategory, @performanceCategoryType, @performanceValue, @performanceInstance, @fileName, @resultfileid, @ExecutionDate)";

                        insertTestResult.Connection = sql;
                        insertTestResult.CommandText = cmdInsertTestResult;
                        insertTestResult.Parameters.AddWithValue("@testName", test.TestName);
                        insertTestResult.Parameters.AddWithValue("@machineName", test.MachineName);
                        insertTestResult.Parameters.AddWithValue("@performanceCategory", test.PerformanceCategory);
                        insertTestResult.Parameters.AddWithValue("@performanceCategoryType", test.PerformanceCategoryType);
                        insertTestResult.Parameters.AddWithValue("@performanceValue", test.PerformanceValue);
                        insertTestResult.Parameters.AddWithValue("@performanceInstance", test.PerformanceInstance);
                        insertTestResult.Parameters.AddWithValue("@fileName", jsonFileName);
                        insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                        insertTestResult.Parameters.AddWithValue("@ExecutionDate", executionDate);
                        try
                        {
                            insertTestResult.ExecuteNonQuery();
                        }
                        catch (SqlException e)
                        {
                            Console.WriteLine("Failed to enter results for the test " + test.TestName + "performance category" +test.PerformanceCategory + " in results file " + jsonFileName);
                        }

                        Console.WriteLine("Result for testname,performanceCategoryType " + test.TestName + "," + test.PerformanceCategoryType + " entered in DB for results file" + jsonFileName);
                    }
                }
            }
            catch (JsonSerializationException e)
            {
                Console.WriteLine(String.Format("Error deserializing JSON file, error message is {0}", e.Message));
            }
        }

		/// <summary>
		/// This function is to load the Json file, deserialize it and parse the test results into the ems perf tables based on table category.
		/// </summary>
		/// <param name="r"></param>
		/// <param name="sql"></param>
		/// <param name="resultFileId"></param>
		/// <param name="table"></param>
		public static void InsertTestData(StreamReader r, SqlConnection sql, string jsonFileName, Int32 resultFileId, EmsTableCategory table, DateTime executionDate)
		{
			try
			{
				var json = r.ReadToEnd();
				
				if (table == EmsTableCategory.LatencyWithParameters)
				{
					if (String.IsNullOrEmpty(json))
					{
						Console.WriteLine(String.Format("No valid data in file: {0}.", jsonFileName));
						return;
					}
					var results = JsonConvert.DeserializeObject<List<EmsLatencyResult>>(json);
					var parameters = JsonConvert.DeserializeObject<List<EmsLatencyParameters>>(json)[0];
					if (parameters.FillsPerOrder != null)
					{
						parameters.WithFills = true;
					}
					if (results != null && parameters != null)
					{
						//Determine if this set of parameters is already in database; if in database retrieve key else insert
						int paramsId = GetOrInsertParamsId(sql, parameters);

						string testName = GetTestName(jsonFileName);

						//Iterate over each result and insert into the database
						foreach (var result in results)
						{
							SqlCommand insertTestResult = new SqlCommand();
							string cmdInsertTestResult =
								@"INSERT INTO dbo.ems_perf_latency_results 
								(resultfileid, ParametersId, TestName, TotalMilliseconds, PipelineLatency, LockLatency, BasketNumber, ExecutionDate)
								Values 
								(@resultfileid, @ParametersId, @TestName, @TotalMilliseconds, @PipelineLatency, @LockLatency, @BasketNumber, @ExecutionDate)";
							insertTestResult.Connection = sql;
							insertTestResult.CommandText = cmdInsertTestResult;
							insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
							insertTestResult.Parameters.AddWithValue("@ParametersId", paramsId);
							insertTestResult.Parameters.AddWithValue("@TestName", testName);
							insertTestResult.Parameters.AddWithValue("@TotalMilliseconds", result.TotalMilliseconds);
							insertTestResult.Parameters.AddWithValue("@PipelineLatency", result.PipelineLatency);
							insertTestResult.Parameters.AddWithValue("@LockLatency", result.LockLatency);
							insertTestResult.Parameters.AddWithValue("@BasketNumber", result.BasketNumber);
							insertTestResult.Parameters.AddWithValue("@ExecutionDate", executionDate);

							try
							{
								insertTestResult.ExecuteNonQuery();
							}
							catch (SqlException e)
							{
								Console.WriteLine("Failed to enter results for the test " + testName + " in results file id " + resultFileId);
								Console.WriteLine(e.Message);
							}

							//Console.WriteLine("Result for test " + testName + " entered in DB for results file id " + resultFileId);
						}
					}
				}
				else
				{
					Console.WriteLine("Unknown category to work with.");
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
		private static int GetJsonFileCount(SqlConnection sqlc, string jsonFileName, PlatformType platform)
		{
			//Initialize SQL command for getting results counts from the result_fields table
			SqlCommand getJsonFileName = new SqlCommand();
			string cmdGetJsonFileName = "";

			if (platform == PlatformType.OMS)
			{
				cmdGetJsonFileName = "Select count(*) from results_filename where fileName = @fileName";
			}
			else if (platform == PlatformType.EMS)
			{
				cmdGetJsonFileName = "Select count(*) from ems_results_filename where FileName = @fileName";
			}
			getJsonFileName.Connection = sqlc;
			getJsonFileName.CommandText = cmdGetJsonFileName;
			getJsonFileName.Parameters.AddWithValue("@fileName", jsonFileName);
			Int32 fileCount = (Int32)getJsonFileName.ExecuteScalar();

			return fileCount;
		}

		/// <summary>
		/// This function is a master function for getting the parameterId or creating it if needed for the table ems_perf_latency_parameters.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="parameters"></param>
		private static int GetOrInsertParamsId(SqlConnection sqlc, EmsLatencyParameters parameters)
		{
			int paramId = GetParamsId(sqlc, parameters);

			if (paramId == 0)
			{
				InsertParameters(sqlc, parameters);
				paramId = GetParamsId(sqlc, parameters);
			}

			return (int)paramId;
		}

		/// <summary>
		/// This function attempts to get the parametersId but will return 0 if it cannot. This is specific to the ems_perf_latency_parameters table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="parameters"></param>
		private static int GetParamsId(SqlConnection sqlc, EmsLatencyParameters parameters)
		{
			SqlCommand getParamsId = new SqlCommand();

			List<string> emsLatencyWhereParams = new List<string>()
			{
				String.Format("BasketsPerSecond = {0}", parameters.BasketsPerSecond),
				String.Format("BasketSize = {0}", parameters.BasketSize),
				String.Format("BasketCount = {0}", parameters.BasketCount),
				String.Format("AmbientForeignExecutionsPerSecond = {0}", parameters.AmbientForeignExecutionsPerSecond),
				String.Format("StagedFlow = {0}", parameters.StagedFlow ? 1 : 0),
				String.Format("NeutralFlow = {0}", parameters.NeutralFlow ? 1 : 0),
				String.Format("MachineUnderTest LIKE '{0}'", parameters.MachineUnderTest),
				String.Format("TestAgentMachine LIKE '{0}'", parameters.TestAgentMachine),
				String.Format("WithFills = {0}", parameters.WithFills ? 1 : 0),
				String.Format("FillsPerOrder {0}", parameters.FillsPerOrder == null ? "IS NULL" : String.Format("= {0}", parameters.FillsPerOrder)),
				String.Format("ATParameter1 {0}", parameters.ATParameter1 == null ? "IS NULL" : String.Format("= {0}", parameters.ATParameter1)),
				String.Format("ATParameter2 {0}", parameters.ATParameter2 == null ? "IS NULL" : String.Format("= '{0}'", parameters.ATParameter2)),
				String.Format("BitParameter1 {0}", parameters.BitParameter1 == null ? "IS NULL" : String.Format("= '{0}'", parameters.BitParameter1)),
				String.Format("IntParameter1 {0}", parameters.IntParameter1 == null ? "IS NULL" : String.Format("= '{0}'", parameters.IntParameter1))
			};

			string cmdGetParamsCount = "SELECT COUNT(*) FROM ems_perf_latency_parameters";

			getParamsId.Connection = sqlc;
			getParamsId.CommandText = String.Format("{0} WHERE {1}", cmdGetParamsCount, String.Join(" AND ", emsLatencyWhereParams));
			Int32 paramsCount = (Int32)getParamsId.ExecuteScalar();
			int paramsId = 0;

			if (paramsCount != 0)
			{
				string cmdGetParamsId = "SELECT ParametersId FROM ems_perf_latency_parameters";
				getParamsId.CommandText = String.Format("{0} WHERE {1}", cmdGetParamsId, String.Join(" AND ", emsLatencyWhereParams));
				paramsId = Convert.ToInt32(getParamsId.ExecuteScalar());
			}
			return (int) paramsId;
		}

		/// <summary>
		/// This function creates a row in the ems_perf_latency_parameters table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="parameters"></param>
		private static void InsertParameters(SqlConnection sqlc, EmsLatencyParameters parameters)
		{
			SqlCommand insertParameters = new SqlCommand();
			string cmdInsertParameters = @"INSERT INTO ems_perf_latency_parameters 
										 (BasketsPerSecond, BasketSize, BasketCount, AmbientForeignExecutionsPerSecond, StagedFlow, NeutralFlow, MachineUnderTest, TestAgentMachine, WithFills, FillsPerOrder, ATParameter1, ATParameter2, BitParameter1, IntParameter1)
										 Values
										 (@BasketsPerSecond, @BasketSize, @BasketCount, @AmbientForeignExecutionsPerSecond, @StagedFlow, @NeutralFlow, @MachineUnderTest, @TestAgentMachine, @WithFills, @FillsPerOrder, @ATParameter1, @ATParameter2, @BitParameter1, @IntParameter1)";
			insertParameters.Connection = sqlc;
			insertParameters.CommandText = cmdInsertParameters;

			insertParameters.Parameters.AddWithValue("@BasketsPerSecond", parameters.BasketsPerSecond);
			insertParameters.Parameters.AddWithValue("@BasketSize", parameters.BasketSize);
			insertParameters.Parameters.AddWithValue("@BasketCount", parameters.BasketCount);
			insertParameters.Parameters.AddWithValue("@AmbientForeignExecutionsPerSecond", parameters.AmbientForeignExecutionsPerSecond);
			insertParameters.Parameters.AddWithValue("@StagedFlow", parameters.StagedFlow ? 1 : 0);
			insertParameters.Parameters.AddWithValue("@NeutralFlow", parameters.NeutralFlow? 1 : 0);
			insertParameters.Parameters.AddWithValue("@MachineUnderTest", parameters.MachineUnderTest);
			insertParameters.Parameters.AddWithValue("@TestAgentMachine", parameters.TestAgentMachine);
			insertParameters.Parameters.AddWithValue("@WithFills", parameters.WithFills ? 1 : 0);
			insertParameters.Parameters.AddWithValue("@FillsPerOrder", ((object)parameters.FillsPerOrder ?? System.DBNull.Value));
			insertParameters.Parameters.AddWithValue("@ATParameter1", (object)parameters.ATParameter1 ?? System.DBNull.Value);
			insertParameters.Parameters.AddWithValue("@ATParameter2", (object)parameters.ATParameter2 ?? System.DBNull.Value);
			insertParameters.Parameters.AddWithValue("@BitParameter1", (object)parameters.BitParameter1 ?? System.DBNull.Value);
			insertParameters.Parameters.AddWithValue("@IntParameter1", (object)parameters.IntParameter1 ?? System.DBNull.Value);

			insertParameters.ExecuteNonQuery();
		}

		/// <summary>
		/// This function determines how many records exist in either the perf_results or fact_results tables, depending on the testType, for the json file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="testType"></param>
		private static int GetTestResultsCount(SqlConnection sqlc, string jsonFileName, int resultFileId, string testType = "", EmsTableCategory table = EmsTableCategory.Unknown)
		{
			//Initialize SQL command for getting results counts from the tests results tables
			SqlCommand getResultsCount = new SqlCommand();
			getResultsCount.Connection = sqlc;

			if (testType.Equals(Fact))
			{
				string cmdGetResultsCount = "Select count (*) from fact_results where filename = @fileName";
				getResultsCount.CommandText = cmdGetResultsCount;
				getResultsCount.Parameters.AddWithValue("@fileName", jsonFileName);
			}
			else if (table == EmsTableCategory.LatencyWithParameters)
			{
				string cmdGetResultsCount = "Select count (*) from ems_perf_latency_results where resultfileid = @resultfileid";
				getResultsCount.CommandText = cmdGetResultsCount;
				getResultsCount.Parameters.AddWithValue("@resultfileid", resultFileId);
			}
			else
			{
				string cmdGetResultsCount = "Select count (*) from perf_results where filename = @fileName";				
				getResultsCount.CommandText = cmdGetResultsCount;
				getResultsCount.Parameters.AddWithValue("@fileName", jsonFileName);
			}

			Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();
			return resultsCount;
		}

        /// <summary>
		/// This function determines how many records exist in perf_counters table for the json file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		/// <param name="testType"></param>
		private static int GetPerfCountersCount(SqlConnection sqlc, string jsonFileName, int resultFileId)
        {
            //Initialize SQL command for getting results counts from the tests results tables
            SqlCommand getResultsCount = new SqlCommand();
            getResultsCount.Connection = sqlc;            
            string cmdGetResultsCount = "Select count (*) from perf_counters where filename = @fileName";
            getResultsCount.CommandText = cmdGetResultsCount;
            getResultsCount.Parameters.AddWithValue("@fileName", jsonFileName);
            Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();
            return resultsCount;
        }

        /// <summary>
        /// This function returns the resultFileId (primary key) for a json file in the results_filename table.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="jsonFileName"></param>
        private static int GetResultFileId(SqlConnection sqlc, string jsonFileName, PlatformType platform)
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
			getResultFileId.Parameters.AddWithValue("@fileName", jsonFileName);
			Int32 resultFileId = (Int32)getResultFileId.ExecuteScalar();

			return resultFileId;
		}

		/// <summary>
		/// This function retrieves the test name based on EMS way of labelling files.
		/// </summary>
		/// <param name="jsonFileName"></param>
		private static string GetTestName(string jsonFileName)
		{
			//full file name format changed to: pixy_Build_<exec#>_GW_<gatewayBuild#>_TS_<tradesrvBuild#>_FH_<fixhandlerBuild#>_<ClassName>_<TestName>_Time_<HHmmssfff>_Date_<yyyy-mm-dd>.json
			//Assuming test name will always be right before time stamp
			string previousStr = null;
			var splitStr = jsonFileName.Split('_');
			bool foundTestName = false;
			if (jsonFileName.ToUpper().Contains("_TIME_"))
			{	
				foreach (var subStr in splitStr)
				{
					if (subStr.ToUpper() == "TIME")
					{
						foundTestName = true;
						break;
					}
					previousStr = subStr;
				}
			}
			if (foundTestName)
			{
				return previousStr;
			}
			if (splitStr.Length >= 11)
			{
				return splitStr[10];
			}
			return "UNKNOWN";
		}

		/// <summary>
		/// This function inserts the jsonFileName into the results_filename table with execution date.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="jsonFileName"></param>
		private static void InsertJsonFile(SqlConnection sqlc, string jsonFileName, DateTime jsonFileExecutionDate, PlatformType platform)
		{
			// Initialize the SQL command for inserting Json file into the results_filename table
			SqlCommand insertJsonFile = new SqlCommand();
			string cmdInsertJsonFilename = "";
			if (platform == PlatformType.OMS)
			{
				cmdInsertJsonFilename = "INSERT INTO results_filename (fileName, ExecutionDate) Values (@fileName,@ExecutionDate)";
			}
			else if (platform == PlatformType.EMS)
			{
				cmdInsertJsonFilename = "INSERT INTO ems_results_filename (fileName, ExecutionDate) Values (@fileName,@ExecutionDate)";
			}
			insertJsonFile.Connection = sqlc;
			insertJsonFile.CommandText = cmdInsertJsonFilename;
			insertJsonFile.Parameters.AddWithValue("@fileName", jsonFileName);
            insertJsonFile.Parameters.AddWithValue("@ExecutionDate", jsonFileExecutionDate);
            insertJsonFile.ExecuteNonQuery();
		}

		/// <summary>
		/// This function determines what table the jsonFileName has results for. The jsonFileName includes the type of test.
		/// </summary>
		/// <param name="jsonFileName"></param>
		private static EmsTableCategory GetTableOfInterest(string jsonFileName)
		{
			Dictionary<string, EmsTableCategory> testClassToTable = new Dictionary<string, EmsTableCategory>();
			testClassToTable.Add("_OutboundLatency_", EmsTableCategory.LatencyWithParameters);
			testClassToTable.Add("_RoundTripLatency_", EmsTableCategory.LatencyWithParameters);
			testClassToTable.Add("_InboundLatency_", EmsTableCategory.LatencyWithParameters);
			testClassToTable.Add("_AutomatedTradingLatency_", EmsTableCategory.LatencyWithParameters);
			testClassToTable.Add("_OmsIntegration_", EmsTableCategory.LatencyWithParameters);
			testClassToTable.Add("_ParallelExamples_", EmsTableCategory.LatencyWithParameters);

			foreach (var pair in testClassToTable)
			{
				if (jsonFileName.ToUpper().Contains(pair.Key.ToUpper()))
				{
					return pair.Value;
				}
			}

			return EmsTableCategory.Unknown;
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

        /// MachineName field in Perfcounter Json file
        public string MachineName { get; set; }

        /// PerformanceCategory field in Perfcounter Json file
        public string PerformanceCategory { get; set; }

        /// PerformanceCategoryType field in Perfcounter Json file
        public string PerformanceCategoryType { get; set; }

        /// PerformanceValue field in Perfcounter Json file
        public string PerformanceValue { get; set; }

       /// PerformanceInstance field in Perfcounter Json file
        public string PerformanceInstance { get; set; }
    }

	// Class to create list of Tests list object in Json file
	public class TestCollection
	{
		public List<Test> Tests { get; set; }
	}
}
