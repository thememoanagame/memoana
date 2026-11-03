function StepMetadata{if(-not $script:Ctx.Paths){throw 'SigningRepository required.'};$p=$script:Ctx.Paths;$s=$script:Ctx.Signing;if(-not $s){$m=ReadMetadata $p.MetadataPath;$s=@{Alias=$m['Keystore Alias'];StorePassword=$m['Store Password'];KeyPassword=$m['Key Password'];KeystorePath=$m['Upload Keystore'];Subject=$m['Certificate Subject']}};$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($s.StorePassword)));@"
MemoAna Android Publishing Information
======================================
Generated:
$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Application:
$($script:Cfg.App)
Application ID:
$($script:Cfg.AppId)
Target Framework:
$($script:Cfg.Tfm)
Configuration:
$($script:Cfg.Config)
Upload Keystore:
$($s.KeystorePath)
Keystore Alias:
$($s.Alias)
Store Password:
$($s.StorePassword)
Key Password:
$($s.KeyPassword)
Certificate Subject:
$($s.Subject)
Store Password SHA-256:
$hash
IMPORTANT
---------
This file contains sensitive signing credentials. Do NOT commit it to Git or upload it publicly.
"@|Set-Content -LiteralPath $p.MetadataPath -Encoding utf8;$script:Ctx.Signing=$s;Log "Metadata saved: $($p.MetadataPath)" Success}
