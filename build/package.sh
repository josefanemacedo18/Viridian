#!/usr/bin/env bash
# Compila o Viridian em Release, executa os testes e gera o pacote de instalação (.zip).
# Uso: build/package.sh   (requer o SDK do .NET 10)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
NAME="Viridian-${VERSION}-Revit2027"
OUT="dist/${NAME}"

dotnet test tests/Viridian.Tests -c Release
dotnet build src/Viridian.Addin -c Release

rm -rf "${OUT}" "dist/${NAME}.zip"
mkdir -p "${OUT}/Viridian"
BIN=src/Viridian.Addin/bin/Release/net10.0-windows
for f in Viridian.Addin Viridian.Core Viridian.Library Viridian.Revit Viridian.UI; do
  cp "${BIN}/${f}.dll" "${OUT}/Viridian/"
  cp "${BIN}/${f}.pdb" "${OUT}/Viridian/"
done
cp "${BIN}/Viridian.Addin.deps.json" "${OUT}/Viridian/"
mkdir -p "${OUT}/Viridian/Fotos"
cp "${BIN}"/Fotos/* "${OUT}/Viridian/Fotos/"
cp "${BIN}/Viridian.addin" "${OUT}/"
cp deploy/Instalar.ps1 deploy/Instalar.cmd deploy/Desinstalar.ps1 deploy/Desinstalar.cmd "${OUT}/"
cp deploy/LEIA-ME.txt "${OUT}/"

# Nenhuma DLL do Revit pode ser distribuída junto com o plugin.
if ls "${OUT}/Viridian" | grep -qi '^RevitAPI'; then
  echo "ERRO: DLL do Revit encontrada no pacote." >&2
  exit 1
fi

(cd dist && zip -qr "${NAME}.zip" "${NAME}")
echo "Pacote gerado: dist/${NAME}.zip"
