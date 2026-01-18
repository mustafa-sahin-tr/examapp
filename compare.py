import os
import unicodedata


def tr_casefold(value: str) -> str:
    """Turkish-aware, case-insensitive normalization for comparisons.

    Python's default lower/casefold is locale-independent and does not handle
    Turkish I/İ/ı/i the way Turkish users expect. This helper applies Turkish
    specific mappings and then lowercases.
    """
    if value is None:
        return ""

    # Normalize first to reduce surprises with composed/decomposed forms.
    value = unicodedata.normalize("NFC", value)

    # Turkish-specific case mapping:
    # - 'I' (LATIN CAPITAL LETTER I) -> 'ı' (LATIN SMALL LETTER DOTLESS I)
    # - 'İ' (LATIN CAPITAL LETTER I WITH DOT ABOVE) -> 'i'
    value = value.replace("I", "ı").replace("İ", "i")
    return value.lower()

def find_missing_folders(files_dir, folders_dir):
    # Get file names (without extension) from files_dir
    file_names = [os.path.splitext(f)[0] for f in os.listdir(files_dir) if os.path.isfile(os.path.join(files_dir, f))]
    # Get folder names from folders_dir
    folder_names = [f for f in os.listdir(folders_dir) if os.path.isdir(os.path.join(folders_dir, f))]
    # Find file names not matching any folder name
    # Trim whitespace from file and folder names
    trimmed_file_names = [name.strip() for name in file_names]
    trimmed_folder_names = [name.strip() for name in folder_names]

    # Case-insensitive compare WITHOUT ignoring Turkish characters.
    # (e.g. "I" != "İ" after Turkish-aware folding, but "Ş" == "ş").
    folded_folder_names = {tr_casefold(name) for name in trimmed_folder_names}
    missing = [name for name in trimmed_file_names if tr_casefold(name) not in folded_folder_names]
    return missing

# Example usage
files_directory = '/Users/mustafasahin/Documents/Ozum/Flip/4'
folders_directory = '/Users/mustafasahin/Downloads/WebPImages/4'

missing_folders = find_missing_folders(files_directory, folders_directory)
print("Folders missing for these files:")
# for idx, name in enumerate(missing_folders, start=1):
#     print(f"{idx}. {name}")
# print(f"Toplam: {len(missing_folders)} dosya/folder eksik.")
print(missing_folders)