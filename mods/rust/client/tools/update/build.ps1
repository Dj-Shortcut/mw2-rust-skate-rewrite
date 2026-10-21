param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$ManifestSha256,
    [string]$ProbePath = (Join-Path $HOME 'Downloads/claude-loader-probe'),
    [Parameter(Mandatory=$true)][string]$ExpectedSteamBuild
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
$probe = [IO.Path]::GetFullPath($ProbePath).TrimEnd([IO.Path]::DirectorySeparatorChar); $null = Plain $probe -Directory
$native = NativeState $m $probe $ExpectedSteamBuild
$candidateParent = InProbe $probe 'candidates'
if (Test-Path -LiteralPath $candidateParent) { $null = Plain $candidateParent -Directory }
$candidate = InProbe $probe ('candidates/update-' + [guid]::NewGuid().ToString('N'))
Require (!(Test-Path -LiteralPath $candidate)) 'Candidate already exists'
$null = [IO.Directory]::CreateDirectory($candidate)
foreach ($folder in @('src','output','manifests')) { $null = [IO.Directory]::CreateDirectory((Join-Path $candidate $folder)) }
$receipt = @{ schema=1; manifestSha256=$ManifestSha256; sourceHead=$m.sourceHead; version=$m.version; steamBuild=$ExpectedSteamBuild; compiled=$false; installed=$false; gameStarted=$false }
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    foreach ($i in $m.inputs) {
        $folder = if ($i.kind -eq 'source') { 'src' } elseif ($i.kind -eq 'model') { 'output' } else { 'manifests' }
        $file = Join-Path (Join-Path $candidate $folder) ([IO.Path]::GetFileName($i.path))
        $uri = 'https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite/' + $i.commit + '/' + $i.path
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $file
        CheckFile $file $i.sha256
    }
    CheckInputs $m $candidate
    $sources = @($m.inputs | Where-Object kind -EQ 'source' | ForEach-Object { Join-Path (Join-Path $candidate 'src') ([IO.Path]::GetFileName($_.path)) })
    $dll = Join-Path $candidate 'output/ShortcutSkateClient.dll'
    $rsp = Join-Path $candidate 'client.rsp'
    $options = @('-nologo','-target:library','-nostdlib','-optimize+','-nowarn:CS1701,CS1702,CS8632',('-out:"' + $dll + '"'))
    $arguments = $options + @($native.references | ForEach-Object { '-r:"' + $_ + '"' }) + @($sources | ForEach-Object { '"' + $_ + '"' })
    [IO.File]::WriteAllLines($rsp, [string[]]$arguments, [Text.UTF8Encoding]::new($false))
    Stable $native.watch
    $global:LASTEXITCODE = $null
    $ErrorActionPreference = 'Continue'
    $output = & $native.dotnet $native.compiler ('@' + $rsp) 2>&1
    $code = $global:LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $text = ($output | ForEach-Object { [string]$_ }) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $candidate 'compiler.log'), $text)
    $receipt.compilerExit = $code
    $receipt.warnings = [regex]::Matches($text,'warning CS\d+').Count
    $receipt.errors = [regex]::Matches($text,'error CS\d+').Count
    Require ($null -ne $code -and $code -eq 0 -and $receipt.errors -eq 0) 'Native compiler did not succeed'
    Stable $native.watch; CheckInputs $m $candidate; ExactOutput $m (Join-Path $candidate 'output')
    $null = ReadManifest $Manifest $ManifestSha256 $PSScriptRoot
    $receipt.dllSha256 = FileHash $dll; $receipt.dllBytes = (Plain $dll).Length
    $receipt.sources = $sources.Count; $receipt.references = @($native.references).Count
    $receipt.generationFiles = @($m.native.generation).Count; $receipt.compiled = $true
} catch { $receipt.error = $_.Exception.Message }
WriteJsonNew (Join-Path $candidate 'build.json') $receipt
@{ candidate=$candidate; buildReceiptSha256=(FileHash (Join-Path $candidate 'build.json')); result=$receipt } | ConvertTo-Json -Depth 5
if (!$receipt.compiled) { throw 'Candidate compilation failed; inspect its local receipt and compiler log' }
