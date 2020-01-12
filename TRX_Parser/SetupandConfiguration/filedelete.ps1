#Create Logfile location to log the files deleted.You should have logs directory created before running this script
$Logfile = "C:\temp\logs\filedelete.log"

Function LogWrite
{
   Param ([string]$logstring)

   Add-content $Logfile -value $logstring
}
#Network path where trx files are located.Change the location as per your env configuration
$filepath = "\\ny4synology01\QA-Long-Term\trxfiles"
$filetype = "*.trx"
$file_count = [System.IO.Directory]::GetFiles("$filepath", "$filetype").Count
# if filecount is greated than 30 , files will be deleted.You can change this variable if you want to delete the files less or greater than this count
If($file_count -gt 30) {
  LogWrite "-------------------------------------------------------------------------------" 
  LogWrite "deleting $($file_count) files on $(Get-Date -DisplayHint Date)"
  
  foreach($file in Get-ChildItem $filepath)
  {
    LogWrite $file.name
  }
  get-childitem $filepath -include *.trx -recurse |  foreach ($_) {remove-item $_.fullname}
  LogWrite "-------------------------------------------------------------------------------" 
}