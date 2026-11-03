function StepRelease{if(-not $script:Ctx.Paths){throw 'SigningRepository required.'};$p=$script:Ctx.Paths;$out=Join-Path $script:Cfg.Root "src\MemoAna\bin\$($script:Cfg.Config)\$($script:Cfg.Tfm)";if(-not(Test-Path -LiteralPath $out -PathType Container)){throw "Output directory not found: $out"};$aab=Get-ChildItem -LiteralPath $out -Filter '*.aab' -Recurse -File|Sort-Object LastWriteTime -Descending|Select-Object -First 1;$apk=Get-ChildItem -LiteralPath $out -Filter '*.apk' -Recurse -File|Sort-Object LastWriteTime -Descending|Select-Object -First 1;if(-not $aab){throw "No AAB found under $out"};$v=ProjectProperty 'ApplicationDisplayVersion';$c=ProjectProperty 'ApplicationVersion';if(-not $v){$v='unknown'};if(-not $c){$c='unknown'};$dest=Join-Path $p.ReleasesDir "$v-$c";EnsureDir $dest;Copy-Item -LiteralPath $aab.FullName -Destination (Join-Path $dest $aab.Name) -Force;if($apk){Copy-Item -LiteralPath $apk.FullName -Destination (Join-Path $dest $apk.Name) -Force};if(Test-Path -LiteralPath $p.MetadataPath -PathType Leaf){Copy-Item -LiteralPath $p.MetadataPath -Destination (Join-Path $dest $script:Cfg.Metadata) -Force};@"
MemoAna Android Release
=======================
Application: $($script:Cfg.App)
Application ID: $($script:Cfg.AppId)
Version: $v
Version Code: $c
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
AAB: $($aab.Name)
APK: $(if($apk){$apk.Name}else{'Not generated'})
Purpose: AAB is intended for Google Play Console. APK is retained for testing.
"@|Set-Content -LiteralPath (Join-Path $dest 'release-info.txt') -Encoding utf8;Section 'Publishing assets';Write-Host "Keystore: $($p.KeystorePath)";Write-Host "Metadata: $($p.MetadataPath)";if($script:Ctx.BackupPath){Write-Host "Backup: $($script:Ctx.BackupPath)"};Write-Host "AAB: $($aab.FullName)";if($apk){Write-Host "APK: $($apk.FullName)"};Write-Host "Release: $dest";Log 'Android publishing completed.' Success}
