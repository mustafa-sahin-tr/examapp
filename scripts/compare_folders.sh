#!/usr/bin/env bash

# Usage: ./compare_folders.sh <folder_A> <folder_B>
# "/Users/mustafasahin/Downloads/WebPImages/4" "/Volumes/Mustafas HDD/HedefOkul/4/WebPImages"
if [ $# -ne 2 ]; then
    echo "Usage: $0 <folder_A> <folder_B>"
    exit 1
fi

DIR_A="$1"
DIR_B="$2"

if [ ! -d "$DIR_A" ]; then
    echo "Error: '$DIR_A' is not a directory"
    exit 1
fi

if [ ! -d "$DIR_B" ]; then
    echo "Error: '$DIR_B' is not a directory"
    exit 1
fi

echo "A: $DIR_A"
echo "B: $DIR_B"
echo ""

MOVED=0
DELETED=0
SKIPPED=0

while IFS= read -r -d '' subfolder_a; do
    name_a=$(basename "$subfolder_a")
    name_a_lower=$(echo "$name_a" | tr '[:upper:]' '[:lower:]')

    # Case-insensitive match in B
    match_b=""
    while IFS= read -r -d '' subfolder_b; do
        name_b=$(basename "$subfolder_b")
        name_b_lower=$(echo "$name_b" | tr '[:upper:]' '[:lower:]')
        if [ "$name_a_lower" = "$name_b_lower" ]; then
            match_b="$subfolder_b"
            break
        fi
    done < <(find "$DIR_B" -mindepth 1 -maxdepth 1 -type d -print0)

    if [ -z "$match_b" ]; then
        # Not in B → move A's folder to B
        mv "$subfolder_a" "$DIR_B/"
        echo "[MOVED  ] $name_a  → $DIR_B/"
        MOVED=$((MOVED + 1))
    else
        count_a=$(find "$subfolder_a" -maxdepth 1 -type f | wc -l | tr -d ' ')
        count_b=$(find "$match_b"     -maxdepth 1 -type f | wc -l | tr -d ' ')

        if [ "$count_a" -eq "$count_b" ]; then
            # Equal → delete from A
            rm -rf "$subfolder_a"
            echo "[DELETED] $name_a  ($count_a files, equal to B)"
            DELETED=$((DELETED + 1))
        else
            # Same name, different content → skip
            echo "[SKIPPED] $name_a  (A: $count_a files, B: $count_b files — content differs)"
            SKIPPED=$((SKIPPED + 1))
        fi
    fi
done < <(find "$DIR_A" -mindepth 1 -maxdepth 1 -type d -print0)

echo ""
echo "Moved: $MOVED  |  Deleted: $DELETED  |  Skipped: $SKIPPED"
