#!/usr/bin/env python3
"""Verify 16 KiB page compatibility of ARM64 JNI libraries inside an APK.

No third-party dependencies. Check both ELF PT_LOAD alignment and ZIP member
alignment, which are independent Android 16 KiB compatibility requirements.
"""
import argparse
import pathlib
import struct
import sys
import zipfile


def verify_elf(data: bytes, filename: str) -> None:
    if data[:6] != b"\x7fELF\x02\x01":
        raise ValueError(f"{filename}: expected little-endian ELF64")
    ph_offset = struct.unpack_from("<Q", data, 32)[0]
    ph_size = struct.unpack_from("<H", data, 54)[0]
    ph_count = struct.unpack_from("<H", data, 56)[0]
    if ph_size < 56 or ph_count < 1:
        raise ValueError(f"{filename}: malformed ELF program headers")
    load_count = 0
    for i in range(ph_count):
        at = ph_offset + i * ph_size
        if at + 56 > len(data):
            raise ValueError(f"{filename}: truncated ELF program headers")
        p_type = struct.unpack_from("<I", data, at)[0]
        if p_type == 1:  # PT_LOAD
            load_count += 1
            align = struct.unpack_from("<Q", data, at + 48)[0]
            if align < 16384 or (align % 16384):
                raise ValueError(f"{filename}: PT_LOAD[{i}] p_align={align:#x}, need 0x4000")
    if not load_count:
        raise ValueError(f"{filename}: no PT_LOAD segments")


def verify_apk(path: pathlib.Path) -> None:
    checked = set()
    with path.open("rb") as raw, zipfile.ZipFile(raw) as archive:
        for member in archive.infolist():
            name = member.filename
            if not (name.startswith("lib/arm64-v8a/") and name.endswith(".so")):
                continue
            checked.add(name)
            verify_elf(archive.read(member), name)
            if member.compress_type != zipfile.ZIP_STORED:
                # Compressed libraries can be extracted by Android. If the APK
                # uses compressed JNI libraries, ZIP page alignment is not relevant.
                continue
            raw.seek(member.header_offset + 26)
            filename_len, extra_len = struct.unpack("<HH", raw.read(4))
            file_start = member.header_offset + 30 + filename_len + extra_len
            if file_start % 16384:
                raise ValueError(
                    f"{name}: ZIP offset={file_start} is not 16 KiB aligned"
                )
    if not checked:
        raise ValueError(f"{path}: missing arm64-v8a native libraries")
    print(f"PASS: {len(checked)} ARM64 native libraries, 16 KiB ELF/ZIP alignment")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("apk", type=pathlib.Path)
    args = parser.parse_args()
    try:
        verify_apk(args.apk)
    except (OSError, ValueError, zipfile.BadZipFile, struct.error) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
