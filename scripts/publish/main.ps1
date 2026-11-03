#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('All','Prerequisites','SigningRepository','Keystore','Metadata','Backup','Publish','Release')][string]$Step='All')
$ErrorActionPreference='Stop';$script:Ctx=@{}
try{
 [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo('en-US')
 [Threading.Thread]::CurrentThread.CurrentUICulture=[Globalization.CultureInfo]::GetCultureInfo('en-US')
 $PSDefaultParameterValues['Out-File:Encoding']='utf8'
 foreach($f in @('common.ps1','00-prerequisites.ps1','01-signing-repository.ps1','02-keystore.ps1','03-metadata.ps1','04-backup.ps1','05-publish.ps1','06-release.ps1')){. (Join-Path $PSScriptRoot $f)}
 Section 'MemoAna Android Publishing';Log "Execution mode: $Step"
 if($Step -in @('All','Prerequisites')){StepPrerequisites}
 if($Step -in @('All','SigningRepository')){StepSigningRepository}elseif($Step -ne 'Prerequisites'){$root=SigningRoot;if(-not $root){throw 'Signing root not configured. Run -Step SigningRepository first.'};$script:Ctx.Paths=Paths $root}
 switch($Step){'All'{StepKeystore;StepMetadata;StepBackup;StepPublish;StepRelease};'Prerequisites'{};'SigningRepository'{};'Keystore'{StepKeystore};'Metadata'{StepMetadata};'Backup'{StepBackup};'Publish'{StepPublish};'Release'{StepRelease}}
}catch{if(Get-Command Log -ErrorAction SilentlyContinue){Log $_.Exception.Message Error}else{Write-Error $_};exit 1}
