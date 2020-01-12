#Please change parameters in file as per your environment config
Import-Module SQLPS  # Importing SQLPS module .This module should be in your "C:\Windows\System32\WindowsPowerShell\v1.0\Modules" directory
$Server = 'localhost' # SQLserver name where DB resides
$User = 'sa'  # DB user which have DBowner rights
$password = 'redsox' #Password of the Account which have DBowner rights
$Database = 'AQAresults' #DB name to be backedup
$FilePath = "D:\Database\aqaresults_$(Get-Date -UFormat %Y%m%d%H%M).bak" # File location where .bak file will be placed with addition of Date,hour and minute will be added to filename
$Query = "BACKUP DATABASE $Database TO DISK = '$FilePath'" # Query to do the DB backup
Invoke-SqlCmd -ServerInstance $Server -Database $Database -Query $Query -Username $User -Password $password  # Using Invoke-SqlCmd cmdlet to do the backup of sql DB