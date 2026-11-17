# Compila o Viridian em Release, executa os testes e gera o pacote de instalação (.zip).
# Uso (PowerShell, na raiz do repositório): .\build\package.ps1   (requer o SDK do .NET 10)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version | Select-Object -First 1
$name = "Viridian-$version-Revit2027"
$out = Join-Path 'dist' $name

dotnet test tests/Viridian.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
dotnet build src/Viridian.Addin -c Release
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path "dist/$name.zip") { Remove-Item -Force "dist/$name.zip" }
New-Item -ItemType Directory -Force -Path (Join-Path $out 'Viridian') | Out-Null

$bin = 'src/Viridian.Addin/bin/Release/net10.0-windows'
foreach ($f in 'Viridian.Addin', 'Viridian.Core', 'Viridian.Library', 'Viridian.Revit', 'Viridian.UI') {
    Copy-Item "$bin/$f.dll" (Join-Path $out 'Viridian')
    Copy-Item "$bin/$f.pdb" (Join-Path $out 'Viridian')
}
Copy-Item "$bin/Viridian.Addin.deps.json" (Join-Path $out 'Viridian')
Copy-Item -Recurse "$bin/Fotos" (Join-Path $out 'Viridian/Fotos')
Copy-Item "$bin/Viridian.addin" $out
Copy-Item 'deploy/Instalar.ps1', 'deploy/Instalar.cmd', 'deploy/Desinstalar.ps1', 'deploy/Desinstalar.cmd', 'deploy/LEIA-ME.txt' $out

if (Get-ChildItem (Join-Path $out 'Viridian') -Filter 'RevitAPI*') { throw 'DLL do Revit encontrada no pacote.' }

Compress-Archive -Path $out -DestinationPath "dist/$name.zip"
Write-Host "Pacote gerado: dist/$name.zip"
