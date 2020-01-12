/*
 *   This program is written to parse the automation trx result files and add them to a DB
 *   Copyright : Eze Software 
 *
 */


using System;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using CommonUtils;


namespace TrxResultsParser
{
	public class Parsetrx
	{
		static void Main(string[] args)
		{
			//initialize common class instance
			Common  comutils= new Common();
			string directoryName = new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName;

			//Change parameters.xml file name  depending on your environment configuration so that appropriate variables are defined in that file and you dont get run time error
			var path = Path.Combine(directoryName, "parameters.xml");
			string hostname = comutils.DbParam(path, "Host");
			string dbname = comutils.DbParam(path, "Dbname");
			string username = comutils.DbParam(path, "Username");
			string password = comutils.DbParam(path, "Password");
            string insertFailedResults = comutils.DbParam(path, "Insertfailedresults");
			
			//Open SQL connection to access the DB
			SqlConnection sqlc = comutils.GetDbSqlConnection(hostname,dbname,username,password);
			sqlc.Open();
			
			//Get the path of directory from parameters.xml file where trx file exists
			DirectoryInfo di = new DirectoryInfo(comutils.GetDirInfo(path));
			string[] trxFiles = Directory.GetFiles(di.ToString(), "*.trx");
			
			foreach (string file in trxFiles)
			{
				System.IO.FileInfo trxFile = new System.IO.FileInfo(file);
				StreamReader fileStreamReader = new StreamReader(trxFile.FullName);
				string trxFileName = trxFile.Name;
				string errorMessage = " ";
				string errorStackTrace = " ";
				string componentName = " ";

				// Get the file count from the results_filename table filtered on file name
                int fileCount = GetTrxFileCount(sqlc, trxFileName);

                //Initialize SQL command for getting results counts pertaining to a trx file from result_fields table
                int resultsCount = GetTestResultsCount(sqlc, trxFileName);

                //If there trx file is not present in results_filename table then insert it
                if (fileCount == 0)
				{
                  InsertTrxFile(sqlc, trxFileName);
				}

                //Get the resultfileid(primary key column) value from results_filename table pertaining to filename
                int resultFileId = GetResultFileId(sqlc, trxFileName);
                
                // if there is no results associated with trx file is present in result_fileds table then go through trx file , load it as XMLdocument ,parse it to filter out all the result fields for each test and then insert it into result_fields table
                if (resultsCount == 0)
				{
                    //serialize and add the results to DB 
                   Console.WriteLine("entering results for file " + trxFileName);
                   XmlSerializer xmlSer = new XmlSerializer(typeof(TestRunType));
				   TestRunType testRunType = (TestRunType) xmlSer.Deserialize(fileStreamReader);
					foreach (object itob1 in testRunType.Items)
					{
						ResultsType resultsType = itob1 as ResultsType;
					   if (resultsType != null)
					 	{
							foreach (object itob2 in resultsType.Items)
							{
								UnitTestResultType unitTestResultType = itob2 as UnitTestResultType;
								if (unitTestResultType != null)
								{
                                    Nullable<int> durationSql = null;
                                    string testName = unitTestResultType.testName;
                                    Console.WriteLine("entering result for test " + testName + " in resultsDB for results file" + trxFileName );
								    componentName = comutils.ClassName(testName, trxFile.FullName);
									string outcome = unitTestResultType.outcome;
                                    DateTime startTime = Convert.ToDateTime(unitTestResultType.startTime, System.Globalization.CultureInfo.InvariantCulture);
                                    DateTime endTime = Convert.ToDateTime(unitTestResultType.endTime, System.Globalization.CultureInfo.InvariantCulture);
                                    DateTime executionDate = startTime.Date;
                                    bool isAborted = unitTestResultType.isAborted;
									string duration = unitTestResultType.duration;
									
									// Getting the error message out of Test Schema in xml.If there is no error message then make the errormessage as empty string
									try
									{
										errorMessage = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.Message))[0].Value;
									}
									catch (Exception e)
									{
										errorMessage = " ";
									}
									// Getting the error stack trace out of Test Schema in xml.If there is no errorstack trace then make the errorstacktrace as empty string
									try
									{
										errorStackTrace = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.StackTrace))[0].Value;
									}
									catch (Exception e)
									{
										errorStackTrace = " ";
									}

                                    //Handling the null condition for duration 
                                    if (duration!= null)
                                    {
                                        // Conversion of duration value from  trx file to integer value in milliseconds using timespan
                                        TimeSpan span = TimeSpan.Parse(duration);
                                        durationSql = (span.Hours) * 3600000 + (span.Minutes) * 60000 + (span.Seconds) * 1000 + span.Milliseconds;
                                    } else if (duration==null)
                                    {
                                        durationSql = durationSql.GetValueOrDefault(0);
                                    }                                                         
                                                                                         
                                    // Calulate testresult bit to be inserted into testresult column depending on outcome field of test in trx file
                                    int testResult;
                                    if (outcome.Equals("Passed"))
                                    {
                                        testResult = 0;
                                    } else
                                    {
                                        testResult = 1;
                                    }

                                    // Parse className column to get the component value to be added to column component
                                    string component;
                                    string tempComponent = componentName.Replace("Eze.Integration.Functional.Test.","");
                                    if (tempComponent.Contains("msIXY"))
                                    {
                                        component = " ";
                                    }
                                    else
                                    {
                                        component = tempComponent.Substring(0, tempComponent.IndexOf('.'));
                                    }

                                    // Parse release name for OMS from trxfile name. e.g if filename is Build_19.6.0.109_Date_2019-07-10.trx the below code will parse the file and releaseName will be 19.6
                                    string releaseName =" ";
                                    if (trxFileName.Contains("Build_"))
                                    {
                                        string tempReleaseName = trxFileName.Replace("Build_", "");
                                        {
                                            releaseName = tempReleaseName.Substring(0, tempReleaseName.IndexOf('.') + 2);
                                        }
                                    }   


                                    //Connection to SQL and parametrize the sql query 
                                    SqlCommand insertTestResult = new SqlCommand();
									string cmdInsertTestResult ="INSERT INTO result_fields (testName,outcome,testresult,startTime,endTime,isAborted,duration,className,component,errorMessage,errorStackTrace,filename,resultfileid,executionDate,releaseName) Values (@testName, @outcome, @testresult, @starttime, @endtime, @isAborted, @duration, @className, @component, @errorMessage, @errorStackTrace, @filename, @resultfileid, @executionDate, @releaseName)";
									insertTestResult.Connection = sqlc;
									insertTestResult.CommandText = cmdInsertTestResult;
									insertTestResult.Parameters.AddWithValue("@testName", testName);
									insertTestResult.Parameters.AddWithValue("@outcome", outcome);
                                    insertTestResult.Parameters.AddWithValue("@testresult", testResult);
                                    insertTestResult.Parameters.AddWithValue("@starttime", startTime);
									insertTestResult.Parameters.AddWithValue("@endtime", endTime);
									insertTestResult.Parameters.AddWithValue("@isAborted", isAborted);
									insertTestResult.Parameters.AddWithValue("@duration", durationSql);
									insertTestResult.Parameters.AddWithValue("@className", componentName);
                                    insertTestResult.Parameters.AddWithValue("@component", component);
                                    insertTestResult.Parameters.AddWithValue("@errorMessage", errorMessage);
									insertTestResult.Parameters.AddWithValue("@errorStackTrace", errorStackTrace);
									insertTestResult.Parameters.AddWithValue("@filename", trxFileName);
                                    insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                                    insertTestResult.Parameters.AddWithValue("@executionDate", executionDate);
                                    insertTestResult.Parameters.AddWithValue("@releaseName", releaseName);
                                    try
									{
										insertTestResult.ExecuteNonQuery();
                                        Console.WriteLine("Result for test " + testName + " entered in DB for results file " + trxFileName);
                                    }
									catch (SqlException e)
									{
										Console.WriteLine("Failed to enter results for the test " + testName + " in results file " + trxFileName);
									}

                                    if (insertFailedResults.Equals("True"))
                                    {
                                        if (outcome.Equals("Failed"))
                                        {
                                            InsertFailedTests(sqlc, resultFileId, testName, outcome, startTime, endTime, component, errorMessage, trxFileName);
                                        }

                                    }

                                }
							}
						}
					}
				}
			}
			sqlc.Close();
		}


        /// <summary>
        /// This function is to add failed test cases to FailtedTests table
        /// </summary>
		/// <param name="sqlc"></param>
        private static void InsertFailedTests(SqlConnection sqlc,int resultFileId, string testName, string outcome, DateTime startTime, DateTime endTime, string component, string errorMessage, string trxFileName)
        {
            //Initialize SQL command 
            SqlCommand insertFailedTests = new SqlCommand();
            string cmdInsertFailedTests = "INSERT INTO failed_tests(resultfileid,testName,outcome,startTime,endTime,component,errorMessage,filename) Values (@resultfileid, @testName, @outcome, @starttime, @endtime, @component, @errorMessage, @filename)";
            insertFailedTests.Connection = sqlc;
            insertFailedTests.CommandText = cmdInsertFailedTests;
            insertFailedTests.Parameters.AddWithValue("@resultfileid", resultFileId);
            insertFailedTests.Parameters.AddWithValue("@testName", testName);
            insertFailedTests.Parameters.AddWithValue("@outcome", outcome);
            insertFailedTests.Parameters.AddWithValue("@starttime", startTime);
            insertFailedTests.Parameters.AddWithValue("@endtime", endTime);
            insertFailedTests.Parameters.AddWithValue("@component", component);
            insertFailedTests.Parameters.AddWithValue("@errorMessage", errorMessage);
            insertFailedTests.Parameters.AddWithValue("@filename", trxFileName);
            try
            {
                insertFailedTests.ExecuteNonQuery();
                Console.WriteLine("Result for failed test " + testName + " entered in DB");
            }
            catch (SqlException e)
            {
                Console.WriteLine("Failed to enter results for the test in Failed_results " + testName);
            }
        }

        
        /// <summary>
		/// This function determines how many records exist in the results_filename table for the trx file.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="trxFileName"></param>
		private static int GetTrxFileCount(SqlConnection sqlc, string trxFileName)
        {
            //Initialize SQL command for getting results counts from the result_fields table
            SqlCommand getTrxFileName = new SqlCommand();
            string cmdGetTrxFileName = "Select count(*) from results_filename where fileName = @fileName";
            getTrxFileName.Connection = sqlc;
            getTrxFileName.CommandText = cmdGetTrxFileName;
            getTrxFileName.Parameters.AddWithValue("@fileName", trxFileName);
            Int32 fileCount = (Int32)getTrxFileName.ExecuteScalar();
            return fileCount;
        }

        /// <summary>
        /// This function determines how many records exist in result_fields table for the trxfile.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="trxFileName"></param>        
        private static int GetTestResultsCount(SqlConnection sqlc, string trxFileName)
        {
            //Initialize SQL command for getting results counts from the result_fields table
            SqlCommand getResultsCount = new SqlCommand();
            string cmdGetResultsCount = "Select count(*) from result_fields where filename = @fileName";
            getResultsCount.Connection = sqlc;
            getResultsCount.CommandText = cmdGetResultsCount;
            getResultsCount.Parameters.AddWithValue("@fileName", trxFileName);
            Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();
            return resultsCount;
        }


        /// <summary>
		/// This function inserts the trxFile into the results_filename table.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="trxFileName"></param>
		private static void InsertTrxFile(SqlConnection sqlc, string trxFileName)
        {
            // Initialize the SQL command for inserting trx file into the results_filename table
            SqlCommand insertTrxFile = new SqlCommand();
            string cmdInsertTrxFilename = "INSERT INTO results_filename (fileName) Values (@fileName)";
            insertTrxFile.Connection = sqlc;
            insertTrxFile.CommandText = cmdInsertTrxFilename;
            insertTrxFile.Parameters.AddWithValue("@fileName", trxFileName);
            insertTrxFile.ExecuteNonQuery();
        }

        /// <summary>
        /// This function returns the resultFileId (primary key) for a trxfile in the results_filename table.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="trxFileName"></param>
        private static int GetResultFileId(SqlConnection sqlc, string trxFileName)
        {
            //Initialize SQL command 
            SqlCommand getResultFileId = new SqlCommand();
            string cmdGetResultFileid = "Select resultFileId from results_filename where filename = @fileName";
            getResultFileId.Connection = sqlc;
            getResultFileId.CommandText = cmdGetResultFileid;
            getResultFileId.Parameters.AddWithValue("@fileName", trxFileName);
            Int32 resultFileId = (Int32)getResultFileId.ExecuteScalar();
            return resultFileId;
        }
    }
}


