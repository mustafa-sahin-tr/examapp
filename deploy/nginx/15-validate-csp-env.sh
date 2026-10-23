#!/bin/sh
# Issue #333: CSP_JITSI_ORIGIN, nginx şablonunda script-src/frame-src içine ham olarak girer. Boş ya da tek bir
# https origin'i (isteğe bağlı port) olmalı — tırnak, boşluk ya da '*' politikayı gevşetir veya nginx yapılandırmasını bozar.
set -eu
value="${CSP_JITSI_ORIGIN:-}"
if [ -n "$value" ] && ! printf '%s' "$value" | grep -Eq '^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?$'; then
  echo "15-validate-csp-env: geçersiz CSP_JITSI_ORIGIN='$value' (beklenen: boş ya da https://host[:port])" >&2
  exit 1
fi
