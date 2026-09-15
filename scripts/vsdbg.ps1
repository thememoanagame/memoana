Invoke-WebRequest -Uri https://aka.ms/getvsdbgsh -OutFile Get-vsdbg.ps1
.\Get-vsdbg.ps1 -Version latest -Runtime linux-x64 -Location "${env:USERPROFILE}\.vsdbg"