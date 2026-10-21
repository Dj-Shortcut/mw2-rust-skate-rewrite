param(
    [Parameter(Mandatory=$true)][string]$BackupPath,
    [Parameter(Mandatory=$true)][string]$PlanSha256,
    [string]$ProbePath = (Join-Path $HOME 'Downloads/claude-loader-probe'),
    [switch]$Apply
)
$ErrorActionPreference='Stop'
# Authenticate the plan and copied common code before importing any backup code.
if ($PlanSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'An exact private plan SHA256 is mandatory' }
$probe = [IO.Path]::GetFullPath($ProbePath).TrimEnd([IO.Path]::DirectorySeparatorChar)
$BackupPath = [IO.Path]::GetFullPath($BackupPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($BackupPath) -ine (Join-Path $probe 'private-backups') -or [IO.Path]::GetFileName($BackupPath) -cnotmatch '^update-[a-f0-9]{32}$') { throw 'Backup is outside this updater scope' }
for ($p = Get-Item -LiteralPath $BackupPath; $null -ne $p; $p = $p.Parent) {
    if (($p.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse-point backup refused' }
}
$planPath = Join-Path $BackupPath 'plan.json'
$planFile = Get-Item -LiteralPath $planPath
if ($planFile.PSIsContainer -or $planFile.Length -le 0 -or $planFile.Length -gt 1048576) { throw 'Private plan must be a bounded file' }
$planBytes = [IO.File]::ReadAllBytes($planPath); $hasher = [Security.Cryptography.SHA256]::Create()
try { $actualPlan = [BitConverter]::ToString($hasher.ComputeHash($planBytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
if ($actualPlan -cne $PlanSha256) { throw 'Private plan pin mismatch' }
$plan = [Text.UTF8Encoding]::new($false,$true).GetString($planBytes).TrimStart([char]0xfeff) | ConvertFrom-Json
$common = Join-Path $BackupPath 'Common.ps1'
if ((Get-Item -LiteralPath $common).Length -gt 1048576) { throw 'Private common helper exceeds its bound' }
$commonBytes = [IO.File]::ReadAllBytes($common); $hasher = [Security.Cryptography.SHA256]::Create()
try { $actualCommon = [BitConverter]::ToString($hasher.ComputeHash($commonBytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
if ($plan.commonSha256 -cnotmatch '^[a-f0-9]{64}$' -or $actualCommon -cne $plan.commonSha256) { throw 'Private common-tool pin mismatch' }
. ([scriptblock]::Create([Text.UTF8Encoding]::new($false,$true).GetString($commonBytes)))
$null = Plain $BackupPath -Directory; CheckFile $planPath $PlanSha256; Offline
$m = ReadManifest (Join-Path $BackupPath 'release.json') $plan.manifestSha256 $BackupPath
Require ($plan.schema -eq 1) 'Unsupported backup plan'
$allowed = @('play/ShortcutSkateClient.dll','launcher/version.txt') + @($m.inputs | Where-Object kind -EQ 'model' | ForEach-Object { 'play/' + [IO.Path]::GetFileName($_.path) })
$entries = @($plan.entries); Require ($entries.Count -ge 1 -and $entries.Count -le $allowed.Count) 'Unexpected restore entry count'
foreach ($e in $entries) {
    Require ($e.relative -cin $allowed -and $e.old -cmatch '^[a-f0-9]{64}$' -and $e.new -cmatch '^[a-f0-9]{64}$') 'Invalid restore entry'
    $allowed = @($allowed | Where-Object { $_ -cne $e.relative })
    $old = @($m.baseline.files | Where-Object relative -CEQ $e.relative); Require ($old.Count -eq 1 -and $old[0].sha256 -ceq $e.old) 'Restore entry differs from baseline'
    CheckFile (InProbe $BackupPath ('original/' + $e.relative)) $e.old
    $target = InProbe $probe $e.relative; $null = Plain $target
    $now = FileHash $target; Require ($now -ceq $e.old -or $now -ceq $e.new) 'Target changed outside this transaction'
}
# Every unchanged baseline file must also remain exact before any restoration.
foreach ($b in $m.baseline.files) {
    if (@($entries | Where-Object relative -CEQ $b.relative).Count -eq 0) { CheckFile (InProbe $probe $b.relative) $b.sha256 $b.bytes }
}
if (!$Apply) { @{ verified=$true; applied=$false; entries=$entries.Count } | ConvertTo-Json; return }
foreach ($e in $entries) {
    Offline; $target = InProbe $probe $e.relative
    $null = Plain $target; $now = FileHash $target; if ($now -ceq $e.old) { continue }
    Require ($now -ceq $e.new) 'Target changed after restore verification'
    $source = InProbe $BackupPath ('original/' + $e.relative); CheckFile $source $e.old
    $stage = Join-Path $BackupPath ('restore-' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $source -Destination $stage; CheckFile $stage $e.old
    CheckFile $target $e.new
    [IO.File]::Replace($stage,$target,[System.Management.Automation.Language.NullString]::Value)
    CheckFile $target $e.old
}
$null = BaselineState $m $probe
@{ verified=$true; applied=$true; restoredBaselineVersion=$m.baseline.version; entries=$entries.Count } | ConvertTo-Json
