#!/usr/bin/env bash

# Usage: ./remove_small_folders.sh <folder>

if [ $# -ne 1 ]; then
    echo "Usage: $0 <folder>"
    exit 1
fi

DIR="$1"

if [ ! -d "$DIR" ]; then
    echo "Error: '$DIR' is not a directory"
    exit 1
fi

echo "Scanning: $DIR"
echo ""

DELETED=0
KEPT=0

while IFS= read -r -d '' subfolder; do
    name=$(basename "$subfolder")
    count=$(find "$subfolder" -maxdepth 1 -type f | wc -l | tr -d ' ')

    if [ "$count" -lt 8 ]; then
        rm -rf "$subfolder"
        echo "[DELETED] $name  ($count files)"
        DELETED=$((DELETED + 1))
    else
        echo "[KEPT   ] $name  ($count files)"
        KEPT=$((KEPT + 1))
    fi
done < <(find "$DIR" -mindepth 1 -maxdepth 1 -type d -print0)

echo ""
echo "Deleted: $DELETED  |  Kept: $KEPT"
