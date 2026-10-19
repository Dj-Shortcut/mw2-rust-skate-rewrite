param([switch]$Apply)
$ErrorActionPreference='Stop'
function sha($p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
if(Get-Process RustClient,Rust -ErrorAction SilentlyContinue){throw 'Rust is running; close it before restoring the backup'}
$plan=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'plan.json') -Raw|ConvertFrom-Json
if(!$plan.backupVerified-or$plan.head-cne'58f82909300f9b973b15b2031ddc397b4b79d1c6'){throw 'Private backup manifest is not the reviewed transaction'}
foreach($e in $plan.entries){
 if((sha $e.backup)-cne$e.old){throw 'Original backup hash mismatch'}
 $now=sha $e.target
 if($now-cne$e.old-and$now-cne$e.new){throw 'Installed target changed outside this transaction; nothing restored'}
}
if(!$Apply){'Verified original backup; pass -Apply to restore only this transaction';return}
foreach($e in $plan.entries){
 if(Get-Process RustClient,Rust -ErrorAction SilentlyContinue){throw 'Rust started during recovery; stop before restoring more files'}
 if((sha $e.target)-ceq$e.old){continue}
 if((sha $e.target)-cne$e.new-or(sha $e.backup)-cne$e.old){throw 'Recovery state changed after verification'}
 $temporary=Join-Path $PSScriptRoot ('restore-'+[guid]::NewGuid().ToString('N'))
 Copy-Item -LiteralPath $e.backup -Destination $temporary
 if((sha $temporary)-cne$e.old){throw 'Temporary restore hash mismatch'}
 [IO.File]::Replace($temporary,$e.target,[System.Management.Automation.Language.NullString]::Value)
 if((sha $e.target)-cne$e.old){throw 'Restored target hash mismatch'}
}
'Original play/launcher files restored and verified; no game was started'
