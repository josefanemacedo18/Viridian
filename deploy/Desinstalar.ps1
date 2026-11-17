<#
    Remove o Viridian do Revit 2027 para o usuário atual.
    A biblioteca botânica, as configurações e as famílias geradas (em %APPDATA%\Viridian) são mantidas.
#>
$ErrorActionPreference = 'Stop'
$revitVersion = '2027'
$addinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$revitVersion"

$running = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Feche o Revit antes de desinstalar.' -ForegroundColor Yellow
    exit 1
}

$manifest = Join-Path $addinsRoot 'Viridian.addin'
$folder = Join-Path $addinsRoot 'Viridian'
if (Test-Path $manifest) { Remove-Item -Force $manifest }
if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }

Write-Host 'Viridian removido do Revit 2027.' -ForegroundColor Green
Write-Host "Seus dados continuam em: $(Join-Path $env:APPDATA 'Viridian')"
