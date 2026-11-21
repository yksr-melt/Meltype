#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Build and compare C#, previous Rust, and current Rust on one Windows, macOS or Linux host."""
import argparse
import datetime
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import platform
import re
import shutil
import statistics
import subprocess
import sys
import tarfile
import tomllib

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("windows_benchmark", ROOT / "tools/benchmark-windows.py")
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)
if os.name != "nt":
    sys.path.insert(0, str(ROOT / "tools/performance"))
    from process_resources import measure_process
    benchmark.measure_process = measure_process
SUFFIX = ".exe" if os.name == "nt" else ""


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def version_key(value):
    match = re.fullmatch(r"(?:flash-v|rust-v|v)?(\d+)\.(\d+)\.(\d+)(?:-(?:preview\.|pre\.)(\d+))?", value)
    if not match:
        return None
    major, minor, patch, preview = match.groups()
    return (int(major), int(minor), int(patch), int(preview is None), int(preview or 0))


def previous_release(current_ref=None):
    text = (benchmark.command(["git", "show", f"{current_ref}:Cargo.toml"]) if current_ref
            else (ROOT / "Cargo.toml").read_text(encoding="utf-8"))
    version = tomllib.loads(text)["workspace"]["package"]["version"]
    current = version_key(version)
    if current is None:
        raise ValueError(f"Unsupported workspace version: {version}; set --baseline-ref explicitly")
    tags = benchmark.command(["git", "tag", "--list"]).splitlines()
    eligible = [(version_key(tag), tag) for tag in tags if tag.startswith(("flash-v", "rust-v"))]
    eligible = [(key, tag) for key, tag in eligible if key is not None and key < current]
    if not eligible:
        raise ValueError(f"No release older than {version}; fetch release tags or set --baseline-ref")
    return max(eligible)[1]


def csharp_release():
    tags = benchmark.command(["git", "tag", "--list"]).splitlines()
    stable = [tag for tag in tags if re.fullmatch(r"v\d+\.\d+\.\d+", tag)]
    if not stable:
        raise ValueError("No C# stable release tag; fetch tags or set --csharp-ref")
    return max(stable, key=version_key)


def measurement_order(versions, round_index):
    offset = round_index % len(versions)
    return versions[offset:] + versions[:offset]


def source_hashes(root):
    paths = [root / name for name in ("Cargo.toml", "Cargo.lock")]
    paths += [p for directory in ("rust/core", "rust/native", "android", "src/Meltype.Core", "dictionaries")
              for p in (root / directory).rglob("*") if p.is_file()
              and not {"bin", "obj", "target", "build", ".gradle", "__pycache__"}.intersection(p.relative_to(root).parts)]
    paths += [root / "tools/performance/windows" / name for name in ("Program.cs", "Compare.csproj")]
    if root == ROOT:
        paths += [ROOT / "tools" / name for name in ("compare-rust-benchmarks.py", "benchmark-windows.py",
                                                    "plot-windows-benchmark.py", "performance/windows/resources.py", "performance/process_resources.py", "performance/native-benchmark.rs")]
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(paths) if p.is_file()}


def extract_sources(commit, destination, paths):
    destination.mkdir()
    archive = subprocess.check_output(["git", "archive", commit, *paths], cwd=ROOT)
    with tarfile.open(fileobj=io.BytesIO(archive)) as files:
        files.extractall(destination, filter="data")


def build_csharp(args, output):
    reference = csharp_release() if args.csharp_ref == "auto" else args.csharp_ref
    commit = benchmark.command(["git", "rev-parse", "--verify", reference + "^{commit}"])
    root = output / "csharp-source"
    extract_sources(commit, root, ["src/Meltype.Core", "dictionaries"])
    harness = root / "tools/performance/windows"
    harness.mkdir(parents=True)
    for name in ("Program.cs", "Compare.csproj"):
        shutil.copyfile(ROOT / "tools/performance/windows" / name, harness / name)
    snapshot = source_hashes(root)
    command = ["dotnet", "publish", str(harness / "Compare.csproj"), "-c", "Release",
               "-o", str(output / "csharp"), f"-p:PublishAot={str(not args.jit).lower()}",
               "--source", "https://api.nuget.org/v3/index.json"]
    print(f"Building C#: {reference} ({'JIT' if args.jit else 'NativeAOT'})", flush=True)
    env = os.environ.copy()
    env.update(DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    with (output / "csharp-build.log").open("w", encoding="utf-8") as log:
        try:
            subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
        except subprocess.CalledProcessError:
            log.flush()
            details = (output / "csharp-build.log").read_text(encoding="utf-8", errors="replace")
            print("C# benchmark build failed; final build log lines follow:", file=sys.stderr)
            print("\n".join(details.splitlines()[-80:]), file=sys.stderr)
            raise
    binary = output / "csharp" / ("Compare.dll" if args.jit else "Compare" + SUFFIX)
    return binary, root, snapshot, dict(csharp_ref=reference, csharp_commit=commit,
        csharp_runtime="JIT (tiering disabled)" if args.jit else "NativeAOT",
        dotnet=benchmark.command(["dotnet", "--version"])), command


def build_hosts(args, output):
    baseline = previous_release(args.current_ref) if args.baseline_ref == "auto" else args.baseline_ref
    old_commit = benchmark.command(["git", "rev-parse", "--verify", baseline + "^{commit}"])
    new_commit = benchmark.command(["git", "rev-parse", "--verify", (args.current_ref or "HEAD") + "^{commit}"])
    baseline_root = output / "baseline-source"
    extract_sources(old_commit, baseline_root, ["Cargo.toml", "Cargo.lock", "rust", "dictionaries", "android"])
    current_root = ROOT
    if args.current_ref:
        current_root = output / "current-source"
        extract_sources(new_commit, current_root, ["Cargo.toml", "Cargo.lock", "rust", "dictionaries", "android"])
    if args.backend == "native":
        # Never inject benchmark files into the working source tree.
        if current_root == ROOT:
            current_root = output / "current-source"
            current_root.mkdir()
            for name in ("Cargo.toml", "Cargo.lock"):
                shutil.copyfile(ROOT / name, current_root / name)
            for name in ("rust", "dictionaries", "android"):
                shutil.copytree(ROOT / name, current_root / name,
                    ignore=shutil.ignore_patterns("target", "build", ".gradle", "__pycache__"))
        for root in (baseline_root, current_root):
            examples = root / "rust/native/examples"
            examples.mkdir(exist_ok=True)
            shutil.copyfile(ROOT / "tools/performance/native-benchmark.rs", examples / "platform_benchmark.rs")
    roots = {"old": baseline_root, "new": current_root}
    snapshots = {label: source_hashes(root) for label, root in roots.items()}
    target = Path(os.environ.get("CARGO_TARGET_DIR", ROOT / "target")).resolve()
    binaries = {}
    build_commands = {}
    # Freeze current first: the old build may reuse/replace the same example output.
    for label in ("new", "old"):
        command = ["cargo", f"+{args.rust_toolchain}", "build", "--manifest-path", str(roots[label] / "Cargo.toml"),
                   "-p", "meltype-native" if args.backend == "native" else "meltype-core", "--example", "platform_benchmark" if args.backend == "native" else "benchmark", "--release", "--locked", "--target-dir", str(target)]
        build_commands[label] = command
        print(f"Building {label}: {baseline if label == 'old' else new_commit[:12]}", flush=True)
        with (output / f"{label}-build.log").open("w", encoding="utf-8") as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        binary = output / f"{label}{SUFFIX}"
        shutil.copy2(target / "release/examples" / (("platform_benchmark" if args.backend == "native" else "benchmark") + SUFFIX), binary)
        binaries[label] = binary
    dictionaries = {label: {p.name: sha(p) for p in sorted((root / "dictionaries").glob("*.txt"))} for label, root in roots.items()}
    version = tomllib.loads((current_root / "Cargo.toml").read_text(encoding="utf-8"))["workspace"]["package"]["version"]
    metadata = dict(old_ref=baseline, new_ref=args.current_ref or new_commit, old_commit=old_commit, new_commit=new_commit,
                    current_version=version,
                    old_version=tomllib.loads((baseline_root / "Cargo.toml").read_text(encoding="utf-8"))["workspace"]["package"]["version"],
                    working_tree=benchmark.command(["git", "status", "--short"]), build_commands=build_commands,
                    source_sha256=snapshots, dictionaries=dictionaries,
                    dictionaries_match=dictionaries["old"] == dictionaries["new"],
                    dictionary_policy="Each version retains its own bundled dictionaries; differences are part of the version comparison.")
    if args.with_csharp:
        binary, root, snapshot, details, command = build_csharp(args, output)
        binaries["csharp"] = binary
        roots["csharp"], snapshots["csharp"] = root, snapshot
        metadata.update(details)
        build_commands["csharp"] = command
        dictionaries["csharp"] = {p.name: sha(p) for p in sorted((root / "dictionaries").glob("*.txt"))}
    metadata["dictionaries_match"] = len({json.dumps(value, sort_keys=True) for value in dictionaries.values()}) == 1
    metadata["current_source"] = "tag/commit" if args.current_ref else "working-tree"
    metadata["version_labels"] = {"old": f"Rust {baseline}", "new": f"Rust {args.current_ref or version}"}
    if args.with_csharp:
        metadata["version_labels"]["csharp"] = f"C# {metadata['csharp_ref']}"
    return binaries, metadata, roots, snapshots


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fetch-tags", action="store_true", help="Fetch published version tags from origin before selection")
    parser.add_argument("--with-csharp", action="store_true", help="Include the latest stable C# release")
    parser.add_argument("--csharp-ref", default="auto", help="C# tag/commit, default latest stable v* tag")
    parser.add_argument("--csharp", type=Path, help="Prebuilt native C# benchmark executable")
    parser.add_argument("--jit", action="store_true", help="Explicit C# JIT instead of NativeAOT (build mode only)")
    parser.add_argument("--current-ref", help="Build this Rust tag/commit instead of the working tree")
    parser.add_argument("--plot", action="store_true", help="Generate charts and tables after measuring")
    parser.add_argument("--github-summary", type=Path)
    parser.add_argument("--old", type=Path)
    parser.add_argument("--new", type=Path)
    parser.add_argument("--old-ref")
    parser.add_argument("--new-ref")
    parser.add_argument("--backend", choices=("core", "native"), default="core", help="Core or native C ABI (not OS IME/UI)")
    parser.add_argument("--build", action="store_true", help="Build current and previous release using one toolchain and shared dependencies")
    parser.add_argument("--baseline-ref", default="auto", help="Previous release tag/commit, or auto from the workspace version")
    parser.add_argument("--rust-toolchain", default="stable")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--samples", type=int, default=5)
    parser.add_argument("--warmups", type=int, default=2)
    parser.add_argument("--rounds", type=int, help="Default: 3 with C#, 2 for Rust only")
    args = parser.parse_args()
    args.with_csharp = args.with_csharp or args.csharp is not None
    if args.rounds is None:
        args.rounds = 3 if args.with_csharp else 2
    if args.plot and any(importlib.util.find_spec(name) is None for name in ("matplotlib", "numpy")):
        parser.error("--plot requires matplotlib and numpy in this Python environment")
    if not args.build and (args.current_ref or args.jit or args.with_csharp and not args.csharp):
        parser.error("--current-ref/--jit and automatic C# builds require --build (or use --csharp EXE)")
    if args.build and args.csharp:
        parser.error("--csharp is for prebuilt mode; use --with-csharp with --build")
    if args.github_summary and not args.plot:
        parser.error("--github-summary requires --plot")
    if args.samples < 3 or args.warmups < 1 or args.rounds < 2:
        parser.error("samples>=3, warmups>=1 and rounds>=2 required")
    if args.backend == "native" and (args.with_csharp or not args.build):
        parser.error("native backend requires --build and Rust-only comparison")
    if args.build and any((args.old, args.new, args.old_ref, args.new_ref)):
        parser.error("--build cannot be combined with prebuilt binary arguments")
    if not args.build and not all((args.old, args.new, args.old_ref, args.new_ref)):
        parser.error("specify --build, or --old/--new/--old-ref/--new-ref")
    if args.fetch_tags:
        subprocess.run(["git", "fetch", "--no-tags", "origin",
                        "refs/tags/flash-*:refs/tags/flash-*", "refs/tags/rust-*:refs/tags/rust-*",
                        "refs/tags/v*:refs/tags/v*"], cwd=ROOT, check=True)
    output = args.output.resolve()
    # A fresh directory preserves evidence and prevents reusing partial builds.
    output.mkdir(parents=True, exist_ok=False)
    if args.build:
        binaries, metadata, roots, snapshots = build_hosts(args, output)
    else:
        binaries = {}
        for label, path in (("old", args.old), ("new", args.new)):
            binaries[label] = output / f"{label}{SUFFIX}"
            shutil.copy2(path.resolve(), binaries[label])
        if args.csharp:
            binaries["csharp"] = output / ("csharp" + SUFFIX)
            shutil.copy2(args.csharp.resolve(), binaries["csharp"])
        metadata = dict(old_ref=args.old_ref, new_ref=args.new_ref)
        if args.with_csharp:
            metadata.update(csharp_ref=args.csharp_ref, csharp_runtime="NativeAOT (prebuilt)")
        roots = snapshots = {}
    hashes = {label: sha(path) for label, path in binaries.items()}
    cases = benchmark.fixtures()
    inputs = output / "cases.json"
    inputs.write_text(json.dumps(cases, ensure_ascii=False, indent=2), encoding="utf-8")
    rows, resources, traces, order = [], [], {}, []
    env = os.environ.copy()
    env.update(MELTYPE_BENCH_PROGRESS="1", DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    versions = ("csharp", "old", "new") if args.with_csharp else ("old", "new")
    for round_index in range(args.rounds):
        for label in measurement_order(versions, round_index):
            order.append(label)
            print(f"Round {round_index + 1}/{args.rounds}: {label}", flush=True)
            raw = output / f"{label}-{round_index}.jsonl"
            with raw.open("w", encoding="utf-8") as log:
                program = (["dotnet", str(binaries[label])] if label == "csharp" and args.jit else [str(binaries[label])])
                usage = benchmark.measure_process(program + [str(inputs), str(args.samples), str(args.warmups)], cwd=ROOT, env=env, stdout=log)
            resources.append(dict(version=label, round=round_index, **usage))
            (output / "resources.json").write_text(json.dumps(resources, indent=2) + "\n", encoding="utf-8")
            for line in raw.read_text(encoding="utf-8-sig").splitlines():
                row = json.loads(line)
                case = next(case for case in cases if case["name"] == row["case"])
                trace = benchmark.verify(row["responses"], case["keys"])
                key = label, row["case"]
                if key in traces and traces[key] != trace:
                    raise ValueError(f"unstable output: {key}")
                traces[key] = trace
                rows.append(dict(version=label, round=round_index, case=row["case"], sample=row["sample"], total_ms=row["total_ms"]))
            expected = {(case["name"], sample) for case in cases for sample in range(args.samples)}
            observed = [(row["case"], row["sample"]) for row in rows if row["version"] == label and row["round"] == round_index]
            if len(observed) != len(expected) or set(observed) != expected:
                raise ValueError(f"Missing/duplicate observations: {label}, round {round_index}")
    unchanged = hashes == {label: sha(path) for label, path in binaries.items()}
    sources_unchanged = all(source_hashes(root) == snapshots[label] for label, root in roots.items()) if roots else None
    accepted = unchanged and sources_unchanged is not False
    results = []
    for case in cases:
        name = case["name"]
        medians = {label: statistics.median(row["total_ms"] for row in rows if row["version"] == label and row["case"] == name) for label in binaries}
        same = traces["old", name] == traces["new", name]
        csharp = {}
        if args.with_csharp:
            csharp_same = traces["csharp", name] == traces["new", name]
            csharp = dict(new_over_csharp=medians["new"] / medians["csharp"],
                          csharp_matching_projection=csharp_same,
                          csharp_output_matches=traces["csharp", name][0] == traces["new", name][0],
                          eligible_for_csharp_speed_claim=csharp_same and accepted)
        results.append(dict(case=name, keys=len(case["keys"]), medians_ms=medians,
                            new_over_old=medians["new"] / medians["old"], matching_projection=same,
                            output_matches=traces["old", name][0] == traces["new", name][0],
                            eligible_for_speed_claim=same and accepted, **csharp))
    report = dict(measured_at=datetime.datetime.now(datetime.timezone.utc).isoformat(), **metadata,
                  comparison_kind="rust-versions", backend=args.backend, host_os=platform.system(), versions=list(versions), machine=platform.platform(), cpu=platform.processor(),
                  samples=args.samples, warmups=args.warmups, rounds=args.rounds, order=order,
                  binary_sha256=hashes, binaries_unchanged=unchanged, sources_unchanged=sources_unchanged,
                  accepted_performance_baseline=accepted, fixtures_sha256=sha(inputs),
                  rustc=benchmark.command(["rustc", f"+{args.rust_toolchain}", "-Vv"]),
                  results=results, rows=rows, process_resources=resources,
                  scope=("Native C ABI keys and JSON, isolated temporary profile per session; " if args.backend == "native" else "In-memory keys and JSON; ") + " no OS engine/live conversion/prediction/learning. Same compiler for both builds. Source/dependency/dictionary revisions differ; this does not isolate one code change.")
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2), flush=True)
    if args.plot:
        command = [sys.executable, str(ROOT / "tools/plot-windows-benchmark.py"),
                   str(output / "report.json"), "--output", str(output / "charts")]
        if args.github_summary:
            command += ["--github-summary", str(args.github_summary.resolve())]
        subprocess.run(command, cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
