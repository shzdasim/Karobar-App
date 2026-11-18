$ErrorActionPreference = "Stop"
$ParentScript = Join-Path (Split-Path -Parent $PSScriptRoot) "build-native-win.ps1"
& $ParentScript @args
