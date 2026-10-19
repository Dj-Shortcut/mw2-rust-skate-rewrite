param([Parameter(Mandatory=$true)][string]$Candidate,[switch]$Apply)
$ErrorActionPreference='Stop'
$probe=Join-Path $HOME 'Downloads\claude-loader-probe'
$head='58f82909300f9b973b15b2031ddc397b4b79d1c6'
$oldDll='840733f909ec53008bb247b4b60c590e026d87467ebfddbd39cf85910d4eca4a'
$inputDigest='d7c676514f634ef78c3b7cb7df466a4735945d8bada2d75e6ff1ca32f811cd6b'
$play=Join-Path $probe 'play';$launcher=Join-Path $probe 'launcher'
function sha($p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function hashText($s){$h=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).Replace('-','').ToLowerInvariant()}finally{$h.Dispose()}}
function offline{if(Get-Process RustClient,Rust -ErrorAction SilentlyContinue){throw 'Rust is running; no installation is allowed'}}
function plain($p){$f=Get-Item -LiteralPath $p;if(($f.Attributes-band[IO.FileAttributes]::ReparsePoint)-ne0){throw 'A reviewed path is a reparse point'};$f}
function stepAssignment($text,$value){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'Launcher PowerShell parse failed'}
 $a=@($ast.FindAll({param($n)$n-is[Management.Automation.Language.AssignmentStatementAst]-and$n.Left.Extent.Text-ceq'$global:psteps'},$true))
 if($a.Count-ne1-or$a[0].Right.Extent.Text-cne("'"+$value+"'")){throw 'Launcher does not have the reviewed single psteps assignment'}
 $a[0]
}
function privateDirectory($path){
 [IO.Directory]::CreateDirectory($path)|Out-Null
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
 $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false);$acl.SetOwner($sid)
 foreach($who in @($sid,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){
  $rule=[Security.AccessControl.FileSystemAccessRule]::new($who,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
  $null=$acl.AddAccessRule($rule)
 }
 Set-Acl -LiteralPath $path -AclObject $acl
 $check=Get-Acl -LiteralPath $path
 if(!$check.AreAccessRulesProtected){throw 'Private backup ACL inheritance was not disabled'}
 foreach($rule in $check.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
  if($rule.AccessControlType-eq[Security.AccessControl.AccessControlType]::Allow-and$rule.IdentityReference.Value-notin@($sid.Value,'S-1-5-18')){throw 'Backup ACL allows an unexpected identity'}
 }
}
offline
$null=plain $probe;$null=plain $play;$null=plain $launcher
$Candidate=[IO.Path]::GetFullPath($Candidate).TrimEnd('\')
$prefix=(Join-Path $probe 'candidates\client029-58f8290-')
if(!$Candidate.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Candidate is outside the reviewed .29 staging prefix'}
$null=plain $Candidate;$out=Join-Path $Candidate 'output';$null=plain $out
$receiptPath=Join-Path $Candidate 'build.json';$null=plain $receiptPath
$receiptHash=sha $receiptPath;$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
if($receipt.head-cne$head-or$receipt.version-cne'0.18.29'-or!($receipt.compiled-is[bool])-or!$receipt.compiled-or$receipt.compilerExit-ne0-or$receipt.errors-ne0-or$receipt.sources-ne38-or$receipt.references-ne356-or$receipt.interop-ne191-or$receipt.generationFiles-ne193-or($receipt.sidecars-join'|')-cne'MethodAddressToToken.db|MethodXrefScanCache.db'-or$receipt.steamBuild-cne'25903346'){throw 'The exact current Windows compile receipt did not pass'}
if([IO.Path]::GetFullPath($receipt.candidate).TrimEnd('\')-ine$Candidate){throw 'Compile receipt belongs to another candidate'}
$steam='C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf'
$steamHash=sha $steam;$build=[regex]::Match([IO.File]::ReadAllText($steam),'"buildid"\s+"([0-9]+)"').Groups[1].Value
if($build-cne'25903346'){throw 'Installed Rust build changed'}
$gen=Join-Path $probe 'gen-25903346\out';$core=Join-Path $probe 'bep788\BepInEx\core';$fx=Join-Path $probe 'bep788\dotnet'
if(!(Select-String -Path (Join-Path $probe 'gen-25903346\gen.log') -Pattern 'GEN DONE.* errors=0( |$)' -Quiet)){throw 'No successful matching generation receipt'}
$genFiles=@(Get-ChildItem -LiteralPath $gen -File -Force)
$interop=@($genFiles|Where-Object Extension -EQ '.dll')
$sidecars=@($genFiles|Where-Object Extension -NE '.dll'|Sort-Object Name)
if($genFiles.Count-ne193-or$interop.Count-ne191-or($sidecars.Name-join'|')-cne'MethodAddressToToken.db|MethodXrefScanCache.db'){throw 'Current generation must contain exactly 191 DLLs and the two known DB sidecars (193 files)'}
$references=@(Get-ChildItem -LiteralPath $fx -File -Filter *.dll|Where-Object{($_.Name-like'System*'-or$_.Name-in'mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll')-and$_.Name-notlike'*.Native.dll'}|ForEach-Object FullName)
$references+=@('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','BepInEx.Unity.Common.dll','Il2CppInterop.Runtime.dll','Il2CppInterop.Common.dll'|ForEach-Object{Join-Path $core $_})
$references+=@($interop|ForEach-Object FullName)
$allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($p in $references){if(!$allowed.Add([IO.Path]::GetFullPath($p))){throw 'Duplicate native reference'}}
if($allowed.Count-ne356-or@($receipt.referencePins).Count-ne356){throw 'Native reference inventory differs from the compile'}
foreach($pin in $receipt.referencePins){$p=[IO.Path]::GetFullPath($pin.path);if(!$allowed.Remove($p)-or(sha $p)-cne$pin.sha256){throw 'A native reference changed after compilation'}}
if($allowed.Count){throw 'Compile receipt missed native references'}
if(@($receipt.inputs).Count-ne48){throw 'Expected 48 independently pinned build inputs'}
[string[]]$ledger=@($receipt.inputs|ForEach-Object{$_.path+'|'+$_.commit+'|'+$_.sha256})
[Array]::Sort($ledger,[StringComparer]::Ordinal)
if((hashText (([string]::Join("`n",$ledger))+"`n"))-cne$inputDigest){throw 'Build input pins differ from the reviewed source'}
$modelPins=@{};$sourceCount=0
foreach($pin in $receipt.inputs){
 $leaf=[IO.Path]::GetFileName($pin.path)
 if($pin.path-match'^mods/rust/(client/src|shared)/[^/]+\.cs$'){$file=Join-Path $Candidate ('src\'+$leaf);$sourceCount++}
 elseif($pin.path-match'^assets/authored/[^/]+\.glb$'){$file=Join-Path $out $leaf;$modelPins[$leaf]=$pin.sha256}
 elseif($pin.path-in@('mods/rust/client/sources.txt','mods/rust/client/models.txt')){$file=Join-Path $Candidate $leaf}
 else{throw 'Build input path is outside the reviewed scope'}
 $null=plain $file;if((sha $file)-cne$pin.sha256){throw 'A pinned candidate input changed'}
}
if($sourceCount-ne38-or$modelPins.Count-ne8){throw 'Candidate source/model inventory differs'}
$allowedOutput=@('ShortcutSkateClient.dll')+@($modelPins.Keys)
foreach($folder in @($play,$out)){
 $files=@(Get-ChildItem -LiteralPath $folder -Force)
 if($files.Count-ne9-or@($files|Where-Object{$_.PSIsContainer-or$_.Name-notin$allowedOutput}).Count){throw 'Play/output folder is not exactly the DLL and eight known models'}
 foreach($f in $files){$null=plain $f.FullName}
}
$modelNames=@($modelPins.Keys|Sort-Object);$builtModels=@($receipt.models|Sort-Object)
if(($modelNames-join'|')-cne($builtModels-join'|')){throw 'Compile model receipt differs'}
$dll=Join-Path $out 'ShortcutSkateClient.dll';$dllHash=sha $dll
if($dllHash-cne$receipt.dllSha256-or(Get-Item $dll).Length-ne$receipt.dllBytes-or$dllHash-ceq$oldDll){throw 'Candidate DLL hash/size does not match the successful compile'}
$current=Join-Path $play 'ShortcutSkateClient.dll'
if((sha $current)-cne$oldDll-or(Get-Item $current).Length-ne416768){throw 'Installed client is not the verified .27 DLL'}
$launch=Join-Path $launcher 'skate.ps1';$version=Join-Path $launcher 'version.txt';$null=plain $launch;$null=plain $version
$launchText=[IO.File]::ReadAllText($launch);$assignment=stepAssignment $launchText 'skate'
$right=$assignment.Right.Extent
$newLaunch=$launchText.Substring(0,$right.StartOffset)+"'physicsdiag,skate'"+$launchText.Substring($right.EndOffset)
$null=stepAssignment $newLaunch 'physicsdiag,skate'
$changes=@(@{relative='play\ShortcutSkateClient.dll';target=$current;source=$dll;old=$oldDll;new=$dllHash},@{relative='launcher\skate.ps1';target=$launch;text=$newLaunch;old=(sha $launch)},@{relative='launcher\version.txt';target=$version;text=('Skate candidate 0.18.29 (58f8290), Rust build 25903346, physics diagnostics enabled'+"`r`n");old=(sha $version)})
foreach($name in $modelPins.Keys){$target=Join-Path $play $name;$old=sha $target;if($old-cne$modelPins[$name]){$changes+=@{relative=('play\'+$name);target=$target;source=(Join-Path $out $name);old=$old;new=$modelPins[$name]}}}
$summary=@{head=$head;candidate=$Candidate;oldDllSha256=$oldDll;newDllSha256=$dllHash;models=8;sources=38;references=356;interop=191;generationFiles=193;sidecars=@($sidecars.Name);compilerWarnings=$receipt.warnings;changes=@($changes|ForEach-Object{$_.relative});launcherStepBefore='skate';launcherStepAfter='physicsdiag,skate';ready=$true;applied=$false;backup=$null}
if(!$Apply){$summary|ConvertTo-Json -Depth 5;return}
offline
if((sha $current)-cne$oldDll-or(sha $receiptPath)-cne$receiptHash-or(sha $steam)-cne$steamHash){throw 'Preflight state changed before the backup'}
$backup=Join-Path $probe ('private-backups\client029-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
if(Test-Path (Join-Path $probe 'private-backups')){$null=plain (Join-Path $probe 'private-backups')}
privateDirectory $backup
$utf8=[Text.UTF8Encoding]::new($true)
foreach($entry in $changes){
 if((sha $entry.target)-cne$entry.old){throw 'A target changed before backup'}
 $save=Join-Path $backup ('original\'+$entry.relative);$stage=Join-Path $backup ('staged\'+$entry.relative)
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($save))|Out-Null
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stage))|Out-Null
 Copy-Item -LiteralPath $entry.target -Destination $save
 if((sha $save)-cne$entry.old){throw 'Private backup verification failed'}
 if($entry.source){Copy-Item -LiteralPath $entry.source -Destination $stage}else{[IO.File]::WriteAllText($stage,$entry.text,$utf8);$entry.new=sha $stage}
 if((sha $stage)-cne$entry.new){throw 'Staged candidate verification failed'}
 $entry.backup=$save;$entry.stage=$stage
}
Copy-Item -LiteralPath $receiptPath -Destination (Join-Path $backup 'candidate-build.json')
if((sha (Join-Path $backup 'candidate-build.json'))-cne$receiptHash){throw 'Build receipt backup did not verify'}
$plan=@{head=$head;probe=$probe;backup=$backup;candidate=$Candidate;entries=$changes;backupVerified=$true}
$plan|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $backup 'plan.json') -Encoding utf8
$rollback='H4sIAAAAAAAC/61US0/bQBC+51dMJUu2VZwCKY8Q5RDRUoIQRAnqBXHY2GN7W3vX3V3HitL8987acRKgtD305n145nvNFkyx3HvUFTdh+uSMiiJb+h3ns1JSjULDpZgojFGhCHHozows3E5civoEdMo8p/BX3hc0wRXP8JrpFIJbblCxbMJMCk4BwShLpOImzWF2PTo+OfW79l73Qd7KCtVYLJjiTBjPX3d4XNeaKBmi1jAttbnMOApzYD8h2MMFM2ooTLa8lMJwUaK/MqmSFbj1Va5BlUJwkQwgzKRG4AbmGEuFoFAbQiQSMCnCnIXfy8Jdd5wiY2Jo+9uSVPs5Fe9GchE0rCazWah4YaZSGnDtf91vWgrXh2DKqp/0/wKVuVIyD25o3/J6V5fvNt2+ouIxxyiQqtlOkUVBKNA9OY/Pj/uH/d7hYdyf989686OT+fFh7yiKwl7/bP5xftaPjsJTd0t3oviCmZYH5EzwGBsFBKGzFBUuOFYYgVFMaFbrR4StGCxMPYfEEdAAIdqKo/ZXHSDQHlkMDm5Q+xYhrWQWbbvfK55wwbK2fWojkHOdMwoU9QBHyGq4KWOYStDUle32rlzARLS3I7DaNhgLbViWWfD13xCmTCS0lKXRPEIiSEz3iA0s7dS62/iMEcFYNxY0+V65rf4gn8MfQMEod0F9D4xsS4AUdv2ikztQaEol/kXJ/xRr0kIZwh2VqmEYSkrackAHsnid79wuY6qorRV7hjZakqH4ozU03DR842JrDEX276mYbnBZvBTN1jIW0zjBohY/ZJsUgmMwL6Riajl8a8Q8d2NE4L5/TEoePV1c3GH1hb48n16SmbF0PffO9f0OXMpiGYyp6ou3qEUMwSeqRrbXgu/a7xHf7v2O3UN7uI3Hq9Q/ju+79kUknFOkOITo7WoebHU9cESZZf4fFH+uapPndhJedl13duNIPZcfMkZPdUqS1xHYzgPQtG1cwMiOCyQsR6iYbvPldn4BtXvcJRkGAAA='
$m=[IO.MemoryStream]::new([Convert]::FromBase64String($rollback));$g=[IO.Compression.GzipStream]::new($m,[IO.Compression.CompressionMode]::Decompress);$sr=[IO.StreamReader]::new($g);$rollbackText=$sr.ReadToEnd();$sr.Dispose()
[IO.File]::WriteAllText((Join-Path $backup 'rollback.ps1'),$rollbackText,$utf8)
try{
 offline
 if((sha $receiptPath)-cne$receiptHash-or(sha $steam)-cne$steamHash){throw 'Native build state changed while making the backup'}
 foreach($entry in $changes){if((sha $entry.target)-cne$entry.old-or(sha $entry.backup)-cne$entry.old){throw 'Target/backup changed after verification'}}
 foreach($entry in $changes){offline;if((sha $entry.target)-cne$entry.old){throw 'Target changed before replacement'};[IO.File]::Replace($entry.stage,$entry.target,$null);if((sha $entry.target)-cne$entry.new){throw 'Installed file verification failed'}}
 foreach($name in $modelPins.Keys){if((sha (Join-Path $play $name))-cne$modelPins[$name]){throw 'Installed model verification failed'}}
 $null=stepAssignment ([IO.File]::ReadAllText($launch)) 'physicsdiag,skate'
 $summary.applied=$true;$summary.backup=$backup;$summary|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $backup 'result.json') -Encoding utf8
 $summary|ConvertTo-Json -Depth 5
}catch{
 $failure=$_.Exception.Message
 try{& (Join-Path $backup 'rollback.ps1') -Apply|Out-Null;$failure+='; verified original files restored'}catch{$failure+='; automatic restore stopped, use private backup recovery'}
 throw ($failure+'; backup='+$backup)
}
