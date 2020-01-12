using System;
using System.Data.SqlClient;
using System.Xml;


namespace CommonUtils
{
	/// <summary>
	/// This class will be used to generate common functions to be used across solution in other projects
	/// </summary>
    public class Common
    {
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
		 
	   
	    //This function is being used to extract the requested parameter out of xml file
	    public string DbParam(string filename, string requestedparam)
	    {
		    string Param = " ";
		    XmlDocument doc = new XmlDocument();
		    doc.Load(filename);
		    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
		    {
			    if (node.Name.Equals("DB"))
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

	    //This function is being used to extract classname of a test from results trx file 
	    public string ClassName(string testname, string filename)
	    {
		    string ClassName = " ";
		    XmlDocument doc = new XmlDocument();
		    doc.Load(filename);
		    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
		    {
			    if (node.Name.Equals("TestDefinitions"))
			    {
				    foreach (XmlNode node1ChildNode in node.ChildNodes)
				    {
					    string classnameattribute = node1ChildNode.LastChild.Attributes["className"].InnerText;
					    string testnameattribute = node1ChildNode.LastChild.Attributes["name"].InnerText;
					    if (testname.Equals(testnameattribute))
					    {
						    ClassName = classnameattribute;
					    }
				    }
			    }
		    }
			 return ClassName;
	    }
    }
}
