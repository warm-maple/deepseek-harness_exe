param(
    [string]$Version = "0.1.0",
    [switch]$SkipInstall,
    [switch]$SkipHostBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactRoot = Join-Path $repoRoot ".artifacts\desktop"
$payloadRoot = Join-Path $artifactRoot "payload"
$runtimeRoot = Join-Path $payloadRoot "runtime"
$toolRoot = Join-Path $repoRoot ".artifacts\tools"
$dotnetRoot = Join-Path $toolRoot "dotnet"
$innoRoot = Join-Path $toolRoot "inno"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE."
    }
}

Set-Location $repoRoot
if (-not $SkipInstall -and -not (Test-Path (Join-Path $repoRoot "node_modules"))) {
    Invoke-Checked "pnpm" @("install", "--frozen-lockfile")
}

if (-not $SkipHostBuild) {
    Invoke-Checked "pnpm" @("run", "build:lib:host")
}
Invoke-Checked "pnpm" @(
    "exec", "tsx", "scripts/verify-runtime-closure.ts",
    "--manifest", "distribution/desktop-runtime/package.json"
)

New-Item -ItemType Directory -Force -Path $artifactRoot, $toolRoot | Out-Null
if (Test-Path $payloadRoot) {
    Remove-Item -LiteralPath $payloadRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$hasNet8Sdk = $false
if ($dotnet) {
    $hasNet8Sdk = (& $dotnet.Source --list-sdks) -match '^8\.'
}
if (-not $hasNet8Sdk) {
    $dotnetExe = Join-Path $dotnetRoot "dotnet.exe"
    if (-not (Test-Path $dotnetExe)) {
        New-Item -ItemType Directory -Force -Path $dotnetRoot | Out-Null
        $installer = Join-Path $toolRoot "dotnet-install.ps1"
        Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
        & $installer -Channel "8.0" -Architecture "x64" -InstallDir $dotnetRoot -NoPath
        if ($LASTEXITCODE -ne 0) {
            throw "The .NET 8 SDK bootstrap failed."
        }
    }
    $dotnetPath = $dotnetExe
} else {
    $dotnetPath = $dotnet.Source
}

Invoke-Checked $dotnetPath @(
    "publish", "apps/desktop/DeepSeekHarness.Desktop.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:Version=$Version",
    "-o", $payloadRoot
)

Invoke-Checked "pnpm" @(
    "--filter", "dsh-desktop-runtime",
    "deploy", $runtimeRoot,
    "--legacy", "--prod",
    "--config.node-linker=hoisted",
    "--config.auto-install-peers=false",
    "--config.link-workspace-packages=true"
)
Invoke-Checked "node" @(
    "node_modules/tsx/dist/cli.mjs", "scripts/materialize-desktop-runtime.ts",
    "--runtime", ".artifacts/desktop/payload/runtime",
    "--source", "distribution/desktop-runtime/node_modules"
)
# Legacy deploy leaves its source workspace in production-only install state.
# Restore the checkout before subsequent development commands run.
Invoke-Checked "pnpm" @(
    "install", "--frozen-lockfile", "--offline",
    "--config.confirmModulesPurge=false"
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
Copy-Item -LiteralPath $nodePath -Destination (Join-Path $runtimeRoot "node.exe") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "apps\desktop\runtime\cordis.yml") -Destination $runtimeRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "apps\desktop\runtime\presets") -Destination $runtimeRoot -Recurse -Force

$entryPath = Join-Path $runtimeRoot "node_modules\@deepseek-ai\dsh-acp-demo\lib\bin.js"
if (-not (Test-Path $entryPath)) {
    throw "The deployed desktop runtime is missing $entryPath."
}

if (-not $SkipInstaller) {
    $iscc = Join-Path $innoRoot "ISCC.exe"
    if (-not (Test-Path $iscc)) {
        New-Item -ItemType Directory -Force -Path $innoRoot | Out-Null
        $innoInstaller = Join-Path $toolRoot "innosetup-6.7.3.exe"
        if (-not (Test-Path $innoInstaller)) {
            Invoke-WebRequest "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe" -OutFile $innoInstaller
        }
        Invoke-Checked $innoInstaller @(
            "/CURRENTUSER", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
            "/DIR=$innoRoot"
        )
        for ($attempt = 0; $attempt -lt 150 -and -not (Test-Path $iscc); $attempt++) {
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path $iscc)) {
            throw "Inno Setup finished without creating $iscc."
        }
    }
    Invoke-Checked $iscc @(
        "/DAppVersion=$Version",
        (Join-Path $repoRoot "apps\desktop\installer\DeepSeekHarness.iss")
    )
}

Write-Host "Desktop payload: $payloadRoot"
if (-not $SkipInstaller) {
    Write-Host "Installer: $(Join-Path $artifactRoot "DeepSeekHarness-Setup-$Version-x64.exe")"
}
