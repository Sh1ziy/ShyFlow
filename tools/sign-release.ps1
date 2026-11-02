# Подписать установщик ключом из %USERPROFILE%\.shyflow-keys (пароль там защищён Windows и читается только этой учётной записью).
#   powershell -File tools\sign-release.ps1 ShyFlow-Setup.exe 3.9.8
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE '.shyflow-keys'
$secure = Get-Content (Join-Path $dir 'update.pass') | ConvertTo-SecureString
$env:SHYFLOW_SIGN_PASS = [Net.NetworkCredential]::new('', $secure).Password
try { node (Join-Path $PSScriptRoot 'update-sign.mjs') sign (Join-Path $dir 'update.key') $Exe $Tag }
finally { Remove-Item Env:SHYFLOW_SIGN_PASS -ErrorAction SilentlyContinue }
