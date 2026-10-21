Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
function Require($ok, [string]$message) { if (!$ok) { throw $message } }
function FileHash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Plain([string]$path, [switch]$Directory) {
    $item = Get-Item -LiteralPath $path -Force
    Require ($item.PSIsContainer -eq [bool]$Directory) 'Unexpected file/directory type'
    for ($p = $item; $null -ne $p; $p = if ($p -is [IO.DirectoryInfo]) { $p.Parent } else { $p.Directory }) {
        Require (($p.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse points are not supported'
    }
    $item
}
function InProbe([string]$probe, [string]$relative) {
    Require ($relative -cmatch '^[A-Za-z0-9_.@/-]+$' -and $relative -notmatch '(^|/)\.{1,2}(/|$)' -and !$relative.StartsWith('/')) 'Invalid relative path'
    $root = [IO.Path]::GetFullPath($probe).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $p = [IO.Path]::GetFullPath((Join-Path $root $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    Require ($p.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Path escaped the probe'
    $p
}
function BytesHash([byte[]]$bytes) {
    $h = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($h.ComputeHash($bytes)).Replace('-','').ToLowerInvariant() } finally { $h.Dispose() }
}
function ReadJson([string]$path, [string]$expected) {
    Require ($expected -cmatch '^[a-f0-9]{64}$') 'An exact SHA256 is mandatory'
    $f = Plain $path
    Require ($f.Length -gt 0 -and $f.Length -le 1048576) 'JSON is empty or exceeds one MiB'
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    Require ((BytesHash $bytes) -ceq $expected -and (FileHash $f.FullName) -ceq $expected -and $bytes.Length -eq $f.Length) 'JSON pin mismatch'
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xfeff)
    $text | ConvertFrom-Json
}
function Offline { Require (!(Get-Process RustClient,Rust -ErrorAction SilentlyContinue)) 'Rust must be closed' }
function CheckFile([string]$path, [string]$hash, [long]$bytes = 0) {
    $f = Plain $path
    Require ($hash -cmatch '^[a-f0-9]{64}$' -and ($bytes -eq 0 -or $f.Length -eq $bytes) -and (FileHash $path) -ceq $hash) 'Pinned file differs'
}
function ExactKeys($value, [string[]]$keys) {
    Require ($null -ne $value -and $value -is [Management.Automation.PSCustomObject]) 'A manifest object is required'
    $names = @($value.PSObject.Properties | ForEach-Object { $_.Name })
    Require ($names.Count -eq $keys.Count -and @($names | Where-Object { $_ -cnotin $keys }).Count -eq 0) 'Missing, unknown or incorrectly cased manifest field'
}
function ReadManifest([string]$path, [string]$expected, [string]$toolRoot) {
    $m = ReadJson $path $expected
    ExactKeys $m @('schema','sourceHead','version','inputs','tools','baseline','native')
    ExactKeys $m.baseline @('version','files')
    ExactKeys $m.native @('dotnetSha256','compilerSha256','generationReceiptSha256','compilerRelative','generation','references')
    Require ($m.schema -eq 1 -and $m.sourceHead -cmatch '^[a-f0-9]{40}$') 'Unsupported or unbound manifest'
    Require ($m.version -cmatch '^0\.18\.\d+$' -and $m.baseline.version -cmatch '^0\.18\.\d+$' -and [version]$m.version -gt [version]$m.baseline.version) 'The release must advance the baseline version'
    $inputs = @($m.inputs); Require ($inputs.Count -ge 4 -and $inputs.Count -le 194) 'Input count is outside the supported bound'
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $leaves = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($i in $inputs) {
        ExactKeys $i @('kind','path','commit','sha256')
        Require ($i.commit -cmatch '^[a-f0-9]{40}$' -and $i.sha256 -cmatch '^[a-f0-9]{64}$' -and $paths.Add($i.path)) 'Unbound or duplicate input'
        $valid = switch ($i.kind) {
            'source' { $i.path -cmatch '^mods/rust/(client/src|shared)/[A-Za-z0-9_]+\.cs$' }
            'model' { $i.path -cmatch '^assets/authored/[a-z0-9_]+\.glb$' }
            'source-list' { $i.path -ceq 'mods/rust/client/sources.txt' -and $i.commit -ceq $m.sourceHead }
            'model-list' { $i.path -ceq 'mods/rust/client/models.txt' -and $i.commit -ceq $m.sourceHead }
            default { $false }
        }
        Require ($valid -and $leaves.Add([IO.Path]::GetFileName($i.path))) 'Unsupported input path or duplicate output name'
    }
    Require (@($inputs | Where-Object kind -EQ 'source-list').Count -eq 1 -and @($inputs | Where-Object kind -EQ 'model-list').Count -eq 1) 'Both source manifests are mandatory'
    Require (@($inputs | Where-Object kind -EQ 'source').Count -ge 1 -and @($inputs | Where-Object kind -EQ 'model').Count -ge 1) 'Sources and models are mandatory'
    $tools = @('Common.ps1','build.ps1','install.ps1','rollback.ps1')
    ExactKeys $m.tools $tools
    Require (@($m.tools.PSObject.Properties).Count -eq $tools.Count) 'Unexpected tool inventory'
    foreach ($name in $tools) { CheckFile (Join-Path $toolRoot $name) $m.tools.$name }
    $models = @($inputs | Where-Object kind -EQ 'model' | ForEach-Object { [IO.Path]::GetFileName($_.path) })
    $required = @('play/ShortcutSkateClient.dll','launcher/skate.ps1','launcher/version.txt') + @($models | ForEach-Object { 'play/' + $_ })
    $baseline = @($m.baseline.files); Require ($baseline.Count -eq $required.Count) 'Baseline must cover the exact play files and launcher/marker'
    foreach ($b in $baseline) {
        ExactKeys $b @('relative','sha256','bytes')
        Require ($b.relative -cin $required -and $b.sha256 -cmatch '^[a-f0-9]{64}$' -and $b.bytes -gt 0) 'Invalid baseline pin'
        $required = @($required | Where-Object { $_ -cne $b.relative })
    }
    Require ($required.Count -eq 0) 'Missing or duplicate baseline pin'
    Require ($m.native.dotnetSha256 -cmatch '^[a-f0-9]{64}$' -and $m.native.compilerSha256 -cmatch '^[a-f0-9]{64}$' -and $m.native.generationReceiptSha256 -cmatch '^[a-f0-9]{64}$') 'Native toolchain pins are mandatory'
    Require ($m.native.compilerRelative -cmatch '^sdk/sdk/[0-9.]+/Roslyn/bincore/csc\.dll$') 'Unsupported existing compiler path'
    $gen = @($m.native.generation); $refs = @($m.native.references)
    Require ($gen.Count -ge 1 -and $gen.Count -le 1024 -and $refs.Count -ge 1 -and $refs.Count -le 2048) 'Native inventory is outside the supported bound'
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($g in $gen) { ExactKeys $g @('name','sha256'); Require ($g.name -cmatch '^[A-Za-z0-9_.-]+\.(dll|db)$' -and $g.sha256 -cmatch '^[a-f0-9]{64}$' -and $names.Add($g.name)) 'Invalid generation pin' }
    $names.Clear()
    foreach ($r in $refs) { ExactKeys $r @('relative','sha256'); Require ($r.relative -cmatch '^(@interop|bep788/(dotnet|BepInEx/core))/[A-Za-z0-9_.-]+\.dll$' -and $r.sha256 -cmatch '^[a-f0-9]{64}$' -and $names.Add($r.relative)) 'Invalid reference pin' }
    $m
}
function NativeState($m, [string]$probe, [string]$steamBuild) {
    Require ($steamBuild -cmatch '^[0-9]{6,12}$') 'ExpectedSteamBuild is mandatory build metadata'
    $steam = 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf'
    $null = Plain $steam
    $build = [regex]::Match([IO.File]::ReadAllText($steam), '"buildid"\s+"([0-9]+)"').Groups[1].Value
    Require ($build -ceq $steamBuild) 'Installed Steam build differs'
    $gen = InProbe $probe ('gen-' + $steamBuild + '/out'); $null = Plain $gen -Directory
    $log = InProbe $probe ('gen-' + $steamBuild + '/gen.log'); CheckFile $log $m.native.generationReceiptSha256
    Require (Select-String -LiteralPath $log -Pattern 'GEN DONE.* errors=0( |$)' -Quiet) 'Generation did not complete successfully'
    $watch = @(@{ path = $steam; sha256 = (FileHash $steam) }, @{ path = $log; sha256 = $m.native.generationReceiptSha256 })
    $files = @(Get-ChildItem -LiteralPath $gen -Force)
    Require ($files.Count -eq @($m.native.generation).Count) 'Generation file inventory differs'
    foreach ($g in $m.native.generation) {
        $p = Join-Path $gen $g.name; CheckFile $p $g.sha256
        $watch += @{ path = $p; sha256 = $g.sha256 }
    }
    $refPaths = @(); $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($r in $m.native.references) {
        $p = if ($r.relative.StartsWith('@interop/')) { Join-Path $gen $r.relative.Substring(9) } else { InProbe $probe $r.relative }
        Require ($seen.Add($p)) 'Duplicate native reference path'; CheckFile $p $r.sha256
        $refPaths += $p; $watch += @{ path = $p; sha256 = $r.sha256 }
    }
    $fx = InProbe $probe 'bep788/dotnet'; $core = InProbe $probe 'bep788/BepInEx/core'
    $null = Plain $fx -Directory; $null = Plain $core -Directory
    $actualRefs = @((Get-ChildItem -LiteralPath $fx -File -Filter *.dll | Where-Object { ($_.Name -like 'System*' -or $_.Name -in @('mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll')) -and $_.Name -notlike '*.Native.dll' }).FullName)
    $actualRefs += @('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','BepInEx.Unity.Common.dll','Il2CppInterop.Runtime.dll','Il2CppInterop.Common.dll' | ForEach-Object { Join-Path $core $_ })
    $actualRefs += @((Get-ChildItem -LiteralPath $gen -File -Filter *.dll).FullName)
    Require ($actualRefs.Count -eq $refPaths.Count) 'Native reference inventory differs'
    foreach ($actual in $actualRefs) { Require ($seen.Remove($actual)) 'Unexpected native reference' }
    Require ($seen.Count -eq 0) 'Missing native reference'
    $dotnet = InProbe $probe 'sdk/dotnet.exe'; $compiler = InProbe $probe $m.native.compilerRelative
    CheckFile $dotnet $m.native.dotnetSha256; CheckFile $compiler $m.native.compilerSha256
    $watch += @{ path = $dotnet; sha256 = $m.native.dotnetSha256 }; $watch += @{ path = $compiler; sha256 = $m.native.compilerSha256 }
    @{ watch = $watch; references = $refPaths; dotnet = $dotnet; compiler = $compiler }
}
function Stable($watch) { foreach ($w in $watch) { CheckFile $w.path $w.sha256 } }
function CheckInputs($m, [string]$candidate) {
    foreach ($i in $m.inputs) {
        $folder = if ($i.kind -eq 'source') { 'src' } elseif ($i.kind -eq 'model') { 'output' } else { 'manifests' }
        CheckFile (Join-Path (Join-Path $candidate $folder) ([IO.Path]::GetFileName($i.path))) $i.sha256
    }
    $pluginPin = @($m.inputs | Where-Object path -CEQ 'mods/rust/client/src/Plugin.cs')
    Require ($pluginPin.Count -eq 1 -and $pluginPin[0].kind -ceq 'source') 'Plugin source pin is mandatory'
    $plugin = [IO.File]::ReadAllText((Join-Path $candidate 'src/Plugin.cs'))
    $versions = [regex]::Matches($plugin, 'public\s+const\s+string\s+Version\s*=\s*"([^"\r\n]+)"\s*;')
    Require ($versions.Count -eq 1 -and $versions[0].Groups[1].Value -ceq $m.version) 'Pinned plugin version differs from release version'
    foreach ($pair in @(@('source-list','source'), @('model-list','model'))) {
        $list = @($m.inputs | Where-Object kind -EQ $pair[0])[0]
        $lines = @([IO.File]::ReadAllLines((Join-Path (Join-Path $candidate 'manifests') ([IO.Path]::GetFileName($list.path)))) | Where-Object { $_.Trim() -and !$_.Trim().StartsWith('#') })
        $entries = @($m.inputs | Where-Object kind -EQ $pair[1]); Require ($lines.Count -eq $entries.Count) 'Manifest source/model count differs'
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($line in $lines) {
            $v = $line.Trim() -split '\s+'; Require ($v.Count -eq 1 -or ($pair[1] -eq 'source' -and $v.Count -eq 3)) 'Invalid source manifest line'
            $pin = @($entries | Where-Object path -CEQ $v[0]); Require ($pin.Count -eq 1 -and $seen.Add($v[0])) 'Source manifest path differs'
            $commit = if ($v.Count -eq 3) { $v[1] } else { $m.sourceHead }; Require ($pin[0].commit -ceq $commit) 'Source manifest commit differs'
            if ($v.Count -eq 3) { Require ($pin[0].sha256 -ceq $v[2]) 'Shared source manifest SHA differs' }
        }
    }
}
function ExactOutput($m, [string]$folder) {
    $names = @('ShortcutSkateClient.dll') + @($m.inputs | Where-Object kind -EQ 'model' | ForEach-Object { [IO.Path]::GetFileName($_.path) })
    $files = @(Get-ChildItem -LiteralPath $folder -Force)
    Require ($files.Count -eq $names.Count) 'Output/play inventory differs'
    foreach ($f in $files) { Require ($f.Name -cin $names -and !$f.PSIsContainer) 'Unexpected output/play entry'; $null = Plain $f.FullName }
}
function BaselineState($m, [string]$probe) {
    ExactOutput $m (InProbe $probe 'play')
    $watch = @()
    foreach ($b in $m.baseline.files) { $p = InProbe $probe $b.relative; CheckFile $p $b.sha256 $b.bytes; $watch += @{ path = $p; sha256 = $b.sha256 } }
    $watch
}
function PrivateDirectory([string]$path) {
    Require (!(Test-Path -LiteralPath $path)) 'Backup already exists'
    $null = [IO.Directory]::CreateDirectory($path)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
    foreach ($who in @($sid,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($who,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $null = $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $path -AclObject $acl
    $check = Get-Acl -LiteralPath $path; Require ($check.AreAccessRulesProtected) 'Backup ACL inheritance is enabled'
    foreach ($rule in $check.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) { Require ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $rule.IdentityReference.Value -in @($sid.Value,'S-1-5-18')) 'Unexpected backup ACL identity' }
}
function WriteJsonNew([string]$path, $value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($value | ConvertTo-Json -Depth 14))
    $stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
