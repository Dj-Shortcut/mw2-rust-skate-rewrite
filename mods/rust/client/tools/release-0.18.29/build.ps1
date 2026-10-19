$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$head='58f82909300f9b973b15b2031ddc397b4b79d1c6'
$probe=Join-Path $HOME 'Downloads\claude-loader-probe'
if(!(Test-Path $probe -PathType Container)){throw 'Existing probe folder missing'}
$candidate=Join-Path $probe ('candidates\client029-58f8290-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
[IO.Directory]::CreateDirectory($candidate)|Out-Null
$receipt=@{head=$head;version='0.18.29';candidate=$candidate;compiled=$false;installed=$false;gameStarted=$false;scope='Native Windows compilation only; new candidate directory; no installed files changed'}
function sha($file){if(Test-Path $file -PathType Leaf){(Get-FileHash $file -Algorithm SHA256).Hash.ToLower()}else{$null}}
try{
 $steam='C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf'
 $steamHash=sha $steam
 $build=[regex]::Match([IO.File]::ReadAllText($steam),'"buildid"\s+"([0-9]+)"').Groups[1].Value
 if($build-ne'25903346'){throw "Installed Steam build is $build; expected 25903346"}
 $gen=Join-Path $probe "gen-$build\out"
 if(!(Select-String -Path (Join-Path $probe "gen-$build\gen.log") -Pattern 'GEN DONE.* errors=0( |$)' -Quiet)){throw 'No successful matching interop receipt'}
 $genFiles=@(Get-ChildItem -LiteralPath $gen -File -Force)
 $interop=@($genFiles|Where-Object Extension -EQ '.dll')
 $sidecars=@($genFiles|Where-Object Extension -NE '.dll'|Sort-Object Name)
 if($genFiles.Count-ne193-or$interop.Count-ne191-or($sidecars.Name-join'|')-cne'MethodAddressToToken.db|MethodXrefScanCache.db'){throw 'Current generation must contain exactly 191 DLLs and the two known DB sidecars (193 files)'}
 $core=Join-Path $probe 'bep788\BepInEx\core';$fx=Join-Path $probe 'bep788\dotnet';$sdk=Join-Path $probe 'sdk'
 $dotnet=Join-Path $sdk 'dotnet.exe'
 $compiler=Get-ChildItem (Join-Path $sdk 'sdk') -Directory|Where-Object Name -Match '^\d+\.\d+'|Sort-Object Name -Descending|Select-Object -First 1
 $csc=Join-Path $compiler.FullName 'Roslyn\bincore\csc.dll'
 if(!(Test-Path $dotnet)-or!(Test-Path $csc)){throw 'Existing staged SDK compiler missing'}
 $packed='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'
 $stream=[IO.MemoryStream]::new([Convert]::FromBase64String($packed))
 $gzip=[IO.Compression.GzipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
 $reader=[IO.StreamReader]::new($gzip);$pins=$reader.ReadToEnd()|ConvertFrom-Json;$reader.Dispose()
 [IO.Directory]::CreateDirectory("$candidate\src")|Out-Null
 [IO.Directory]::CreateDirectory("$candidate\output")|Out-Null
 $inputs=@();$sources=@();$models=@()
 foreach($pin in $pins){
  $name=Split-Path $pin.p -Leaf
  $folder=if($pin.t-eq'source'){"$candidate\src"}elseif($pin.t-eq'model'){"$candidate\output"}else{$candidate}
  $file=Join-Path $folder $name
  Invoke-WebRequest -UseBasicParsing -Uri "https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite/$($pin.c)/$($pin.p)" -OutFile $file
  $actual=sha $file
  if($actual-ne$pin.h){throw "SHA256 mismatch for $($pin.p)"}
  $inputs+=@{path=$pin.p;commit=$pin.c;sha256=$actual}
  if($pin.t-eq'source'){$sources+=$file};if($pin.t-eq'model'){$models+=$name}
 }
 if($sources.Count-ne38-or$models.Count-ne8){throw 'Unexpected source/model inventory'}
 $refs=@(Get-ChildItem $fx -Filter *.dll|Where-Object{($_.Name-like'System*'-or$_.Name-in'mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll')-and$_.Name-notlike'*.Native.dll'}|ForEach-Object FullName)
 $refs+=@('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','BepInEx.Unity.Common.dll','Il2CppInterop.Runtime.dll','Il2CppInterop.Common.dll'|ForEach-Object{Join-Path $core $_})
 $refs+=@($interop|ForEach-Object FullName)
 if($refs.Count-ne356){throw "Expected 356 real references, found $($refs.Count)"}
 $referencePins=@($refs|ForEach-Object{@{path=$_;sha256=(sha $_)}})
 if(@($referencePins|Where-Object{!$_.sha256}).Count){throw 'A real reference is missing'}
 $dll="$candidate\output\ShortcutSkateClient.dll";$rsp="$candidate\client.rsp"
 $options=@('-nologo','-target:library','-nostdlib','-optimize+','-nowarn:CS1701,CS1702,CS8632',('-out:"'+$dll+'"'))
 ($options+@($refs|ForEach-Object{'-r:"'+$_+'"'})+@($sources|ForEach-Object{'"'+$_+'"'}))|Set-Content $rsp -Encoding ascii
 $ErrorActionPreference='Continue';$output=& $dotnet $csc "@$rsp" 2>&1;$code=$LASTEXITCODE;$ErrorActionPreference='Stop'
 $text=($output|ForEach-Object{"$_"})-join"`r`n";[IO.File]::WriteAllText("$candidate\compiler.log",$text)
 $receipt.compiler=$csc;$receipt.steamBuild=$build;$receipt.sources=$sources.Count;$receipt.references=$refs.Count;$receipt.interop=$interop.Count;$receipt.generationFiles=$genFiles.Count;$receipt.sidecars=@($sidecars.Name);$receipt.models=$models
 $receipt.compilerExit=$code;$receipt.warnings=[regex]::Matches($text,'warning CS\d+').Count;$receipt.errors=[regex]::Matches($text,'error CS\d+').Count
 $receipt.inputs=$inputs;$receipt.referencePins=$referencePins
 if($code-ne0-or!(Test-Path $dll)){throw "Client compiler failed: exit $code"}
 foreach($pin in $referencePins){if((sha $pin.path)-ne$pin.sha256){throw 'Reference changed during compilation'}}
 if((sha $steam)-ne$steamHash){throw 'Steam manifest changed during compilation'}
 $receipt.compiled=$true;$receipt.dllSha256=sha $dll;$receipt.dllBytes=(Get-Item $dll).Length
}catch{$receipt.error=$_.Exception.Message}
$receipt|ConvertTo-Json -Depth 8|Set-Content "$candidate\build.json" -Encoding utf8
Write-Host "CANDIDATE: $candidate"
@{head=$head;compiled=$receipt.compiled;sources=$receipt.sources;refs=$receipt.references;models=@($receipt.models).Count;exit=$receipt.compilerExit;warnings=$receipt.warnings;errors=$receipt.errors;sha256=$receipt.dllSha256;error=$receipt.error}|ConvertTo-Json -Compress|Write-Host
