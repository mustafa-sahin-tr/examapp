import os

def find_missing_folders(files_dir, folders_dir):
    # Get file names (without extension) from files_dir
    file_names = [os.path.splitext(f)[0] for f in os.listdir(files_dir) if os.path.isfile(os.path.join(files_dir, f))]
    # Get folder names from folders_dir
    folder_names = [f for f in os.listdir(folders_dir) if os.path.isdir(os.path.join(folders_dir, f))]
    # Find file names not matching any folder name
    # Trim whitespace from file and folder names
    trimmed_file_names = [name.strip() for name in file_names]
    trimmed_folder_names = [name.strip() for name in folder_names]
    # Find file names (trimmed) not matching any folder name (trimmed)
    missing = [name for name in trimmed_file_names if name not in trimmed_folder_names]
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