<#
    Instala o Viridian para o usuário atual no Autodesk Revit 2027.
    Uso: clique com o botão direito em Instalar.cmd e escolha "Executar" (não exige administrador).
#>
$ErrorActionPreference = 'Stop'
$revitVersion = '2027'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$revitVersion"
$target = Join-Path $addinsRoot 'Viridian'

Write-Host "Viridian — instalação para Revit $revitVersion" -ForegroundColor Green

$running = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Feche o Revit antes de instalar (os arquivos do plugin ficam em uso enquanto ele está aberto).' -ForegroundColor Yellow
    exit 1
}

$payload = Join-Path $source 'Viridian'
if (-not (Test-Path (Join-Path $payload 'Viridian.Addin.dll'))) {
    Write-Host "Pasta 'Viridian' não encontrada ao lado deste instalador. Extraia o arquivo .zip completo antes de instalar." -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $addinsRoot | Out-Null
if (Test-Path $target) {
    Remove-Item -Recurse -Force $target
}

Copy-Item -Recurse -Force $payload $target
Copy-Item -Force (Join-Path $source 'Viridian.addin') (Join-Path $addinsRoot 'Viridian.addin')

# Arquivos baixados da internet ficam bloqueados pelo Windows; o desbloqueio evita avisos ao carregar o plugin.
Get-ChildItem -Recurse $target | Unblock-File -ErrorAction SilentlyContinue
Unblock-File (Join-Path $addinsRoot 'Viridian.addin') -ErrorAction SilentlyContinue

Write-Host "Instalado em: $target" -ForegroundColor Green
Write-Host 'Abra o Revit 2027: a aba "Viridian" aparece na faixa de opções.'
