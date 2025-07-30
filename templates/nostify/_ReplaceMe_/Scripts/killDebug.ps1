
$connArray = Get-NetTCPConnection -LocalPort <ThePortNumber> -ErrorAction SilentlyContinue
if ($connArray.Length -gt 0) {
     $processNumbersArray = $connArray.OwningProcess
     if ($processNumbersArray.Count -gt 0) {
          foreach ($p in $processNumbersArray | Where-Object {$_ -ne 0}) { Stop-Process -f -ID $p }
     }
}
