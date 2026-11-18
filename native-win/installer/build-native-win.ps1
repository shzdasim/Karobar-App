<#
.SYNOPSIS
    Wrapper build script in `native-win/installer/build-native-win.ps1` delegating to `native-win/build-native-win.ps1`.
#>
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "..\build-native-win.ps1") @args
