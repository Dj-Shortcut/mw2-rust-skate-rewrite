param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$ManifestSha256,
    [string]$ProbePath = (Join-Path $HOME 'Downloads/claude-loader-probe'),
    [Parameter(Mandatory=$true)][string]$ExpectedSteamBuild,
    [Parameter(Mandatory=$true)][string]$Candidate,
    [Parameter(Mandatory=$true)][string]$BuildReceiptSha256,
    [switch]$Apply
)
# Pin the external manifest and common helper before importing any helper code.
$ErrorActionPreference = 'Stop'
if ($ManifestSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'An exact manifest SHA256 is mandatory' }
$manifestFile = Get-Item -LiteralPath $Manifest -Force
if ($manifestFile.PSIsContainer -or $manifestFile.Length -le 0 -or $manifestFile.Length -gt 1048576) { throw 'Manifest must be a bounded file' }
for ($p = $manifestFile; $null -ne $p; $p = if ($p -is [IO.DirectoryInfo]) { $p.Parent } else { $p.Directory }) {
    if (($p.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse-point manifest refused' }
}
$manifestBytes = [IO.File]::ReadAllBytes($Manifest)
$hasher = [Security.Cryptography.SHA256]::Create()
try { $actualManifest = [BitConverter]::ToString($hasher.ComputeHash($manifestBytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
if ($actualManifest -cne $ManifestSha256) { throw 'Manifest pin mismatch' }
$trustedManifest = [Text.UTF8Encoding]::new($false,$true).GetString($manifestBytes).TrimStart([char]0xfeff) | ConvertFrom-Json
$commonPath = Join-Path $PSScriptRoot 'Common.ps1'
if ((Get-Item -LiteralPath $commonPath).Length -gt 1048576) { throw 'Common helper exceeds its bound' }
$commonBytes = [IO.File]::ReadAllBytes($commonPath); $hasher = [Security.Cryptography.SHA256]::Create()
try { $actualCommon = [BitConverter]::ToString($hasher.ComputeHash($commonBytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
if ($trustedManifest.tools.'Common.ps1' -cnotmatch '^[a-f0-9]{64}$' -or $actualCommon -cne $trustedManifest.tools.'Common.ps1') { throw 'Common helper pin mismatch' }
. ([scriptblock]::Create([Text.UTF8Encoding]::new($false,$true).GetString($commonBytes)))
$m = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
Offline
$probe = [IO.Path]::GetFullPath($ProbePath).TrimEnd([IO.Path]::DirectorySeparatorChar); $null = Plain $probe -Directory
$candidates = InProbe $probe 'candidates'
$Candidate = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar)
Require ([IO.Path]::GetDirectoryName($Candidate) -ieq $candidates -and [IO.Path]::GetFileName($Candidate) -cmatch '^update-[a-f0-9]{32}$') 'Candidate is outside the reviewed staging directory'
$null = Plain $Candidate -Directory
$receiptPath = Join-Path $Candidate 'build.json'; $r = ReadJson $receiptPath $BuildReceiptSha256
Require ($r.schema -eq 1 -and $r.manifestSha256 -ceq $ManifestSha256 -and $r.sourceHead -ceq $m.sourceHead -and $r.version -ceq $m.version -and $r.steamBuild -ceq $ExpectedSteamBuild) 'Candidate identity differs from the release manifest'
Require ($r.compiled -is [bool] -and $r.compiled -and $null -ne $r.compilerExit -and $r.compilerExit -eq 0 -and $r.errors -eq 0) 'Candidate has no successful native compile receipt'
$native = NativeState $m $probe $ExpectedSteamBuild
Require ($r.sources -eq @($m.inputs | Where-Object kind -EQ 'source').Count -and $r.references -eq @($native.references).Count -and $r.generationFiles -eq @($m.native.generation).Count) 'Candidate native/source counts differ'
CheckInputs $m $Candidate; ExactOutput $m (Join-Path $Candidate 'output')
$dll = Join-Path $Candidate 'output/ShortcutSkateClient.dll'; CheckFile $dll $r.dllSha256 $r.dllBytes
$baseline = @(BaselineState $m $probe)
$changes = @()
foreach ($b in $m.baseline.files) {
    if ($b.relative -ceq 'launcher/skate.ps1') { continue }
    if ($b.relative -ceq 'launcher/version.txt') {
        $changes += @{ relative=$b.relative; old=$b.sha256; text=('Skate client ' + $m.version + ' (' + $m.sourceHead.Substring(0,7) + ")`r`n") }
    } else {
        $file = Join-Path (Join-Path $Candidate 'output') ([IO.Path]::GetFileName($b.relative))
        $new = FileHash $file
        if ($new -cne $b.sha256) { $changes += @{ relative=$b.relative; old=$b.sha256; new=$new; source=$file } }
    }
}
Require (@($changes | Where-Object relative -CEQ 'play/ShortcutSkateClient.dll').Count -eq 1) 'This release does not advance the installed DLL'
Stable $baseline; Stable $native.watch; CheckFile $receiptPath $BuildReceiptSha256
$null = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
$summary = @{ ready=$true; applied=$false; version=$m.version; sourceHead=$m.sourceHead; changes=@($changes | ForEach-Object { $_.relative }); launcherUnchanged=$true }
if (!$Apply) { $summary | ConvertTo-Json -Depth 5; return }
# Everything above is verification only. Repeat all inputs immediately before any backup/write.
Offline; Stable $baseline; Stable $native.watch; CheckInputs $m $Candidate
CheckFile $receiptPath $BuildReceiptSha256; $null = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
$backup = InProbe $probe ('private-backups/update-' + [guid]::NewGuid().ToString('N'))
$parent = [IO.Path]::GetDirectoryName($backup); if (Test-Path -LiteralPath $parent) { $null = Plain $parent -Directory }
PrivateDirectory $backup
foreach ($name in @('Common.ps1','build.ps1','install.ps1','rollback.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $backup $name)
    CheckFile (Join-Path $backup $name) $m.tools.$name
}
Copy-Item -LiteralPath $Manifest -Destination (Join-Path $backup 'release.json'); CheckFile (Join-Path $backup 'release.json') $ManifestSha256
Copy-Item -LiteralPath $receiptPath -Destination (Join-Path $backup 'candidate-build.json'); CheckFile (Join-Path $backup 'candidate-build.json') $BuildReceiptSha256
foreach ($e in $changes) {
    $target = InProbe $probe $e.relative; CheckFile $target $e.old
    $save = InProbe $backup ('original/' + $e.relative); $stage = InProbe $backup ('staged/' + $e.relative)
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($save)); $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stage))
    Copy-Item -LiteralPath $target -Destination $save; CheckFile $save $e.old
    if ($e.ContainsKey('source')) { Copy-Item -LiteralPath $e.source -Destination $stage } else { [IO.File]::WriteAllText($stage,$e.text,[Text.UTF8Encoding]::new($false)); $e.new = FileHash $stage }
    CheckFile $stage $e.new
}
$plan = @{ schema=1; manifestSha256=$ManifestSha256; buildReceiptSha256=$BuildReceiptSha256; commonSha256=$m.tools.'Common.ps1'; entries=@($changes | ForEach-Object { @{ relative=$_.relative; old=$_.old; new=$_.new } }) }
$planPath = Join-Path $backup 'plan.json'; WriteJsonNew $planPath $plan; $planHash = FileHash $planPath
try {
    Offline; Stable $baseline; Stable $native.watch; CheckInputs $m $Candidate
    CheckFile $receiptPath $BuildReceiptSha256; $null = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
    foreach ($e in $changes) {
        Offline; CheckFile (InProbe $probe $e.relative) $e.old
        CheckFile (InProbe $backup ('original/' + $e.relative)) $e.old
        CheckFile (InProbe $backup ('staged/' + $e.relative)) $e.new
        [IO.File]::Replace((InProbe $backup ('staged/' + $e.relative)),(InProbe $probe $e.relative),[System.Management.Automation.Language.NullString]::Value)
        CheckFile (InProbe $probe $e.relative) $e.new
    }
    foreach ($w in $baseline) {
        $changed = @($changes | Where-Object { (InProbe $probe $_.relative) -ieq $w.path })
        CheckFile $w.path $(if ($changed.Count) { $changed[0].new } else { $w.sha256 })
    }
    Stable $native.watch; CheckInputs $m $Candidate; CheckFile $receiptPath $BuildReceiptSha256
    $null = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
    $summary.applied=$true; $summary.backup=$backup; $summary.planSha256=$planHash
    WriteJsonNew (Join-Path $backup 'result.json') $summary
    $summary | ConvertTo-Json -Depth 5
} catch {
    $failure = $_.Exception.Message
    try { CheckFile (Join-Path $backup 'rollback.ps1') $m.tools.'rollback.ps1'; & (Join-Path $backup 'rollback.ps1') -BackupPath $backup -ProbePath $probe -PlanSha256 $planHash -Apply | Out-Null; $failure += '; original files restored and verified' }
    catch { $failure += '; automatic restoration stopped; retain the private backup for recovery' }
    throw ($failure + '; backup=' + $backup + '; planSha256=' + $planHash)
}
