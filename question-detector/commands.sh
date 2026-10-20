#!/usr/bin/env bash
# question-detector eğitim veri seti komutları (Mac). question-detector/ içinden:
#   ./commands.sh check    temiz başlangıç / hazır mı kontrolü (hiçbir şey silmez)
#   ./commands.sh clean    eski artıkları siler (data/, runs/, YOLOv8/, scripts/ ...) - dataset/ ve models/'e DOKUNMAZ
#   ./commands.sh status   birikmiş veri seti sayıları + son kayıtlar
#   ./commands.sh test     birim testleri (pillow + pytest gerekir)
#
# Eğitim Colab'da: training/weekly_training.ipynb (Drive > Othercomputers > ... > question-detector/training).
set -euo pipefail
cd "$(dirname "$0")"

LEGACY=(data runs YOLOv8 scripts prediction.json predictions.json yolov8n.pt __pycache__)

case "${1:-}" in
  check)
    python3 training/check_clean_start.py
    ;;

  clean)
    found=()
    for p in "${LEGACY[@]}"; do [ -e "$p" ] && found+=("$p"); done
    if [ ${#found[@]} -eq 0 ]; then
      echo "Silinecek eski artık yok."
      exit 0
    fi
    echo "Silinecekler ($(pwd)):"
    du -sh "${found[@]}"
    echo "dataset/ ve models/ korunur. Bu klasör Drive'a aynalanıyorsa Drive'dan da silinir."
    read -r -p "Silinsin mi? [e/H] " ans
    if [ "$ans" = "e" ] || [ "$ans" = "E" ]; then
      rm -rf -- "${found[@]}"
      echo "Silindi."
    else
      echo "Vazgeçildi."
    fi
    ;;

  status)
    pages=$( (ls dataset/questions/images/*.jpg 2>/dev/null || true) | wc -l | tr -d ' ')
    crops=$( (ls dataset/answers/images/*.jpg 2>/dev/null || true) | wc -l | tr -d ' ')
    echo "Sayfa (soru modeli): $pages   Soru kırpması (şık modeli): $crops"
    if [ -f dataset/manifest.jsonl ]; then
      echo "Son 5 kayıt:"
      tail -n 5 dataset/manifest.jsonl
    fi
    ;;

  test)
    python3 -m pytest -q tests
    ;;

  *)
    sed -n '2,8p' "$0"
    exit 1
    ;;
esac
