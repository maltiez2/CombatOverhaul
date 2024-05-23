import os
import re
import glob

def replace_in_file(file_path, pattern, replacement):
    try:
        with open(file_path, "r", encoding="utf-8") as f:
            content = f.read()

        new_content = re.sub(pattern, replacement, content)

        if new_content != content:
            with open(file_path, "w", encoding="utf-8") as f:
                f.write(new_content)
            print(f"Updated: {file_path}")

    except (UnicodeDecodeError, PermissionError):
        print(f"Skipped: {file_path}")


def replace_in_folder(folder_pattern, pattern, replacement):
    matching_paths = glob.glob(folder_pattern, recursive=True)

    if not matching_paths:
        print("No folders matched pattern:", folder_pattern)
        return

    for folder_path in matching_paths:
        if not os.path.isdir(folder_path):
            continue

        #print(f"Scanning folder: {folder_path}")
        for root, _, files in os.walk(folder_path):
            for name in files:
                file_path = os.path.join(root, name)
                replace_in_file(file_path, pattern, replacement)


if __name__ == "__main__":
    replace_in_folder("../../resources/assets/*/shapes/**/*", '"block/', '"game:block/')
    replace_in_folder("../../resources/assets/*/shapes/**/*", '"item/', '"game:item/')
    replace_in_folder("../../resources/assets/*/shapes/**/*", '"entity/', '"game:entity/')
