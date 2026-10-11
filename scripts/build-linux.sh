#!/usr/bin/env bash
# GameLocalizationSystem (GLS) - Linux Single-File Build Script
set -e

echo "=== GLS Linux (x64) Derleme Başlatılıyor ==="

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

cd "$ROOT_DIR"

OUTPUT_DIR="dist/GLS-linux-x64"

echo "1. xUnit Testleri Çalıştırılıyor..."
dotnet test src/LocalizePak.slnx -c Release

echo "2. Bağımsız Tek Dosya (Self-Contained) Derleniyor..."
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$OUTPUT_DIR"

chmod +x "$OUTPUT_DIR/GLS"

echo "=== Başarılı! Çalıştırılabilir dosya: $OUTPUT_DIR/GLS ==="
echo "Çalıştırmak için: ./$OUTPUT_DIR/GLS"
