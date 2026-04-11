#!/bin/bash
# Keycloak tema değişikliklerini deploy klasörüne yansıtır.
# Kullanım: ./sync-theme.sh

set -e

SRC="keycloak-themes/my-theme"
DST="deploy/keycloak/keycloak-themes/my-theme"

rsync -av --delete "$SRC/" "$DST/"

echo ""
echo "✓ Tema senkronize edildi: $SRC → $DST"
