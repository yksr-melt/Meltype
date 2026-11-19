#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Build the shared Rust engine and a load-unpacked ChromeOS extension ZIP."""
import argparse
import base64
import array
import gzip
import hashlib
import json
import re
import shutil
import subprocess
import struct
import sys
from urllib.request import urlopen
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.2.129"
FILES = ("manifest.json", "background.mjs", "controller.mjs", "keys.mjs", "site-profiles.mjs", "options.html", "options.mjs", "options.css", "analytics.mjs", "shunti.mjs", "update.mjs", "ai-offscreen.html", "ai-offscreen.mjs", "ai-worker.mjs")


def build_kanji_dictionary():
    """固定版Mozcの語彙・接続コストを、WASMが直接参照できる形式にする。"""
    commit = (ROOT / "native/mozc/MOZC_COMMIT").read_text().strip()
    cache = ROOT / "tools/data/chromeos-mozc" / commit
    cache.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def source(name, path="src/data/dictionary_oss/"):
        target = cache / name
        if not target.exists():
            with urlopen(f"https://raw.githubusercontent.com/google/mozc/{commit}/{path}{name}", timeout=120) as response:
                data = response.read()
            temporary = target.with_suffix(".download")
            temporary.write_bytes(data)
            temporary.replace(target)
        data = target.read_bytes()
        hashes[name] = hashlib.sha256(data).hexdigest()
        return data.decode("utf-8")

    connection = iter(source("connection_single_column.txt").splitlines())
    dimension = int(next(connection))
    matrix = array.array("H", (int(value) for value in connection))
    if len(matrix) != dimension * dimension or dimension > 8192:
        raise ValueError("Mozc接続コストの形式が不正です")
    if sys.byteorder != "little":
        matrix.byteswap()
    entries = []
    for index in range(10):
        for line in source(f"dictionary{index:02}.txt").splitlines():
            fields = line.split("\t")
            if len(fields) < 5:
                raise ValueError("Mozc語彙の形式が不正です")
            reading, left, right, cost, word = fields[:5]
            left, right, cost = int(left), int(right), int(cost)
            if not reading or not word or not (0 <= left < dimension and 0 <= right < dimension):
                raise ValueError("Mozc語彙の値が不正です")
            entries.append((reading.encode("utf-8"), word.encode("utf-8"), left, right, cost))
    entries.sort()
    pool = bytearray()
    offsets = {}

    def intern(value):
        if value not in offsets:
            offsets[value] = len(pool)
            pool.extend(value)
        return offsets[value]

    records = bytearray()
    for reading, word, left, right, cost in entries:
        records.extend(struct.pack("<IIIIHHi", intern(reading), len(reading), intern(word), len(word), left, right, cost))
    pkg = ROOT / "chromeos/pkg"
    pkg.mkdir(exist_ok=True)
    (pkg / "kanji.bin").write_bytes(
        b"MTKKC001" + struct.pack("<III", dimension, len(entries), max(len(entry[0]) for entry in entries))
        + matrix.tobytes() + records + pool)
    license_text = source("LICENSE", "")
    (pkg / "MOZC-DICTIONARY-NOTICES.txt").write_text(
        f"Mozc dictionary data: https://github.com/google/mozc/tree/{commit}/src/data/dictionary_oss\n"
        "Meltypeで語彙と接続コストを別形式へ変換。Mozc本体の変換エンジンとは異なります。\n\n"
        + license_text + "\n\nSource SHA256:\n" + json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"kanji_entries": len(entries), "dictionary_bytes": (pkg / "kanji.bin").stat().st_size}))


def extract_dictionary():
    packed = ROOT / "native/mozc/portable"
    pkg = ROOT / "chromeos/pkg"
    pkg.mkdir(exist_ok=True)
    with gzip.open(packed / "kanji.bin.gz", "rb") as src, (pkg / "kanji.bin").open("wb") as dest:
        shutil.copyfileobj(src, dest)
    shutil.copyfile(packed / "NOTICES.txt", pkg / "MOZC-DICTIONARY-NOTICES.txt")


def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--refresh-dictionary", action="store_true", help="regenerate the bundled offline dictionary from the pinned Mozc sources")
    parser.add_argument("--wasm-bindgen", default="wasm-bindgen")
    parser.add_argument("--out", type=Path, default=ROOT / "dist/chromeos-preview")
    args = parser.parse_args()
    if args.refresh_dictionary:
        build_kanji_dictionary()
        packed = ROOT / "native/mozc/portable"
        with (ROOT / "chromeos/pkg/kanji.bin").open("rb") as src, (packed / "kanji.bin.gz").open("wb") as out:
            with gzip.GzipFile(filename="", fileobj=out, mode="wb", compresslevel=6, mtime=0) as gz:
                shutil.copyfileobj(src, gz)
        digest = hashlib.sha256((ROOT / "chromeos/pkg/kanji.bin").read_bytes()).digest()
        (packed / "integrity.txt").write_text("sha256-" + base64.b64encode(digest).decode("ascii") + "\n", encoding="ascii")
        shutil.copyfile(ROOT / "chromeos/pkg/MOZC-DICTIONARY-NOTICES.txt", packed / "NOTICES.txt")
        return
    tool = shutil.which(str(args.wasm_bindgen)) or str(Path(args.wasm_bindgen).resolve())
    version = subprocess.check_output([tool, "--version"], encoding="utf-8").strip()
    if version != f"wasm-bindgen {VERSION}":
        parser.error(f"wasm-bindgen {VERSION} is required; found {version}")
    out = args.out.resolve()
    if out == ROOT or ROOT.is_relative_to(out) or out.is_symlink():
        parser.error("output must be a dedicated generated directory")
    allowed = set(FILES) | {"ai", "pkg", "LICENSE", "THIRD-PARTY-NOTICES.md", "RUST-DEPENDENCY-NOTICES.txt", "README.md", "SHA256SUMS.json"}
    if out.exists() and any(p.name not in allowed for p in out.iterdir()):
        parser.error("output contains unrelated files; choose an empty directory")
    run("cargo", "build", "--release", "--locked", "--target", "wasm32-unknown-unknown", "-p", "meltype-browser")
    metadata = json.loads(subprocess.check_output(["cargo", "metadata", "--no-deps", "--format-version", "1"], cwd=ROOT, encoding="utf-8"))
    wasm = Path(metadata["target_directory"]) / "wasm32-unknown-unknown/release/meltype_browser.wasm"
    run(tool, wasm, "--target", "web", "--out-dir", ROOT / "chromeos/pkg")
    extract_dictionary()
    run(sys.executable, ROOT / "chromeos/build-shunti.py", ROOT / "chromeos/ai")
    run("node", "--test", *sorted((ROOT / "chromeos/tests").glob("*.test.mjs")))
    package_extension(out)


def package_extension(out):
    out.mkdir(parents=True, exist_ok=True)
    for name in FILES + ("README.md",):
        shutil.copyfile(ROOT / "chromeos" / name, out / name)
    for name in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
        shutil.copyfile(ROOT / name, out / name)
    shutil.copytree(ROOT / "chromeos/ai", out / "ai", dirs_exist_ok=True)
    (out / "pkg").mkdir(exist_ok=True)
    for name in ("meltype_browser.js", "meltype_browser_bg.wasm", "kanji.bin", "MOZC-DICTIONARY-NOTICES.txt"):
        shutil.copyfile(ROOT / "chromeos/pkg" / name, out / "pkg" / name)
    # Include the license texts from the actual resolved engine/build dependency
    # graph, rather than assuming desktop package notices cover new WASM crates.
    tree = subprocess.check_output(["cargo", "tree", "--locked", "-p", "meltype-browser", "--target", "wasm32-unknown-unknown", "--edges", "normal,build", "--prefix", "none", "--format", "{p}"], cwd=ROOT, encoding="utf-8")
    dependencies = {tuple(m.groups()) for line in tree.splitlines() if (m := re.match(r"^([\w-]+) v(\S+)", line))}
    graph = json.loads(subprocess.check_output(["cargo", "metadata", "--locked", "--format-version", "1", "--filter-platform", "wasm32-unknown-unknown"], cwd=ROOT, encoding="utf-8"))
    notices = ["Meltype ChromeOS Rust/WASM dependency license texts\n"]
    for package in sorted(graph["packages"], key=lambda p: (p["name"], p["version"])):
        if (package["name"], package["version"]) not in dependencies:
            continue
        notices.append(f'\n=== {package["name"]} {package["version"]} ({package.get("license")}) ===\n')
        folder = Path(package["manifest_path"]).parent
        license_files = [p for p in folder.iterdir() if p.is_file() and p.name.upper().startswith(("LICENSE", "LICENCE", "COPYING", "COPYRIGHT", "NOTICE"))]
        if package.get("license_file"):
            license_files.append(folder / package["license_file"])
        if not license_files and package["name"].startswith("meltype-"):
            license_files = [ROOT / "LICENSE"]
        if not license_files:
            raise RuntimeError(f'No license text found for {package["name"]}')
        for path in sorted(set(license_files)):
            notices.append(f'\n--- {path.name} ---\n{path.read_text(encoding="utf-8", errors="replace")}\n')
    (out / "RUST-DEPENDENCY-NOTICES.txt").write_text("".join(notices), encoding="utf-8")
    packaged = [*(out / p for p in FILES), out / "README.md", out / "LICENSE", out / "THIRD-PARTY-NOTICES.md",
                out / "pkg/meltype_browser.js", out / "pkg/meltype_browser_bg.wasm", out / "pkg/kanji.bin",
                out / "pkg/MOZC-DICTIONARY-NOTICES.txt", out / "RUST-DEPENDENCY-NOTICES.txt"]
    packaged.extend(sorted(p for p in (out / "ai").rglob("*") if p.is_file()))
    checksums = {p.relative_to(out).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in packaged}
    (out / "SHA256SUMS.json").write_text(json.dumps(checksums, indent=2) + "\n", encoding="utf-8")
    with zipfile.ZipFile(out.with_suffix(".zip"), "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in packaged + [out / "SHA256SUMS.json"]:
            archive.write(path, path.relative_to(out).as_posix())
    print(json.dumps({"directory": str(out), "zip": str(out.with_suffix('.zip')), "files": len(checksums)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
