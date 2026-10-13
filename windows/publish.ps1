# Publishes a self-contained, single-file Deskworlds.exe plus the web assets into dist\.
# dotnet publish has been observed to silently drop content files larger than ~256 KB
# when copying them to the output directory, so the web trees are mirrored with robocopy
# afterwards to guarantee the full set (three.js, reefscape binary assets, ...).
param(
    [string]$Configuration = "Release",
    [string]$Output = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot                    # windows\ -> repo root
$project = Join-Path $PSScriptRoot "Deskworlds\Deskworlds.csproj"
$dist = Join-Path $root $Output

$dotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$env:DOTNET_ROOT = Split-Path $dotnet

& $dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $dist --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

# Mirror the web trees in; /XD keeps test folders out of the package.
foreach ($pair in @(@("scenes", "tests"), @("vendor", $null), @("ui", "tests"))) {
    $src = Join-Path $root $pair[0]
    $dst = Join-Path $dist $pair[0]
    $args = @($src, $dst, "/MIR", "/NFL", "/NDL", "/NJH", "/NJS", "/NP")
    if ($pair[1]) { $args += @("/XD", $pair[1]) }
    robocopy @args | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $($pair[0]) failed with $LASTEXITCODE" }
}

# Publish leftovers that have no business in a user-facing package.
Get-ChildItem $dist -Recurse -Include *.pdb, *.xml -File | Remove-Item -Force

$exe = Get-Item (Join-Path $dist "Deskworlds.exe")
$files = (Get-ChildItem $dist -Recurse -File | Measure-Object).Count
$totalMb = [math]::Round((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Published: $($exe.FullName) ($([math]::Round($exe.Length/1MB,1)) MB), $files files, $totalMb MB total"
