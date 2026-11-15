#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Compare upstream C# NativeAOT and Rust release common-core key/JSON handling.

Does not install/register an IME or read user dictionaries. Conversion backends,
OS spelling, live conversion, prediction, learning and disk I/O are excluded.
Both implementations use the bundled spelling dictionary.
Session/dictionary construction, verification and process startup are not timed.
"""
import argparse
import datetime
import hashlib
import io
import json
import os
import platform
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import tarfile

sys.path.insert(0, str(Path(__file__).resolve().parent / "performance/windows"))
from resources import measure_process

BASELINE = "46db6ca2f941eb9580f4d5ed103ee0bee71111c7"
REPO = Path(__file__).resolve().parent.parent


def command(args, **kwargs):
    return subprocess.check_output(args, cwd=REPO, text=True, encoding="utf-8", **kwargs).strip()


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fixtures():
    def keys(text):
        return [[ord(c.upper()) if c.isascii() and c.isalpha() else 7, ord(c), int(c.isupper())]
                for c in text]

    cases = []
    texts = {"japanese": "kyouhaiitenkidesunewatashihagakuseidesuyoroshikuonegaishimasu",
             "mixed": "kyouhaiitenkidesunegoogledekensakushitegithubnipushshita",
             "english": "thankyouforyourhelp", "url": "https://example.com/" + "a" * 300}
    for name, text in texts.items():
        for length in (32, 64, 128, 256):
            raw = (text * ((length + len(text) - 1) // len(text)))[:length]
            cases.append(dict(name=f"{name}_{length}", keys=keys(raw) + [[13, 0, 0]]))
    for name, text in {"short_japanese": "ohayougozaimasu", "short_mixed": "googledekensaku",
                       "short_english": "hello", "short_url": "https://example.com/"}.items():
        cases.append(dict(name=name, keys=keys(text) + [[13, 0, 0]]))
    cases.append(dict(name="edit_and_katakana", keys=keys("konnichiwa") + [[8, 0, 0]] * 2
                      + keys("hasekai") + [[118, 0, 0], [13, 0, 0]]))
    return cases


def verify(responses, keys):
    """Apply guarded edits in UTF-16 as a Windows host would. Reject invalid deletes."""
    document = ""
    trace = []
    for response, (vk, ch, _modifiers) in zip(responses, keys, strict=True):
        result = json.loads(response)
        commits = []
        for edit in result["commits"]:
            # Rust の比較用 JSON は deleteBefore/expect、上流 C# 1.6.0 のフロント向け JSON は
            # removeBefore/moveCaretBack を使う。同じ意味なので、ここで 1 つの形にそろえる
            if "deleteBefore" in edit:
                count, expected = edit["deleteBefore"], edit.get("expect")
            else:
                expected = edit.get("removeBefore")
                count = len(expected.encode("utf-16-le")) // 2 if expected else 0
            if count:
                if expected is None or not document.endswith(expected) or len(expected.encode("utf-16-le")) // 2 != count:
                    raise ValueError(f"invalid guarded edit: {edit!r} in {document!r}")
                document = document.encode("utf-16-le")[:-2 * count].decode("utf-16-le")
            document += edit["text"]
            commits.append(dict(deleteBefore=count, text=edit["text"], expect=expected))
        if not result["consumed"] and ch >= 32 and vk not in (8, 13):
            document += chr(ch)
        view = result["view"]
        trace.append(dict(consumed=result["consumed"], commits=commits,
                          text=view["text"] if view else None,
                          converting=view["converting"] if view else False))
    if result["view"] is not None:
        raise ValueError("Enter left uncommitted text")
    return document, trace


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--baseline-ref", default=BASELINE)
    parser.add_argument("--samples", type=int, default=15)
    parser.add_argument("--warmups", type=int, default=3)
    parser.add_argument("--rounds", type=int, default=2, help="alternate implementation order each round")
    parser.add_argument("--rust-toolchain", default="stable")
    parser.add_argument("--jit", action="store_true", help="explicitly use C# JIT instead of NativeAOT")
    args = parser.parse_args()
    if args.samples < 3 or args.warmups < 1 or args.rounds < 2:
        parser.error("samples>=3, warmups>=1 and rounds>=2 required")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if (output / "report.json").exists():
        parser.error("choose a new output directory to preserve previous measurements")
    baseline = command(["git", "rev-parse", args.baseline_ref])
    source_paths = sorted({"tools/benchmark-windows.py", "tools/performance/windows/resources.py", "tools/performance/windows/Program.cs",
                           "tools/performance/windows/Compare.csproj", "Cargo.toml", "Cargo.lock",
                           "rust/core/Cargo.toml"} | {
        p.relative_to(REPO).as_posix()
        for root in (REPO / "rust/core/src", REPO / "rust/core/data", REPO / "rust/core/examples", REPO / "dictionaries")
        for p in root.rglob("*") if p.is_file()
    })
    source_hashes = {p: sha(REPO / p) for p in source_paths}
    working_tree = command(["git", "status", "--short"])
    rust_commit = command(["git", "rev-parse", "HEAD"])
    baseline_root = output / "baseline-source"
    baseline_root.mkdir(exist_ok=True)
    archive = subprocess.check_output(["git", "archive", baseline, "src/Meltype.Core", "dictionaries"], cwd=REPO)
    with tarfile.open(fileobj=io.BytesIO(archive)) as files:
        files.extractall(baseline_root, filter="data")
    hashes = {label: {p.name: sha(p) for p in sorted((root / "dictionaries").glob("*.txt"))}
              for label, root in [("csharp", baseline_root), ("rust", REPO)]}
    if hashes["csharp"] != hashes["rust"]:
        raise RuntimeError("dictionary versions differ; resolve before comparing")
    harness = baseline_root / "tools/performance/windows"
    harness.mkdir(parents=True, exist_ok=True)
    for name in ("Program.cs", "Compare.csproj"):
        shutil.copyfile(REPO / "tools/performance/windows" / name, harness / name)
    fixture_file = output / "cases.json"
    cases = fixtures()
    fixture_file.write_text(json.dumps(cases, ensure_ascii=False, indent=2), encoding="utf-8")
    env = os.environ.copy()
    env["DOTNET_TieredCompilation"] = "0"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["MELTYPE_BENCH_PROGRESS"] = "1"
    build_commands = [
        ["cargo", f"+{args.rust_toolchain}", "build", "-p", "meltype-core", "--example", "benchmark", "--release", "--locked"],
        ["dotnet", "publish", str(harness / "Compare.csproj"), "-c", "Release", "-o", str(output / "csharp"),
         f"-p:PublishAot={str(not args.jit).lower()}", "--source", "https://api.nuget.org/v3/index.json"]]
    for label, build in zip(("rust", "csharp"), build_commands):
        print(f"Building {label}", flush=True)
        with (output / f"{label}-build.log").open("w", encoding="utf-8") as log:
            subprocess.run(build, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    suffix = ".exe" if os.name == "nt" else ""
    target = Path(env.get("CARGO_TARGET_DIR", REPO / "target"))
    if {p: sha(REPO / p) for p in source_paths} != source_hashes:
        raise RuntimeError("sources changed during the build; rerun with fixed sources")
    # Keep the measured executable independent of subsequent cargo builds.
    rust_binary = output / "rust" / ("benchmark" + suffix)
    rust_binary.parent.mkdir(exist_ok=True)
    shutil.copyfile(target / "release/examples" / ("benchmark" + suffix), rust_binary)
    programs = {"rust": [str(rust_binary)],
                "csharp": (["dotnet", str(output / "csharp/Compare.dll")] if args.jit else [str(output / "csharp" / ("Compare" + suffix))])}
    binary_paths = {v: Path(program[-1]) for v, program in programs.items()}
    binary_hashes = {v: sha(p) for v, p in binary_paths.items()}
    rows = []
    evidence = {}
    order = []
    resources = []
    for round_index in range(args.rounds):
        for label in (("csharp", "rust") if round_index % 2 == 0 else ("rust", "csharp")):
            print(f"Measuring round {round_index + 1}/{args.rounds}: {label}", flush=True)
            order.append(label)
            with (output / f"{label}-{round_index}.jsonl").open("w", encoding="utf-8") as raw:
                usage = measure_process(programs[label] + [str(fixture_file), str(args.samples), str(args.warmups)],
                                        cwd=REPO, env=env, stdout=raw)
                resources.append(dict(version=label, round=round_index, **usage))
                (output / "resources.json").write_text(json.dumps(resources, indent=2) + "\n", encoding="utf-8")
            for line in (output / f"{label}-{round_index}.jsonl").read_text(encoding="utf-8-sig").splitlines():
                row = json.loads(line)
                case = next(c for c in cases if c["name"] == row["case"])
                document, trace = verify(row.pop("responses"), case["keys"])
                key = (label, row["case"])
                value = dict(output=document, trace=trace)
                if key in evidence and evidence[key] != value:
                    raise RuntimeError(f"non-deterministic result: {key}")
                evidence[key] = value
                row.update(version=label, round=round_index)
                rows.append(row)
    sources_unchanged = all(sha(REPO / p) == source_hashes[p] for p in source_paths)
    binaries_unchanged = all(sha(p) == binary_hashes[v] for v, p in binary_paths.items())
    comparisons = []
    for case in cases:
        name = case["name"]
        groups = {v: [r for r in rows if r["case"] == name and r["version"] == v] for v in programs}
        medians = {v: statistics.median(r["total_ms"] for r in rs) for v, rs in groups.items()}
        latencies = {v: sorted(t for r in rs for t in r["keys_ms"]) for v, rs in groups.items()}
        matching_output = evidence[("csharp", name)]["output"] == evidence[("rust", name)]["output"]
        matching_trace = evidence[("csharp", name)]["trace"] == evidence[("rust", name)]["trace"]
        comparisons.append(dict(case=name, keys=len(case["keys"]), median_total_ms=medians,
                                p95_key_ms={v: ts[int((len(ts)-1)*.95)] for v, ts in latencies.items()},
                                rust_over_csharp=medians["rust"] / medians["csharp"],
                                output_matches=matching_output, trace_matches=matching_trace,
                                eligible_for_speed_claim=matching_trace and sources_unchanged and binaries_unchanged))
        print(f"{name}: C# {medians['csharp']:.3f} ms; Rust {medians['rust']:.3f} ms; "
              f"ratio {medians['rust']/medians['csharp']:.3f}; output={matching_output}, trace={matching_trace}")
    report = dict(measured_at=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  machine=platform.platform(), cpu=platform.processor(), baseline=baseline,
                  rust_commit=rust_commit, working_tree=working_tree,
                  binary_sha256=binary_hashes,
                  binaries_unchanged=binaries_unchanged, sources_unchanged=sources_unchanged,
                  accepted_performance_baseline=sources_unchanged and binaries_unchanged,
                  rustc=command(["rustc", f"+{args.rust_toolchain}", "-Vv"]), dotnet=command(["dotnet", "--version"]),
                  csharp_runtime="JIT (tiering disabled)" if args.jit else "NativeAOT", build_commands=build_commands,
                  samples_per_round=args.samples, warmups_per_case=args.warmups, order=order,
                  settings=dict(LiveConversion=False, PredictiveCandidates=False, LearningEnabled=False,
                                CanDeleteSurrounding=True, BuiltInSpelling=True, OsSpelling=False),
                  scope=__doc__, dictionaries=hashes, fixtures_sha256=sha(fixture_file),
                  source_sha256=source_hashes,
                  process_resources=resources, comparisons=comparisons, rows=rows,
                  evidence={v: {c["name"]: evidence[v, c["name"]] for c in cases} for v in programs})
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {output / 'report.json'}")


if __name__ == "__main__":
    main()
