/*
 *   This program is written to parse the automation trx result files and add them to a DB
 *   Copyright : Eze Software 
 *
 */

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using CommonUtils;
using System.Data;


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
                string trxFileNameParent = "";


                var platform = comutils.DeterminePlatformFromFileName(trxFileName);

                if (platform == PlatformType.Unknown)
                {
                    Console.WriteLine(String.Format("Unable to determine platform for file {0}", trxFileName));
                    continue;
                }

                string trxFileDate = trxFileName.Substring(0, trxFileName.LastIndexOf("."));
                DateTime trxFileDateTime = Convert.ToDateTime(trxFileDate.Substring(trxFileDate.LastIndexOf("_") + 1), System.Globalization.CultureInfo.InvariantCulture); 
                DateTime trxFileExecutionTime = trxFileDateTime.Date;

                // Get the file count from the results_filename table filtered on file name
                int fileCount = GetTrxFileCount(sqlc, trxFileName, platform);

                //If there trx file is not present in results_filename table then insert it
                if (fileCount == 0)
                {
                    InsertTrxFile(sqlc, trxFileName, trxFileExecutionTime, platform);
                }


                if (trxFileName.Contains("RerunResults"))
                {
                    //Find the parent file of rerun file
                    trxFileNameParent = trxFileName.Replace("RerunResults", "MainResults");
                    //Find the resultfile id for parent file
                    int resultFileIdParent = GetResultFileId(sqlc, trxFileNameParent, platform);
                    // Check if result of parent is there in results_fields table
                    int resultsCountParent = GetTestResultsCount(sqlc, trxFileNameParent, platform);
                    //Check the condition if parent results file results are zero or not in result fields table 
                    if (resultsCountParent == 0)
                    {
                        Console.WriteLine("Mainresults(parentfile) has not been parsed in to result_fields table");
                    }
                    //Check the condition if results of rerun trx file is not there in failed tests table
                    else if(GetTestResultsCountRerunTests(sqlc,resultFileIdParent,platform)==0)
                    {
                        Console.WriteLine("Inserting passed results from rerun into failed tests table");
                        XmlSerializer xmlSer = new XmlSerializer(typeof(TestRunType));
                        TestRunType testRunType = (TestRunType)xmlSer.Deserialize(fileStreamReader);
                        InsertOmsTestDataRerun(sqlc, resultFileIdParent, trxFile.FullName, trxFileName, testRunType);
                    }
                    else
                    {
                        Console.WriteLine("Rerun file results are already there in failed tests table ");
                    }
                   continue;
                }

                //Get the resultfileid(primary key column) value from results_filename table pertaining to filename
                int resultFileId = GetResultFileId(sqlc, trxFileName, platform);

                

                //Initialize SQL command for getting results counts pertaining to a trx file from result_fields table
                int resultsCount = GetTestResultsCount(sqlc, trxFileName, platform);

                if (platform == PlatformType.OMS)
                {
                    // if there is no results associated with trx file is present in result_fileds table then go through trx file , load it as XMLdocument ,parse it to filter out all the result fields for each test and then insert it into result_fields table
                    if (resultsCount == 0)
                    {
                        Console.WriteLine("entering results for file " + trxFileName);
                        XmlSerializer xmlSer = new XmlSerializer(typeof(TestRunType));
                        TestRunType testRunType = (TestRunType)xmlSer.Deserialize(fileStreamReader);

                        InsertOmsTestData(sqlc, resultFileId, trxFile.FullName, trxFileName, testRunType);
                    }
                }
                else if (platform == PlatformType.EMS)
                {
                    // if there are no results associated with trx file is present in result_fields table then go through trx file, load it as XMLdocument, parse it to filter out all the result fields for each test and then insert it into ems_result_fields table
                    if (resultsCount == 0)
                    {
                        Console.WriteLine("entering results for file " + trxFileName);
                        XmlSerializer xmlSer = new XmlSerializer(typeof(TestRunType));
                        TestRunType testRunType = (TestRunType)xmlSer.Deserialize(fileStreamReader);

                        InsertEmsTestData(sqlc, resultFileId, trxFileName, testRunType);
                    }
                }
                else
                {
                    Console.WriteLine(String.Format("Currently there is no support for {0}.", platform.ToString()));
                }
			}
			sqlc.Close();
		}
               
        /// <summary>
        /// This function determines how many records exist in the results_filename table for the trx file.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="trxFileName"></param>
        private static int GetTrxFileCount(SqlConnection sqlc, string trxFileName, PlatformType platform)
        {
            //Initialize SQL command for getting results counts from the result_fields table
            SqlCommand getTrxFileName = new SqlCommand();
            string cmdGetTrxFileName = "";

            if (platform == PlatformType.OMS)
            {
                cmdGetTrxFileName = "Select count(*) from results_filename where fileName = @fileName";
            }
            else if (platform == PlatformType.EMS)
            {
                cmdGetTrxFileName = "Select count(*) from ems_results_filename where FileName = @fileName";
            }

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
        private static int GetTestResultsCount(SqlConnection sqlc, string trxFileName, PlatformType platform)
        {
            //Initialize SQL command for getting results counts from the result_fields table
            SqlCommand getResultsCount = new SqlCommand();
            string cmdGetResultsCount = "";

            if (platform == PlatformType.OMS)
            {
                cmdGetResultsCount = "Select count(*) from result_fields where filename = @fileName";
            }
            else if (platform == PlatformType.EMS)
            {
                cmdGetResultsCount = "Select count(*) from ems_result_fields where FileName = @fileName";
            }

            getResultsCount.Connection = sqlc;
            getResultsCount.CommandText = cmdGetResultsCount;
            getResultsCount.Parameters.AddWithValue("@fileName", trxFileName);
            Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();
            return resultsCount;
        }


        /// <summary>
        /// This function determines how many records exists in failed_tests  table for the rerun trxfile.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="trxFileName"></param>        
        private static int GetTestResultsCountRerunTests(SqlConnection sqlc, int resultfileid, PlatformType platform)
        {
            //Initialize SQL command for getting results counts from the result_fields table
            SqlCommand getResultsCount = new SqlCommand();
            string cmdGetResultsCount = "";

            if (platform == PlatformType.OMS)
            {
                cmdGetResultsCount = "Select count(*) from failed_tests where resultfileid = @resultfileid";
            }
            getResultsCount.Connection = sqlc;
            getResultsCount.CommandText = cmdGetResultsCount;
            getResultsCount.Parameters.AddWithValue("@resultfileid", resultfileid);
            Int32 resultsCount = (Int32)getResultsCount.ExecuteScalar();
            return resultsCount;
        }



        /// <summary>
		/// This function inserts the trxFile into the results_filename table with execution date.
		/// </summary>
		/// <param name="sqlc"></param>
		/// <param name="trxFileName"></param>
		private static void InsertTrxFile(SqlConnection sqlc, string trxFileName, DateTime executionDate, PlatformType platform)
        {
            // Initialize the SQL command for inserting trx file into the results_filename table
            SqlCommand insertTrxFile = new SqlCommand();
            string cmdInsertTrxFilename = "";

            if (platform == PlatformType.OMS)
            {
                cmdInsertTrxFilename = "INSERT INTO results_filename (fileName,ExecutionDate) Values (@fileName, @ExecutionDate)";
            }
            else if (platform == PlatformType.EMS)
            {
                cmdInsertTrxFilename = "INSERT INTO ems_results_filename (fileName,ExecutionDate) Values (@fileName, @ExecutionDate)";
            }

            insertTrxFile.Connection = sqlc;
            insertTrxFile.CommandText = cmdInsertTrxFilename;
            insertTrxFile.Parameters.AddWithValue("@fileName", trxFileName);
            insertTrxFile.Parameters.AddWithValue("@ExecutionDate", executionDate);
            insertTrxFile.ExecuteNonQuery();
        }

        /// <summary>
        /// This function returns the resultFileId (primary key) for a trxfile in the results_filename table.
        /// </summary>
        /// <param name="sqlc"></param>
        /// <param name="trxFileName"></param>
        private static int GetResultFileId(SqlConnection sqlc, string trxFileName, PlatformType platform)
        {
            //Initialize SQL command 
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
            getResultFileId.Parameters.AddWithValue("@fileName", trxFileName);
            Int32 resultFileId = (Int32)getResultFileId.ExecuteScalar();
            return resultFileId;
        }

        private static void InsertEmsTestData(SqlConnection sqlc, int resultFileId, string trxFileName, TestRunType testRunType)
        {
            string workflow = " ";
            string errorMessage = " ";
            string errorStackTrace = " ";
            Dictionary<string, string> idToCategory = new Dictionary<string, string>();
            Dictionary<string, string> idToClassName = new Dictionary<string, string>();

            //GIANT LOOP TO GET THE WORKFLOW/TESTCATEGORY/CLASSNAME
            foreach (object itob1 in testRunType.Items)
            {
                TestRunTypeTestDefinitions testRunTypeTestDefinitions = itob1 as TestRunTypeTestDefinitions;

                if (testRunTypeTestDefinitions != null)
                {
                    foreach (object itob3 in testRunTypeTestDefinitions.Items)
                    {
                        UnitTestType unitTestType = itob3 as UnitTestType;
                        if (unitTestType != null)
                        {
                            string id = unitTestType.id;

                            UnitTestTypeTestMethod unitTestTypeTestMethod = unitTestType.TestMethod as UnitTestTypeTestMethod;
                            if (unitTestTypeTestMethod != null)
                            {
                                string className = unitTestTypeTestMethod.className.Split(',')[0];
                                idToClassName.Add(id, className);
                            }

                            foreach (object itob4 in unitTestType.Items)
                            {
                                BaseTestTypeTestCategory baseTestTypeTestCategory = itob4 as BaseTestTypeTestCategory;

                                if (baseTestTypeTestCategory != null)
                                {
                                    foreach (object item in baseTestTypeTestCategory.TestCategoryItem)
                                    {
                                        TestCategoryTypeTestCategoryItem testCategoryTypeTestCategoryItem = item as TestCategoryTypeTestCategoryItem;
                                        if (testCategoryTypeTestCategoryItem != null)
                                        {
                                            string category = testCategoryTypeTestCategoryItem.TestCategory;
                                            idToCategory.Add(id, category);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

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
                            //Console.WriteLine("entering result for test " + testName + " in resultsDB for results file " + trxFileName);

                            if (idToCategory.ContainsKey(unitTestResultType.testId))
                            {
                                workflow = idToCategory[unitTestResultType.testId];
                                if (workflow.Contains(":"))
                                {
                                    workflow = workflow.Substring(0, workflow.IndexOf(":"));
                                }
                            }

                            string className = " ";
                            if (idToClassName.ContainsKey(unitTestResultType.testId))
                            {
                                className = idToClassName[unitTestResultType.testId];
                                if (className.Contains("msIXY.Tests."))
                                {
                                    className = className.Replace("msIXY.Tests.", "");
                                }
                            }

                            DateTime startTime = new DateTime(2000, 1, 1);
                            DateTime endTime = new DateTime(2000, 1, 1);

                            if (unitTestResultType.startTime != null)
                            {
                                startTime = Convert.ToDateTime(unitTestResultType.startTime, System.Globalization.CultureInfo.InvariantCulture);
                            }

                            if (unitTestResultType.endTime != null)
                            {
                                endTime = Convert.ToDateTime(unitTestResultType.endTime, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            string trxFileDate = trxFileName.Substring(0, trxFileName.LastIndexOf("."));
                            DateTime trxFileDateTime = Convert.ToDateTime(trxFileDate.Substring(trxFileDate.LastIndexOf("_") + 1), System.Globalization.CultureInfo.InvariantCulture);
                            DateTime executionDate = trxFileDateTime.Date;

                            string outcome = unitTestResultType.outcome;
                            bool isAborted = unitTestResultType.isAborted;
                            string duration = unitTestResultType.duration;

                            // Getting the error message out of Test Schema in xml.If there is no error message then make the errormessage as empty string
                            try
                            {
                                errorMessage = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.Message))[0].Value;
                            }
                            catch (Exception)
                            {
                                errorMessage = " ";
                            }
                            // Getting the error stack trace out of Test Schema in xml.If there is no errorstack trace then make the errorstacktrace as empty string
                            try
                            {
                                errorStackTrace = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.StackTrace))[0].Value;
                            }
                            catch (Exception)
                            {
                                errorStackTrace = " ";
                            }

                            //Handling the null condition for duration 
                            if (duration != null)
                            {
                                // Conversion of duration value from  trx file to integer value in milliseconds using timespan
                                TimeSpan span = TimeSpan.Parse(duration);
                                durationSql = (span.Hours) * 3600000 + (span.Minutes) * 60000 + (span.Seconds) * 1000 + span.Milliseconds;
                            }
                            else if (duration == null)
                            {
                                durationSql = durationSql.GetValueOrDefault(0);
                            }

                            // Calulate testresult bit to be inserted into testresult column depending on outcome field of test in trx file
                            int testResult;
                            if (outcome.Equals("Passed"))
                            {
                                testResult = 0;
                            }
                            else
                            {
                                testResult = 1;
                            }

                            // Parse release name for EMS from trxfile name. e.g if filename is msixy_Build_0.0.0.683_Time_164715234_Date_2018-01-31.trx the below code will parse the file and releaseName will be 0.0.0.683
                            string releaseName = " ";
                            if (trxFileName.Contains("_Build_") && trxFileName.Contains("_Time_"))
                            {
                                var start = trxFileName.IndexOf("_Build_") + "_Build_".Length;
                                var end = trxFileName.IndexOf("_Time_");
                                releaseName = trxFileName.Substring(start, end - start);
                            }

                            //Connection to SQL and parametrize the sql query 
                            SqlCommand insertTestResult = new SqlCommand();
                            string cmdInsertTestResult =
                                "INSERT INTO ems_result_fields " +
                                "(TestName, Outcome, TestResult, StartTime, EndTime, IsAborted, Duration, ClassName, Workflow, ErrorMessage, ErrorStackTrace, " +
                                "fileName, resultfileid, ExecutionDate, ReleaseName) " +
                                "Values (@testName, @outcome, @testresult, @starttime, @endtime, @isAborted, @duration, @className, @workflow, @errorMessage, " +
                                "@errorStackTrace, @filename, @resultfileid, @executionDate, @releaseName)";
                            insertTestResult.Connection = sqlc;
                            insertTestResult.CommandText = cmdInsertTestResult;
                            insertTestResult.Parameters.AddWithValue("@testName", testName);
                            insertTestResult.Parameters.AddWithValue("@outcome", outcome);
                            insertTestResult.Parameters.AddWithValue("@testresult", testResult);
                            insertTestResult.Parameters.AddWithValue("@starttime", startTime);
                            insertTestResult.Parameters.AddWithValue("@endtime", endTime);
                            insertTestResult.Parameters.AddWithValue("@isAborted", isAborted);
                            insertTestResult.Parameters.AddWithValue("@duration", durationSql);
                            insertTestResult.Parameters.AddWithValue("@className", className);
                            insertTestResult.Parameters.AddWithValue("@workflow", workflow);
                            insertTestResult.Parameters.AddWithValue("@errorMessage", errorMessage);
                            insertTestResult.Parameters.AddWithValue("@errorStackTrace", errorStackTrace);
                            insertTestResult.Parameters.AddWithValue("@filename", trxFileName);
                            insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                            insertTestResult.Parameters.AddWithValue("@executionDate", executionDate);
                            insertTestResult.Parameters.AddWithValue("@releaseName", releaseName);
                            try
                            {
                                insertTestResult.ExecuteNonQuery();
                                //Console.WriteLine("Result for test " + testName + " entered in DB for results file " + trxFileName);
                            }
                            catch(Exception)
                            {
                                Console.WriteLine("Failed to enter results for the test " + testName + " in results file " + trxFileName);
                            }

                        }
                    }
                }
            }
        }


        // Function to insert passed test results on rerun in to failed tests table
        private static void InsertOmsTestDataRerun(SqlConnection sqlc, int resultFileId, string fullPath, string trxFileName, TestRunType testRunType)
        {
            string componentName = " ";
            string errorMessage = " ";           
            int errorClass = 1;              // initialize errorclass variable with value 1 which is equal to environment issue (assuming everything is environment issue for test which passed on rerun)
            Dictionary<string, string> idToClassName = new Dictionary<string, string>();

            //LOOP FOR CLASSNAME AND TEST ID
            foreach (object itob1 in testRunType.Items)
            {
                TestRunTypeTestDefinitions testRunTypeTestDefinitions = itob1 as TestRunTypeTestDefinitions;

                if (testRunTypeTestDefinitions != null)
                {
                    foreach (object itob3 in testRunTypeTestDefinitions.Items)
                    {
                        UnitTestType unitTestType = itob3 as UnitTestType;
                        if (unitTestType != null)
                        {
                            string id = unitTestType.id;

                            UnitTestTypeTestMethod unitTestTypeTestMethod = unitTestType.TestMethod as UnitTestTypeTestMethod;
                            if (unitTestTypeTestMethod != null)
                            {
                                string className = unitTestTypeTestMethod.className.Split(',')[0];
                                idToClassName.Add(id, className);
                            }
                        }
                    }
                }
            }

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
                            string testName = unitTestResultType.testName;
                            if (idToClassName.ContainsKey(unitTestResultType.testId))
                            {
                                componentName = idToClassName[unitTestResultType.testId];
                            }
                            if (componentName == " ")
                            {
                                Console.WriteLine("Unable to find class/component name for test.");
                            }
                            //string componentName = comutils.ClassName(testName, fullPath);
                            string outcome = unitTestResultType.outcome;

                            DateTime startTime = new DateTime(2000, 1, 1);
                         

                            if (unitTestResultType.startTime != null)
                            {
                                startTime = Convert.ToDateTime(unitTestResultType.startTime, System.Globalization.CultureInfo.InvariantCulture);
                            }          

                            DateTime executionDate = startTime.Date;                      

                            // Getting the error message out of Test Schema in xml.If there is no error message then make the errormessage as empty string
                            try
                            {
                                errorMessage = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.Message))[0].Value;
                            }
                            catch (Exception)
                            {
                                errorMessage = " ";
                            }

                            // Parse className column to get the component value to be added to column component
                            string tempComponent = componentName.Replace("Eze.Integration.Functional.Test.", "");
                            string component = tempComponent.Substring(0, tempComponent.IndexOf('.'));
                            string triageComment = "Passed on Rerun";
                            string jiraid = "";
                            if (outcome.Equals("Passed"))
                            {
                                //Connection to SQL and parametrize the sql query 
                                SqlCommand insertTestResult = new SqlCommand();
                                string cmdInsertTestResult = "INSERT INTO failed_tests (resultfileid,testName,ExecutionDate,component,triageComment,jiraid,errorMessage,errorClass) Values (@resultfileid, @testName, @executionDate, @component, @triagecomment, @jiraid, @errorMessage, @errorClass)";
                                insertTestResult.Connection = sqlc;
                                insertTestResult.CommandText = cmdInsertTestResult;
                                insertTestResult.Parameters.AddWithValue("@resultfileid", resultFileId);
                                insertTestResult.Parameters.AddWithValue("@testName", testName);
                                insertTestResult.Parameters.AddWithValue("@executionDate", executionDate);
                                insertTestResult.Parameters.AddWithValue("@component", component);
                                insertTestResult.Parameters.AddWithValue("@triageComment", triageComment);
                                insertTestResult.Parameters.AddWithValue("@jiraid", jiraid);
                                insertTestResult.Parameters.AddWithValue("@errorMessage", errorMessage);
                                insertTestResult.Parameters.AddWithValue("@errorClass", errorClass);
                               
                                try
                                {
                                    insertTestResult.ExecuteNonQuery();
                                }
                                catch (SqlException e)
                                {
                                    Console.WriteLine("Failed to insert results into the QATDR database for the test " + testName + " in results file " + trxFileName);
                                    Console.WriteLine(e.Message);
                                }

                            }
                        }
                    }
                }
            }
        }



                                 
        private static void InsertOmsTestData(SqlConnection sqlc, int resultFileId, string fullPath, string trxFileName, TestRunType testRunType)
        {
            string componentName = " ";
            string errorMessage = " ";
            string errorStackTrace = " ";
            int errorClass = 0;              // initialize errorclass variable with value 0 which is equal to product issue (assuming everything is product issue from start)
            Dictionary<string, string> idToClassName = new Dictionary<string, string>();

            //LOOP FOR CLASSNAME AND TEST ID
            foreach (object itob1 in testRunType.Items)
            {
                TestRunTypeTestDefinitions testRunTypeTestDefinitions = itob1 as TestRunTypeTestDefinitions;

                if (testRunTypeTestDefinitions != null)
                {
                    foreach (object itob3 in testRunTypeTestDefinitions.Items)
                    {
                        UnitTestType unitTestType = itob3 as UnitTestType;
                        if (unitTestType != null)
                        {
                            string id = unitTestType.id;

                            UnitTestTypeTestMethod unitTestTypeTestMethod = unitTestType.TestMethod as UnitTestTypeTestMethod;
                            if (unitTestTypeTestMethod != null)
                            {
                                string className = unitTestTypeTestMethod.className.Split(',')[0];
                                idToClassName.Add(id, className);
                            }
                        }
                    }
                }
            }

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
                            if (idToClassName.ContainsKey(unitTestResultType.testId))
                            {
                                componentName = idToClassName[unitTestResultType.testId];
                            }
                            if (componentName == " ")
                            {
                                Console.WriteLine("Unable to find class/component name for test.");
                            }
                            //string componentName = comutils.ClassName(testName, fullPath);
                            string outcome = unitTestResultType.outcome;

                            DateTime startTime = new DateTime(2000, 1, 1);
                            DateTime endTime = new DateTime(2000, 1, 1);

                            if (unitTestResultType.startTime != null)
                            {
                                startTime = Convert.ToDateTime(unitTestResultType.startTime, System.Globalization.CultureInfo.InvariantCulture);
                            }

                            if (unitTestResultType.endTime != null)
                            {
                                endTime = Convert.ToDateTime(unitTestResultType.endTime, System.Globalization.CultureInfo.InvariantCulture);
                            }

                            DateTime executionDate = startTime.Date;

                            bool isAborted = unitTestResultType.isAborted;
                            string duration = unitTestResultType.duration;

                            // Getting the error message out of Test Schema in xml.If there is no error message then make the errormessage as empty string
                            try
                            {
                                errorMessage = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.Message))[0].Value;
                            }
                            catch (Exception)
                            {
                                errorMessage = " ";
                            }
                            // Getting the error stack trace out of Test Schema in xml.If there is no errorstack trace then make the errorstacktrace as empty string
                            try
                            {
                                errorStackTrace = ((System.Xml.XmlNode[])(((OutputType)(((TestResultType)(unitTestResultType)).Items[0])).ErrorInfo.StackTrace))[0].Value;
                            }
                            catch (Exception)
                            {
                                errorStackTrace = " ";
                            }

                            //Handling the null condition for duration 
                            if (duration != null)
                            {
                                // Conversion of duration value from  trx file to integer value in milliseconds using timespan
                                TimeSpan span = TimeSpan.Parse(duration);
                                durationSql = (span.Hours) * 3600000 + (span.Minutes) * 60000 + (span.Seconds) * 1000 + span.Milliseconds;
                            }
                            else if (duration == null)
                            {
                                durationSql = durationSql.GetValueOrDefault(0);
                            }

                            // Calulate testresult bit to be inserted into testresult column depending on outcome field of test in trx file
                            int testResult = 0;
                            if (outcome.Equals("Passed"))
                            {
                                testResult = 0;
                            }
                            else if (outcome.Equals("Failed"))
                            {
                                testResult = 1;
                            }
                            else if (outcome.Equals("NotExecuted"))
                            {
                                testResult = 2;
                            }

                            // Parse className column to get the component value to be added to column component
                            string tempComponent = componentName.Replace("Eze.Integration.Functional.Test.", "");
                            string component = tempComponent.Substring(0, tempComponent.IndexOf('.'));

                            // Parse release name for OMS from trxfile name. e.g if filename is Build_19.6.0.109_Date_2019-07-10.trx the below code will parse the file and releaseName will be 19.6
                            string releaseName = " ";
                            if (trxFileName.Contains("Build_"))
                            {
                                string tempReleaseName = trxFileName.Replace("Build_", "");
                                {
                                    releaseName = tempReleaseName.Substring(0, tempReleaseName.IndexOf('.') + 2);
                                }
                            }

                            //Connection to SQL and parametrize the sql query 
                            SqlCommand insertTestResult = new SqlCommand();
                            string cmdInsertTestResult = "INSERT INTO result_fields (testName,outcome,testresult,startTime,endTime,isAborted,duration,className,component,errorMessage,errorStackTrace,filename,resultfileid,executionDate,releaseName) Values (@testName, @outcome, @testresult, @starttime, @endtime, @isAborted, @duration, @className, @component, @errorMessage, @errorStackTrace, @filename, @resultfileid, @executionDate, @releaseName)";
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
                            }
                            catch (SqlException e)
                            {
                                Console.WriteLine("Failed to insert results into the QATDR database for the test " + testName + " in results file " + trxFileName);
                                Console.WriteLine(e.Message);
                            }

                            // If the test failed, update the predictedErrorClass column in the result_fields table for the given test
                            if (outcome.Equals("Failed"))
                            {   
                                try
                                {
                                   Console.WriteLine("Failed testname is  " + testName );
                                    Common comutil = new Common();
                                    string workingDirectoryName = new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName;
                                    //Change parameters.xml file name  depending on your environment configuration so that appropriate variables are defined in that file and you dont get run time error
                                    var pathFile = Path.Combine(workingDirectoryName, "parameters.xml");

                                    //Run the py_predict_errorclass store procedure to get the predicted errorclass integer value
                                    SqlCommand getErrorClass = new SqlCommand("py_predict_errorclass", sqlc);
                                    getErrorClass.CommandType = CommandType.StoredProcedure;
                                    getErrorClass.CommandTimeout = 90;
                                    getErrorClass.Parameters.AddWithValue("@model","MultinomialNB");
                                    
                                    SqlDataReader reader = getErrorClass.ExecuteReader();
                                    if (reader.HasRows)
                                    {
                                        while (reader.Read())
                                        {
                                                errorClass = Convert.ToInt32(reader["errorClass"]);
                                        }
                                    }
                                    reader.Close();
                                    
                                    // Get the errorclassdescription from errorclassmap table for errorclass integer derived through py_predict_errorclass SP
                                    SqlCommand getErrorClassDescription = new SqlCommand();
                                    string cmdGetErrorClassDescription = "SELECT errorclassdescription FROM errorclassmap WHERE errorclass=@ec";
                                    getErrorClassDescription.Connection = sqlc;
                                    getErrorClassDescription.CommandTimeout = 60;
                                    getErrorClassDescription.CommandText = cmdGetErrorClassDescription;
                                    getErrorClassDescription.Parameters.AddWithValue("@ec", errorClass);
                                    string errorClassDescription = (string)getErrorClassDescription.ExecuteScalar();

                                    // Get the jiraid associated with test failure
                                    SqlCommand getJiraID = new SqlCommand();
                                    string cmdGetJiraID = "SELECT jiraid FROM failed_tests WHERE testName=@tn ORDER BY ExecutionDate DESC";
                                    getJiraID.Connection = sqlc;
                                    getJiraID.CommandText = cmdGetJiraID;
                                    getJiraID.Parameters.AddWithValue("@tn", testName);
                                    string jiraId = (string)getJiraID.ExecuteScalar();
                                    string jiraIdWithStatus = "";
                                    if ((jiraId == null) || (jiraId == ""))
                                    {
                                       jiraIdWithStatus = null;
                                    }
                                    else
                                    {
                                       string jiraUsername = comutil.JiraParam(pathFile, "Jirausername");
                                       string jiraPassword = comutil.JiraParam(pathFile, "Jirapassword");
                                       //Putting try catch block in case there is bad jiraid associated in failed tests table of qatdr resulting in exception for jirastatus
                                       try
                                       {
                                          string jiraStatus = comutil.getJiraStatus(jiraId, jiraUsername, jiraPassword);
                                          jiraIdWithStatus = jiraId + " | " + jiraStatus;
                                       }
                                       catch
                                       {
                                          jiraIdWithStatus = null;
                                       }                                     
                                    }

                                    // Update result_fields table with errorclassdescription
                                    SqlCommand updatePredict = new SqlCommand();
                                    string cmdUpdatePredict = "UPDATE result_fields SET predictedErrorClass = @erp, lastJiraId = @jira WHERE testName = @tn AND resultfileid = @rf";
                                    updatePredict.Connection = sqlc;
                                    updatePredict.CommandTimeout = 60;
                                    updatePredict.CommandText = cmdUpdatePredict;
                                    updatePredict.Parameters.AddWithValue("@erp", errorClassDescription);
                                    if (jiraIdWithStatus != null)
                                    {
                                       updatePredict.Parameters.AddWithValue("@jira", jiraIdWithStatus);
                                    }
                                    else
                                    {
                                       updatePredict.Parameters.AddWithValue("@jira", "No Jira");
                                    }
                                    updatePredict.Parameters.AddWithValue("@tn", testName);
                                    updatePredict.Parameters.AddWithValue("@rf", resultFileId);
                                    updatePredict.ExecuteNonQuery();
                                }
                                catch (SqlException e)
                                {
                                    Console.WriteLine("SQL Exception: Failed to update the predictedErrorClass for " + testName + " in results file " + trxFileName);
                                    Console.WriteLine(e.Message);
                                    Console.WriteLine(e.StackTrace);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("General Exception: Failed to update the predictedErrorClass for " + testName + " in results file " + trxFileName);
                                    Console.WriteLine(e.Message);
                                    Console.WriteLine(e.StackTrace);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
