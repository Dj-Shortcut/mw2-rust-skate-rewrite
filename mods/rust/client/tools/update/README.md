# Manifest-driven client update tools

These maintainer tools build a new candidate and install only that exact candidate over a separately pinned client baseline. They contain no frozen release payload, release manifest, current-install claim or default client downgrade. A release manifest can be issued only after its public source, existing native toolchain and installed baseline have been inspected and pinned.

Use the four scripts from one reviewed immutable package commit. `build.ps1` and `install.ps1` require both the manifest file and its independently trusted SHA256. They authenticate `Common.ps1` before importing it, then verify all four tool hashes. Obtain the expected manifest hash through the reviewed release handoff, not from an adjacent untrusted file.

The existing staged SDK, generated wrappers, BepInEx and launcher must already exist under `Downloads/claude-loader-probe`. `-ProbePath` may select another existing copy of that same layout; it never changes the operating system's home directory. The tools do not install an SDK, generate wrappers, change system or game security settings, launch or close Rust, alter Steam files, send files, or change server state.

## Manifest contract

There is deliberately no sample manifest containing invented release pins. Supply a real JSON object with these exact required fields. Unknown or incorrectly cased keys at any manifest level are refused before candidate/backup creation:

| Field | Contract |
| --- | --- |
| `schema` | Integer `1`. |
| `sourceHead`, `version` | Exact public Git commit and client version. The version must exceed `baseline.version` and match the pinned `Plugin.cs` constant. |
| `inputs` | Array of `{kind,path,commit,sha256}`. Kinds are `source`, `model`, `source-list`, `model-list`. Include the two client manifests, every source they resolve and every model. Shared source overrides retain their independent commit and hash. No duplicate paths or output names. |
| `tools` | Object mapping `Common.ps1`, `build.ps1`, `install.ps1`, `rollback.ps1` to their exact hashes. |
| `baseline.version` | Version of the separately verified installed client. |
| `baseline.files` | Exact `{relative,sha256,bytes}` pins for the play DLL, every listed play model, `launcher/skate.ps1` and `launcher/version.txt`. No omitted or extra entry. |
| `native.generationReceiptSha256` | Hash of the existing successful generation log. |
| `native.generation` | Exact `{name,sha256}` array for every generated DLL and sidecar file. |
| `native.references` | Exact `{relative,sha256}` compiler-reference set. Paths use `@interop/` for generated DLLs, or `bep788/dotnet/` and `bep788/BepInEx/core/` for existing dependencies. The live selected reference set must match exactly. |
| `native.dotnetSha256`, `native.compilerSha256`, `native.compilerRelative` | Existing SDK host and compiler pins; compiler path is under `sdk/sdk/<version>/Roslyn/bincore/csc.dll`. |

Pass the actual independently checked Steam build as the mandatory `-ExpectedSteamBuild` argument. It is compared with the current Steam manifest, the matching generation directory and receipt, and the exact generated/reference hashes. No specific Steam build number is embedded in these scripts. The current manifest is also hashed before and after compilation or installation verification.

## Build and verify

Use values from the reviewed handoff in these variables; this page supplies no release pins:

```powershell
& "$tools\build.ps1" -Manifest $manifest -ManifestSha256 $manifestHash -ExpectedSteamBuild $steamBuild
& "$tools\install.ps1" -Manifest $manifest -ManifestSha256 $manifestHash -ExpectedSteamBuild $steamBuild -Candidate $candidate -BuildReceiptSha256 $buildReceiptHash
```

The build downloads only the exact public source/model/manifest inputs into a fresh candidate folder. It checks the two source manifests against the supplied resolved input set, compiles with the existing pinned SDK, and checks every watched native file and candidate input again. Require `compiled: true`, compiler exit zero, zero errors and a matching DLL hash/length in `build.json`. Read any warnings. A candidate path or a green repository CI job alone does not establish a successful native client build.

Installation defaults to verification only and requires Rust closed, a successful exact build receipt, unchanged native inputs, exact output inventory and the exact installed baseline. Require `ready: true, applied: false` before applying the same candidate with `-Apply` and the same arguments.

Apply creates a new protected current-user/SYSTEM backup, verifies every original and staged file, saves the authenticated manifest/build receipt and all four tools, then writes a new hash-bound plan. It repeats baseline/native/input checks before the first replacement. Only the play DLL, changed listed models and displayed version marker can be replaced. The launcher script and its selected steps remain byte-identical. Each replacement is atomic; partial failure attempts the same guarded rollback.

## Restore only this transaction

Retain the returned backup directory and plan SHA256. With Rust closed:

```powershell
& "$backup\rollback.ps1" -BackupPath $backup -PlanSha256 $planHash
& "$backup\rollback.ps1" -BackupPath $backup -PlanSha256 $planHash -Apply
```

Pass the same `-ProbePath` when using a non-default staging copy. Rollback authenticates the private plan before importing the backed-up common helper, checks the manifest/tool pins and permitted target paths, and accepts only unchanged originals or this transaction's exact installed hashes. An unrelated target change stops restoration. No world, game installation or server files are restored.

## Validation boundary

Local checks can exercise parsing, manifest refusal, exact filesystem replacement/rollback and a baseline change before the first write. Windows ACL enforcement, real SDK/native compilation, loading the new client and riding remain separate gates for each actual release. No final release manifest or installable client is supplied by these tools alone.
