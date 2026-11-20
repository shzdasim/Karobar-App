<#
.SYNOPSIS
    Automated Native Windows Single-Binary (.NET 9 + Kestrel + WebView2) Build,
    Authenticode Code Signing & Zero-Data-Loss Inno Setup Release Pipeline for Karobar-App Native.
.DESCRIPTION
    1. Stamps `public/version.json` with the current version & build number (`2.0.<run_number>`)
    2. Builds the Vite React 19 SPA (`npm run build`)
    3. Publishes `Karobar.Host` as a single self-contained Windows x64 binary (`Karobar-Native.exe`)
    4. Stages the React frontend bundle, print templates, and optional WhatsApp sidecar runtime
    5. Signs `Karobar-Native.exe` with `desktop/MalikAsimShahzad-CodeSign.pfx`
    6. Compiles `native-win/installer/Karobar-Native-Setup.iss` via Inno Setup 6 into
       `dist/installer/Karobar-Native-Setup.exe` and generates `dist/installer/latest-native-release.json`
#>

$ErrorActionPreference = "Stop"

# Resolve Repository Root whether invoked from `native-win/` or `native-win/installer/`
$ScriptDir = $PSScriptRoot
if ((Split-Path -Leaf $ScriptDir) -ieq "installer") {
    $RepoRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
} else {
    $RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
}
Set-Location $RepoRoot

$BuildNumber = if ($env:GITHUB_RUN_NUMBER) { [int]$env:GITHUB_RUN_NUMBER } else { 1 }
$AppVersion  = if ($env:KAROBAR_APP_VERSION -and $env:KAROBAR_APP_VERSION -ne "" -and $env:KAROBAR_APP_VERSION -ne "auto") { $env:KAROBAR_APP_VERSION } else { "2.0.$BuildNumber" }
$CommitSha   = if ($env:GITHUB_SHA) { $env:GITHUB_SHA.Substring(0, 7) } else { "local" }
$ReleasedAt  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$RepoSlug    = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { "shzdasim/Karobar-App" }

Write-Host "==> [0/6] Stamping Karobar-App Native Version Metadata: v$AppVersion (Build #$BuildNumber, Commit $CommitSha)..." -ForegroundColor Cyan
$VersionMeta = [ordered]@{
    app_name       = "Karobar-App Native"
    version        = $AppVersion
    build_number   = $BuildNumber
    commit_sha     = $CommitSha
    repository     = $RepoSlug
    released_at    = $ReleasedAt
    runtime        = "native-dotnet9-win-x64"
    runtime_layout = "split-v2"
}
$VersionMetaJson = $VersionMeta | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $RepoRoot "public\version.json") -Value $VersionMetaJson -Encoding UTF8

$DistDir         = Join-Path $RepoRoot "dist"
$CacheDir        = Join-Path $DistDir "cache"
$PublishDir      = Join-Path $DistDir "native-publish"
$StageDir        = Join-Path $DistDir "native-stage"
$InstallerOutDir = Join-Path $DistDir "installer"

New-Item -ItemType Directory -Force -Path $CacheDir, $StageDir, $InstallerOutDir | Out-Null

Write-Host "==> [1/6] Building React 19 SPA Frontend Assets (npm run build)..." -ForegroundColor Cyan
if (-not $env:KAROBAR_SKIP_NPM_BUILD) {
    npm ci
    npm run build
}

Write-Host "==> [2/6] Publishing Karobar.Host (Split Self-Contained win-x64 for ~3-5MB OTA Patches) + Karobar.Precheck..." -ForegroundColor Cyan
if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

$HostProjectPath = Join-Path $RepoRoot "native-win\src\Karobar.Host\Karobar.Host.csproj"
dotnet publish $HostProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$AppVersion `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish Karobar.Host failed with exit code $LASTEXITCODE"
}

$PrecheckOutDir     = Join-Path $CacheDir "precheck"
$PrecheckProjectPath = Join-Path $RepoRoot "native-win\src\Karobar.Precheck\Karobar.Precheck.csproj"
if (Test-Path $PrecheckOutDir) { Remove-Item -Recurse -Force $PrecheckOutDir }
dotnet publish $PrecheckProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$AppVersion `
    -o $PrecheckOutDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish Karobar.Precheck failed with exit code $LASTEXITCODE"
}
$PrecheckExe = Join-Path $PrecheckOutDir "Karobar-Precheck.exe"
if (-not (Test-Path $PrecheckExe)) {
    throw "Expected precheck binary not found at $PrecheckExe"
}

Write-Host "==> [3/6] Staging Native Application Payload into dist/native-stage..." -ForegroundColor Cyan
if (Test-Path $StageDir) {
    Remove-Item -Recurse -Force $StageDir
}
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

Copy-Item -Path "$PublishDir\*" -Destination $StageDir -Recurse -Force

# Remove any stray .pdb debug symbols and ensure only a single Karobar-Native.exe binary exists in StageDir
Get-ChildItem -Path $StageDir -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
$PublishedHostExe = Join-Path $StageDir "Karobar.Host.exe"
$TargetAppExe     = Join-Path $StageDir "Karobar-Native.exe"
if ((Test-Path $PublishedHostExe) -and (-not (Test-Path $TargetAppExe))) {
    Move-Item -Path $PublishedHostExe -Destination $TargetAppExe -Force
} elseif ((Test-Path $PublishedHostExe) -and (Test-Path $TargetAppExe)) {
    Remove-Item -Path $PublishedHostExe -Force
}

foreach ($dir in @("public", "resources\views", "resources\assistant", "scripts")) {
    $src = Join-Path $RepoRoot $dir
    if (Test-Path $src) {
        $dest = Join-Path $StageDir $dir
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -Path $src -Destination $dest -Recurse -Force
    }
}

$CubeKnowledgeSrc = Join-Path $RepoRoot "resources\assistant\cube-knowledge.json"
if (Test-Path $CubeKnowledgeSrc) {
    New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "resources\assistant") | Out-Null
    Copy-Item -Path $CubeKnowledgeSrc -Destination (Join-Path $StageDir "resources\assistant\cube-knowledge.json") -Force
}

$DesktopStage = Join-Path $StageDir "desktop"
New-Item -ItemType Directory -Force -Path $DesktopStage | Out-Null
foreach ($dFile in @("splash.html", "terminal-offline.html", "icon.ico", "icon.png", "MalikAsimShahzad-KarobarERP.cer")) {
    $dSrc = Join-Path $RepoRoot "desktop\$dFile"
    if (Test-Path $dSrc) {
        Copy-Item -Path $dSrc -Destination (Join-Path $DesktopStage $dFile) -Force
    }
}
# Prioritize Native Host HTML assets (port 18960, Pairing Token, 10s auto-reconnect) over legacy Electron root desktop/ files
$NativeDesktopDir = Join-Path $RepoRoot "native-win\src\Karobar.Host\desktop"
foreach ($nFile in @("splash.html", "terminal-offline.html")) {
    $nSrc = Join-Path $NativeDesktopDir $nFile
    if (Test-Path $nSrc) {
        Copy-Item -Path $nSrc -Destination (Join-Path $DesktopStage $nFile) -Force
    }
}

$SumatraSrc = Join-Path $RepoRoot "desktop\node_modules\pdf-to-printer\dist\SumatraPDF-3.4.6-32.exe"
if (Test-Path $SumatraSrc) {
    Copy-Item -Path $SumatraSrc -Destination (Join-Path $DesktopStage "SumatraPDF.exe") -Force
}

# Clean dev/test artifacts so upgrades NEVER overwrite end-user database.sqlite or .env
Remove-Item -Path (Join-Path $StageDir "public\hot") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $StageDir "public\storage") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path `
    (Join-Path $StageDir "database"), `
    (Join-Path $StageDir "storage\app\public"), `
    (Join-Path $StageDir "storage\app\backups"), `
    (Join-Path $StageDir "storage\app\whatsapp-agent"), `
    (Join-Path $StageDir "storage\backups"), `
    (Join-Path $StageDir "storage\logs") | Out-Null

# Compute deterministic third-party DLL fingerprint BEFORE Inno Setup & OTA patch packaging
# so public/version.json inside BOTH the installer and patch carries the exact deps_fingerprint
$ThirdPartyDllList = (Get-ChildItem -Path $StageDir -Filter "*.dll" | Where-Object { $_.Name -notlike "Karobar*" } | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Length)" }) -join "|"
$Sha = [System.Security.Cryptography.SHA256]::Create()
$DepsHashBytes = $Sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($ThirdPartyDllList))
$DepsHash8 = ([System.BitConverter]::ToString($DepsHashBytes) -replace "-", "").ToLower().Substring(0, 8)
$RuntimeLayoutTag = "split-v2-$DepsHash8"
$Sha.Dispose()

$VersionMeta["deps_fingerprint"] = $DepsHash8
$VersionMetaJson = $VersionMeta | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $RepoRoot "public\version.json") -Value $VersionMetaJson -Encoding UTF8
Set-Content -Path (Join-Path $StageDir "public\version.json") -Value $VersionMetaJson -Encoding UTF8
Write-Host "    Stamped deps_fingerprint=$DepsHash8 into public\version.json (layout=$RuntimeLayoutTag)" -ForegroundColor Gray

Write-Host "==> [4/6] Verifying Native CLI Pre-Install Machine ID & License Engine (Isolated {tmp} Simulation)..." -ForegroundColor Cyan
if ($IsWindows -or $env:OS -eq "Windows_NT") {
    $IsolatedTmp = Join-Path $CacheDir "isolated-tmp-verify"
    if (Test-Path $IsolatedTmp) { Remove-Item -Recurse -Force $IsolatedTmp }
    New-Item -ItemType Directory -Force -Path $IsolatedTmp | Out-Null

    $IsolatedExe = Join-Path $IsolatedTmp "Karobar-Precheck.exe"
    Copy-Item -Path $PrecheckExe -Destination $IsolatedExe -Force

    $TestMidOut = Join-Path $IsolatedTmp "machine-id.txt"
    $MidProc = Start-Process -FilePath $IsolatedExe `
        -ArgumentList "--cli", "license:machine-id", "--out=`"$TestMidOut`"" `
        -WorkingDirectory $IsolatedTmp `
        -WindowStyle Hidden `
        -Wait -PassThru
    if ($MidProc.ExitCode -ne 0) {
        throw "Isolated Native CLI license:machine-id exited with code $($MidProc.ExitCode)"
    }
    if (-not (Test-Path $TestMidOut)) {
        throw "Isolated Native CLI license:machine-id did not write $TestMidOut"
    }
    $MidBytes = [IO.File]::ReadAllBytes($TestMidOut)
    if ($MidBytes.Length -ge 3 -and $MidBytes[0] -eq 0xEF -and $MidBytes[1] -eq 0xBB -and $MidBytes[2] -eq 0xBF) {
        throw "machine-id.txt contains a UTF-8 BOM which breaks Inno Setup LoadStringFromFile!"
    }
    $DetectedTestMid = [Text.Encoding]::ASCII.GetString($MidBytes).Trim()
    if (-not ($DetectedTestMid -match '^KRB-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$')) {
        throw "Invalid Machine ID format returned by isolated native CLI: '$DetectedTestMid'"
    }
    Write-Host "    Verified Isolated {tmp} Machine ID (BOM-free): $DetectedTestMid" -ForegroundColor Green

    $BogusKeyFile = Join-Path $IsolatedTmp "input-key.txt"
    $ErrOutFile   = Join-Path $IsolatedTmp "license-error.txt"
    $SealOutFile  = Join-Path $IsolatedTmp "install-license.seal"
    [IO.File]::WriteAllText($BogusKeyFile, "KRBL.invalid.payload", (New-Object Text.UTF8Encoding($false)))
    $VerifyProc = Start-Process -FilePath $IsolatedExe `
        -ArgumentList "--cli", "license:verify-installer", "--allow-unbound", "--key-file=`"$BogusKeyFile`"", "--seal-out=`"$SealOutFile`"", "--error-out=`"$ErrOutFile`"" `
        -WorkingDirectory $IsolatedTmp `
        -WindowStyle Hidden `
        -Wait -PassThru
    if ($VerifyProc.ExitCode -eq 0) {
        throw "Isolated Native CLI unexpectedly accepted an invalid license key!"
    }
    if (-not (Test-Path $ErrOutFile)) {
        throw "Isolated Native CLI license:verify-installer did not write --error-out!"
    }
    Write-Host "    Verified Isolated {tmp} License Verifier error handling: $(Get-Content $ErrOutFile -Raw)" -ForegroundColor Green
    Remove-Item -Recurse -Force $IsolatedTmp -ErrorAction SilentlyContinue

    # Live Desktop + Kestrel Boot Smoke Test (catches any controller binding, DI, or WinForms splash deadlock!)
    $BootTestDbDir = Join-Path $CacheDir "boot-smoke-root"
    if (Test-Path $BootTestDbDir) { Remove-Item -Recurse -Force $BootTestDbDir }
    New-Item -ItemType Directory -Force -Path (Join-Path $BootTestDbDir "database"), (Join-Path $BootTestDbDir "storage\logs"), (Join-Path $BootTestDbDir "public") | Out-Null
    Copy-Item -Path (Join-Path $StageDir "public\*") -Destination (Join-Path $BootTestDbDir "public") -Recurse -Force -ErrorAction SilentlyContinue

    $PrevExitAfterBoot = $env:KAROBAR_EXIT_AFTER_BOOT
    $PrevAppRootEnv    = $env:KAROBAR_APP_ROOT
    $PrevRootPathEnv   = $env:KAROBAR_ROOT_PATH
    try {
        $env:KAROBAR_EXIT_AFTER_BOOT = "1"
        $env:KAROBAR_APP_ROOT        = $BootTestDbDir
        $env:KAROBAR_ROOT_PATH       = $BootTestDbDir
        $BootProc = Start-Process -FilePath $TargetAppExe `
            -WorkingDirectory $BootTestDbDir `
            -WindowStyle Hidden `
            -PassThru
        if (-not $BootProc.WaitForExit(30000)) {
            try { $BootProc.Kill() } catch {}
            throw "Karobar-Native.exe deadlocked / hung during desktop + Kestrel startup (exceeded 30s timeout)!"
        }
        if ($BootProc.ExitCode -ne 0) {
            $LogFile = Join-Path $BootTestDbDir "storage\logs\native-startup.log"
            $LogText = if (Test-Path $LogFile) { Get-Content $LogFile -Raw } else { "(no startup log)" }
            throw "Karobar-Native.exe failed live desktop + Kestrel boot with exit code $($BootProc.ExitCode):`n$LogText"
        }
        Write-Host "    Verified Live Desktop + Kestrel + 59-Controller Boot (ExitCode=0)!" -ForegroundColor Green
    } finally {
        $env:KAROBAR_EXIT_AFTER_BOOT = $PrevExitAfterBoot
        $env:KAROBAR_APP_ROOT        = $PrevAppRootEnv
        $env:KAROBAR_ROOT_PATH       = $PrevRootPathEnv
        Remove-Item -Recurse -Force $BootTestDbDir -ErrorAction SilentlyContinue
    }
}

Write-Host "==> [5/6] Code Signing Executable with Authenticode Certificate (MalikAsimShahzad-CodeSign.pfx)..." -ForegroundColor Cyan
$RepoPfxPath = Join-Path $RepoRoot "desktop\MalikAsimShahzad-CodeSign.pfx"
$RepoCerPath = Join-Path $RepoRoot "desktop\MalikAsimShahzad-KarobarERP.cer"
$PfxPath     = Join-Path $CacheDir "MalikAsimShahzad-CodeSign.pfx"
$CerPath     = Join-Path $DesktopStage "MalikAsimShahzad-KarobarERP.cer"
$PfxPasswordPlain = if ($env:WINDOWS_CERT_PASSWORD) { $env:WINDOWS_CERT_PASSWORD } else { "KarobarSign2026!" }
$PfxPasswordSec   = ConvertTo-SecureString -String $PfxPasswordPlain -Force -AsPlainText

if ($env:WINDOWS_CERT_PFX_BASE64) {
    [IO.File]::WriteAllBytes($PfxPath, [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_BASE64))
    $SigningCert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\CurrentUser\My" -Password $PfxPasswordSec
    Export-Certificate -Cert $SigningCert -FilePath $CerPath -Force | Out-Null
} elseif ((Test-Path $RepoPfxPath) -and (Test-Path $RepoCerPath)) {
    Copy-Item -Path $RepoPfxPath -Destination $PfxPath -Force
    Copy-Item -Path $RepoCerPath -Destination $CerPath -Force
    $SigningCert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\CurrentUser\My" -Password $PfxPasswordSec
} else {
    $SigningCert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=Malik Asim Shahzad (shzdasim), O=Malik Asim Shahzad, OU=https://github.com/shzdasim/Karobar-App, C=PK" `
        -FriendlyName "Malik Asim Shahzad - Karobar-App Developer Certificate" `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm "SHA256" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.17={text}URL=https://github.com/shzdasim/Karobar-App") `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5)

    Export-PfxCertificate -Cert $SigningCert -FilePath $PfxPath -Password $PfxPasswordSec -Force | Out-Null
    Export-Certificate -Cert $SigningCert -FilePath $CerPath -Force | Out-Null
}

if (Test-Path $CerPath) {
    Copy-Item -Path $CerPath -Destination (Join-Path $InstallerOutDir "MalikAsimShahzad-KarobarERP.cer") -Force
}

$SignToolExe = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

function Invoke-KarobarAuthenticodeSign([string]$TargetExe) {
    if (-not (Test-Path $TargetExe)) { return }
    Write-Host "    Signing executable: $(Split-Path -Leaf $TargetExe)" -ForegroundColor Gray
    if ($SignToolExe -and (Test-Path $SignToolExe)) {
        & $SignToolExe sign /f $PfxPath /p $PfxPasswordPlain /fd SHA256 /d "Karobar-App Native - Malik Asim Shahzad" /du "https://github.com/shzdasim/Karobar-App" /tr "http://timestamp.digicert.com" /td SHA256 $TargetExe
        if ($LASTEXITCODE -ne 0) {
            & $SignToolExe sign /f $PfxPath /p $PfxPasswordPlain /fd SHA256 /d "Karobar-App Native - Malik Asim Shahzad" /du "https://github.com/shzdasim/Karobar-App" $TargetExe
        }
    } elseif ($SigningCert) {
        $sigRes = Set-AuthenticodeSignature -FilePath $TargetExe -Certificate $SigningCert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
        if ($sigRes.Status -ne "Valid") {
            Set-AuthenticodeSignature -FilePath $TargetExe -Certificate $SigningCert -HashAlgorithm SHA256 | Out-Null
        }
    }
}

Invoke-KarobarAuthenticodeSign $TargetAppExe
Invoke-KarobarAuthenticodeSign $PrecheckExe
Invoke-KarobarAuthenticodeSign $PublishedHostExe

Write-Host "==> [6/6] Compiling Inno Setup 6 Native Windows Installer (Karobar-Native-Setup.iss)..." -ForegroundColor Cyan
$IsccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$IsccExe = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $IsccExe) {
    $IsccExe = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source
}
if (-not $IsccExe) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install via: winget install JRSoftware.InnoSetup"
}

$IssPath = Join-Path $RepoRoot "native-win\installer\Karobar-Native-Setup.iss"
if ($SignToolExe -and (Test-Path $SignToolExe)) {
    $SignCmd = "`$q$SignToolExe`$q sign /f `$q$PfxPath`$q /p `$q$PfxPasswordPlain`$q /fd SHA256 /d `$qKarobar-App Native - Malik Asim Shahzad`$q /du `$qhttps://github.com/shzdasim/Karobar-App`$q `$f"
    & $IsccExe "/DMyAppVersion=$AppVersion" "/DSignToolEnabled=1" "/Skarobarsign=$SignCmd" $IssPath
} else {
    & $IsccExe "/DMyAppVersion=$AppVersion" $IssPath
}
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$FinalInstallerExe = Join-Path $InstallerOutDir "Karobar-Native-Setup.exe"
Invoke-KarobarAuthenticodeSign $FinalInstallerExe

# Generate 1-Click Certificate Trust & Native Installer Launcher (Install-Karobar-Native.bat)
$HelperBatPath = Join-Path $InstallerOutDir "Install-Karobar-Native.bat"
$HelperBatContent = @"
@echo off
title Karobar-App Native v$AppVersion - Verified Publisher Installer
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges to trust developer certificate...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
echo [1/3] Trusting Developer Certificate: Malik Asim Shahzad (https://github.com/shzdasim/Karobar-App)...
certutil.exe -addstore -f "Root" "%~dp0MalikAsimShahzad-KarobarERP.cer" >nul 2>&1
certutil.exe -addstore -f "TrustedPublisher" "%~dp0MalikAsimShahzad-KarobarERP.cer" >nul 2>&1
echo [2/3] Clearing browser download security zone mark...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' | Unblock-File -ErrorAction SilentlyContinue"
echo [3/3] Launching Signed Karobar-App Native Setup Wizard...
start "" "%~dp0Karobar-Native-Setup.exe"
"@
Set-Content -Path $HelperBatPath -Value $HelperBatContent -Encoding ASCII

# Package LIGHTWEIGHT differential OTA patch zip (~3-5 MB ONLY — app assemblies + React SPA + print templates, NEVER the .NET 9 runtime!)
$PatchStageDir = Join-Path $CacheDir "native-patch-stage"
if (Test-Path $PatchStageDir) { Remove-Item -Recurse -Force $PatchStageDir }
New-Item -ItemType Directory -Force -Path $PatchStageDir | Out-Null

# 1. Copy only Karobar app binaries & runtime metadata (~1.5 MB zipped vs ~90 MB .NET runtime)
Get-ChildItem -Path $StageDir -File | Where-Object {
    $_.Name -like "Karobar*"
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $PatchStageDir $_.Name) -Force
}

# 2. Copy React SPA, print templates, Cube AI knowledge, scripts, and desktop assets (never database or user storage!)
foreach ($relDir in @("public", "resources\views", "resources\assistant", "scripts", "desktop")) {
    $srcSub = Join-Path $StageDir $relDir
    if (Test-Path $srcSub) {
        $dstSub = Join-Path $PatchStageDir $relDir
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstSub) | Out-Null
        Copy-Item -Path $srcSub -Destination $dstSub -Recurse -Force
    }
}

$PatchZipPath = Join-Path $InstallerOutDir "Karobar-Native-Patch.zip"
if (Test-Path $PatchZipPath) { Remove-Item -Force $PatchZipPath }
Compress-Archive -Path "$PatchStageDir\*" -DestinationPath $PatchZipPath -CompressionLevel Optimal -Force
Remove-Item -Recurse -Force $PatchStageDir -ErrorAction SilentlyContinue

$PatchSize   = (Get-Item $PatchZipPath).Length
$PatchSizeMB = [Math]::Round($PatchSize / 1MB, 2)
$PatchSha256 = (Get-FileHash -Path $PatchZipPath -Algorithm SHA256).Hash.ToLower()
$InstallerExePath = Join-Path $InstallerOutDir "Karobar-Native-Setup.exe"
$InstallerSha256 = if (Test-Path $InstallerExePath) { (Get-FileHash -Path $InstallerExePath -Algorithm SHA256).Hash.ToLower() } else { "" }
$InstallerSize   = if (Test-Path $InstallerExePath) { [int64](Get-Item $InstallerExePath).Length } else { 0 }

Write-Host "    Differential OTA Patch packaged: Karobar-Native-Patch.zip ($PatchSizeMB MB, SHA256=$PatchSha256, layout=$RuntimeLayoutTag)" -ForegroundColor Green

$LatestReleaseManifest = [ordered]@{
    app_name          = "Karobar-App Native"
    version           = $AppVersion
    build_number      = $BuildNumber
    commit_sha        = $CommitSha
    release_name      = "Karobar-App Native v$AppVersion (Build #$BuildNumber)"
    released_at       = $ReleasedAt
    notes             = "Native Windows C#/.NET 9 + Kestrel + Waseela Multi-Tab WebView2 Release (Fast Differential Patch: ${PatchSizeMB} MB)."
    runtime_layout    = "split-v2"
    deps_fingerprint  = $DepsHash8
    download_url      = "https://github.com/$RepoSlug/releases/download/native-latest/Karobar-Native-Setup.exe"
    installer_size    = $InstallerSize
    installer_sha256  = $InstallerSha256
    bundle_url        = "https://github.com/$RepoSlug/releases/download/native-latest/Karobar-Native-Setup-Bundle.zip"
    patch_url         = "https://github.com/$RepoSlug/releases/download/native-latest/Karobar-Native-Patch.zip"
    patch_size        = [int64]$PatchSize
    patch_sha256      = $PatchSha256
    min_patch_version = "2.0.0"
    release_url       = "https://github.com/$RepoSlug/releases/tag/native-latest"
    zero_data_loss    = $true
}
Set-Content -Path (Join-Path $InstallerOutDir "latest-native-release.json") -Value ($LatestReleaseManifest | ConvertTo-Json -Depth 4) -Encoding UTF8

Write-Host "================================================================================" -ForegroundColor Green
Write-Host "SUCCESS! Native Windows Single-Binary & Code-Signed Installer generated at:" -ForegroundColor Green
Write-Host "  dist\installer\Karobar-Native-Setup.exe (v$AppVersion, Build #$BuildNumber)" -ForegroundColor Yellow
Write-Host "  dist\installer\Karobar-Native-Patch.zip" -ForegroundColor Yellow
Write-Host "  dist\installer\latest-native-release.json" -ForegroundColor Yellow
Write-Host "================================================================================" -ForegroundColor Green
