# SPDX-License-Identifier: GPL-3.0-or-later
"""Render readable common-core and resource charts, retaining every observation.

Requires matplotlib and numpy. Accepts the full benchmark report or its compact
summary.json output, which retains every total-time and resource observation.
"""
import argparse
import csv
import hashlib
import json
import re
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.patches import Patch
import numpy as np

VERSIONS = ("csharp", "rust")
LABELS = ("C# NativeAOT", "Rust Release")
COLORS = ("#da8745", "#387cac")
IMAGES = ["speed-32-64.png", "speed-128-256.png", "speed-short-edit.png", "cpu.png", "ram.png"]
RUST_IMAGES = ["rust-" + name for name in IMAGES]


def rust_label(value):
    match = re.fullmatch(r"(?:Rust )?(?:flash-v|rust-v|v)?(\d+\.\d+\.\d+)(?:-(?:preview\.|pre\.)(\d+))?", value)
    if match:
        version, preview = match.groups()
        return f"Rust v{version}" + (f" pre{preview}" if preview else "")
    return "Rust " + value.removeprefix("Rust ")[:12]



def distribution(values):
    values = np.asarray(values, dtype=float)
    if not len(values) or not np.isfinite(values).all():
        raise ValueError("A nonempty, finite sample is required")
    q1, med, q3 = np.percentile(values, [25, 50, 75], method="linear")
    iqr = q3 - q1
    inliers = values[(values >= q1 - 1.5 * iqr) & (values <= q3 + 1.5 * iqr)]
    fliers = values[(values < q1 - 1.5 * iqr) | (values > q3 + 1.5 * iqr)]
    return dict(n=len(values), q1=float(q1), med=float(med), q3=float(q3),
                whislo=float(inliers.min()), whishi=float(inliers.max()),
                fliers=fliers.tolist(), minimum=float(values.min()), maximum=float(values.max()))


def axis_window(stats, log=None):
    """Fit both versions together, keeping remote outliers in the report."""
    central = []
    for stat in stats:
        # An outer (3 IQR) fence keeps ordinary outliers visible. A small
        # relative floor also handles identical samples / timer resolution.
        spread = max(stat["q3"] - stat["q1"], abs(stat["med"]) * .05, 1e-9)
        central.extend([stat["whislo"], stat["whishi"]])
        central.extend(value for value in stat["fliers"]
                       if stat["q1"] - 3 * spread <= value <= stat["q3"] + 3 * spread)
    low, high = min(central), max(central)
    positive = all(stat["minimum"] > 0 for stat in stats)
    if log is True and not positive:
        raise ValueError("Logarithmic time axes require positive times")
    use_log = positive and (log is True or log is None and high / low > 10)
    if use_log:
        lo, hi = np.log10([low, high])
        span = max(hi - lo, .08)
        limits = (10 ** (lo - span * .12), 10 ** (hi + span * .4))
    else:
        # Very close measurements still need distinct positions on the plot.
        span = max(high - low, abs(high) * 1e-12) if high > low else max(abs(high) * .1, 1e-9)
        limits = (low - span * .12, high + span * .4)
        if all(stat["minimum"] >= 0 for stat in stats):
            limits = (max(0, limits[0]), limits[1])
    return ("log" if use_log else "linear"), limits


def boxes(ax, groups, ylabel, title, *, log=None):
    stats = [distribution(groups[v]) for v in VERSIONS]
    scale, limits = axis_window(stats, log)
    medians = {stat["med"] for stat in stats}
    digits = next(n for n in range(4, 18) if len({format(m, f".{n}g") for m in medians}) == len(medians))
    visible_stats = []
    for version, stat in zip(VERSIONS, stats):
        stat["axis_scale"] = scale
        stat["axis_limits"] = list(limits)
        stat["off_scale"] = [float(value) for value in groups[version]
                             if not limits[0] <= value <= limits[1]]
        visible_stats.append(dict(stat, fliers=[value for value in stat["fliers"]
                                               if limits[0] <= value <= limits[1]]))
    drawing = ax.bxp(visible_stats, positions=list(range(1, len(VERSIONS) + 1)), widths=.48, patch_artist=True, showfliers=True,
                     manage_ticks=False, medianprops=dict(color="#111111", linewidth=2.2),
                     flierprops=dict(marker="D", markersize=5, markerfacecolor="none",
                                     markeredgecolor="#a52f35", alpha=.9))
    for box, color in zip(drawing["boxes"], COLORS):
        box.set_facecolor(color)
        box.set_alpha(.48)
    for position, version in enumerate(VERSIONS, 1):
        values = np.asarray(groups[version], dtype=float)
        offsets = np.linspace(-.1, .1, len(values))
        visible = (values >= limits[0]) & (values <= limits[1])
        ax.scatter(position + offsets[visible], values[visible], s=18,
                   color=COLORS[position - 1], alpha=.42, zorder=2)
        stat = stats[position - 1]
        annotation = f"median {stat['med']:.{digits}g}\nn={stat['n']}, outliers={len(stat['fliers'])}"
        if stat["off_scale"]:
            annotation += f"\n{len(stat['off_scale'])} off-scale\n(values in report)"
        ax.text(position, .98, annotation,
                transform=ax.get_xaxis_transform(), ha="center", va="top", fontsize=9 if len(VERSIONS) == 3 else 10,
                bbox=dict(facecolor="white", edgecolor="none", alpha=.9), zorder=5)
    ax.set_yscale(scale)
    ax.set_ylim(*limits)
    if scale == "linear":
        ax.ticklabel_format(axis="y", style="plain", useOffset=(limits[1] - limits[0] < abs(limits[0]) * 1e-4))
    ax.set_xticks(range(1, len(VERSIONS) + 1), LABELS)
    ax.set_xlim(.45, len(VERSIONS) + .55)
    ax.set_title(title, fontsize=13, pad=12)
    ax.set_ylabel(ylabel + (" (log scale)" if scale == "log" else ""))
    ax.grid(axis="y", alpha=.22, which="both")
    ax.spines[["top", "right"]].set_visible(False)
    return {v: stat for v, stat in zip(VERSIONS, stats)}


def off_scale_table(stats):
    from collections import Counter
    rows = ["## 表示範囲外の測定値", "",
            "各パネルは全比較対象に同じ表示範囲を使います。大きく離れた外れ値は縦軸を広げず、以下に全件を記載します。中央値・四分位数・ひげは全観測値から計算し、元データは削除しません。値の後の×は同じ値の観測件数です。", ""]
    entries = []
    for section, units in [("speed", None), ("resources", stats["resource_units"])]:
        for name, versions in stats[section].items():
            unit = "ms" if units is None else units[name]
            for version, label in zip(VERSIONS, LABELS):
                stat = versions[version]
                if stat["off_scale"]:
                    values = "; ".join(f"{value:.9g} × {count}" for value, count
                                       in sorted(Counter(stat["off_scale"]).items()))
                    low, high = stat["axis_limits"]
                    entries.append(f"|{name}|{label}|{unit}|{low:.6g}–{high:.6g}|{len(stat['off_scale'])}|{values}|")
    if entries:
        rows += ["|項目|版|単位|図の表示範囲|範囲外の件数|範囲外の値 × 件数|",
                 "|---|---|---|---:|---:|---|", *entries, ""]
    else:
        rows += ["表示範囲外の測定値はありません。", ""]
    return rows


def zoom_medians(ax, groups):
    """Reveal close medians in the scalable view; keep the full distribution in the PNG."""
    if not ax.axison:
        return
    values = np.array([groups[v]["med"] for v in VERSIONS])
    transform = ax.yaxis.get_transform()
    span = np.ptp(transform.transform(values))
    visible = np.ptp(transform.transform(ax.get_ylim()))
    if span == 0 or span >= visible * .1:
        return
    title, unit = ax.get_title(), ax.get_ylabel().removesuffix(" (log scale)")
    ax.clear()
    ax.set_title(title)
    ax.set_ylabel(f"{unit} (median detail)")
    ax.set_xticks(range(1, len(values) + 1), LABELS)
    ax.set_xlim(.45, len(values) + .55)
    padding = max(np.ptp(values) * .3, abs(values[0]) * 1e-12)
    ax.set_ylim(values.min() - padding, values.max() + padding)
    for x, (value, color) in enumerate(zip(values, COLORS), 1):
        ax.plot(x, value, "o", color=color)
        ax.annotate(np.format_float_positional(value, trim="-"), (x, value),
                    xytext=(0, 6), textcoords="offset points", ha="center")
    ax.grid(axis="y", alpha=.22)
    ax.spines[["top", "right"]].set_visible(False)


def finish(fig, path, title, note, panel_stats):
    fig.suptitle(title, fontsize=15, fontweight="bold", y=1 - .12 / fig.get_figheight())
    fig.legend(handles=[Patch(facecolor=c, alpha=.55, label=l) for c, l in zip(COLORS, LABELS)] + [
        Line2D([0], [0], color="#111111", linewidth=2.2, label="Median"),
        Line2D([0], [0], linestyle="none", marker="D", markerfacecolor="none",
               markeredgecolor="#a52f35", label="Outlier (outside 1.5 x IQR)")],
        loc="upper center", bbox_to_anchor=(.5, 1 - .48 / fig.get_figheight()), ncol=3 if len(VERSIONS) == 3 else 4, frameon=False)
    height = fig.get_figheight()
    fig.text(.5, .1 / height, note, ha="center", va="bottom", fontsize=10)
    fig.subplots_adjust(left=.09, right=.98, bottom=.85 / height,
                        top=1 - (1.65 if len(VERSIONS) == 3 else 1.35) / height, hspace=.65, wspace=.3)
    fig.savefig(path, dpi=170, facecolor="white", metadata={"Software": "Meltype benchmark plotter"})
    for ax, groups in zip(fig.axes, panel_stats):
        zoom_medians(ax, groups)
    fig.savefig(path.with_suffix(".svg"), facecolor="white", metadata={"Date": None})
    plt.close(fig)


def render_charts(report, output, names, comparison_label, rounds, process_count):
    width = 14.8 if len(VERSIONS) == 3 else 12.8
    with_csharp = len(VERSIONS) == 3
    stats = {"speed": {}, "resources": {}, "resource_units": {}}
    stamp = f"{report['measured_at'][:10]} | {comparison_label} | n={report['samples_per_round'] * (rounds)} sequences / version / case"
    partitions = [
        (names[0], [r for r in report["comparisons"] if r["case"].endswith(("_32", "_64"))], "32 / 64 character inputs"),
        (names[1], [r for r in report["comparisons"] if r["case"].endswith(("_128", "_256"))], "128 / 256 character inputs"),
        (names[2], [r for r in report["comparisons"] if r["case"].startswith("short_") or r["case"] == "edit_and_katakana"], "Short inputs / editing"),
    ]
    selected = [row["case"] for _, cases, _ in partitions for row in cases]
    if len(selected) != len(set(selected)) or set(selected) != {r["case"] for r in report["comparisons"]}:
        raise ValueError("Chart partitions do not cover every case exactly once")
    for filename, cases, title in partitions:
        fig, axes = plt.subplots((len(cases) + 1) // 2, 2, figsize=(width, 1.9 + 4.4 * ((len(cases) + 1) // 2)), squeeze=False)
        for ax, case in zip(axes.flat, cases):
            groups = {v: [r["total_ms"] for r in report["rows"] if r["version"] == v and r["case"] == case["case"]]
                      for v in VERSIONS}
            suffix = "" if case["eligible_for_speed_claim"] else " [RUST TRACE MISMATCH]"
            if with_csharp and not case["eligible_for_csharp_speed_claim"]:
                suffix += " [C# TRACE MISMATCH]"
            stats["speed"][case["case"]] = boxes(ax, groups, "Sequence time (ms)",
                f"{case['case']}{suffix}\n{case['keys']} keys incl. Enter")
        for ax in list(axes.flat)[len(cases):]:
            ax.set_visible(False)
        finish(fig, output / filename, f"{report.get('host_os', 'Windows')} / {report.get('backend', 'core')}: {title}",
               "Lower is faster. Each dot = one sequence; whiskers = 1.5 x IQR. Off-scale values are listed in the report.\n"
               "No TSF, candidate rendering, external converter, prediction or learning.\n" + stamp,
               [stats["speed"][case["case"]] for case in cases])
    mib = 1024 ** 2
    panels = [
        (names[3], "CPU / elapsed time across complete benchmark processes", [
            ("cpu_seconds", "CPU time", "seconds", lambda r: r["cpu_seconds"]),
            ("wall_seconds", "Elapsed time", "seconds", lambda r: r["wall_seconds"]),
            ("cpu_percent_one_core", "Mean CPU use\n(one core = 100%)", "%", lambda r: r["cpu_percent_one_core"]),
            ("cpu_percent_machine", "Mean CPU use\n(whole machine = 100%)", "%", lambda r: r["cpu_percent_machine"]),
        ]),
        (names[4], "Memory across complete benchmark processes", [
            ("peak_working_set_bytes", "Peak working set\n(resident; shared pages included)", "MiB", lambda r: r["peak_working_set_bytes"] / mib),
            ("sampled_mean_working_set_bytes", "Sampled mean working set", "MiB", lambda r: r["sampled_mean_working_set_bytes"] / mib),
            ("sampled_peak_private_bytes", "Sampled peak private bytes\n(committed)", "MiB", lambda r: r["sampled_peak_private_bytes"] / mib),
            ("peak_working_set_percent_physical", "Peak working set /\ntotal physical RAM", "%", lambda r: r["peak_working_set_percent_physical"]),
        ]),
    ]
    for filename, title, metrics in panels:
        fig, axes = plt.subplots(2, 2, figsize=(width, 10.7))
        for ax, (key, caption, unit, value) in zip(axes.flat, metrics):
            groups = {v: [value(r) for r in report["process_resources"] if r["version"] == v and r.get(key) is not None] for v in VERSIONS}
            if any(r.get(key) is None for r in report["process_resources"]):
                ax.set_title(caption)
                ax.text(.5, .5, "Not measured on this OS", ha="center", va="center", transform=ax.transAxes)
                ax.set_axis_off()
                stats["resources"][key] = {v: {"med": None, "off_scale": []} for v in VERSIONS}
            else:
                stats["resources"][key] = boxes(ax, groups, unit, caption)
            stats["resource_units"][key] = unit
        finish(fig, output / filename, title,
               "Each dot = one process run; includes startup, dictionaries, sessions, warmups and JSON output.\n"
               "Off-scale values are listed in the report. RSS peak from OS; available memory samples at ~50 ms. Unavailable metrics marked unmeasured.\n" + stamp.split(" | n=")[0] + f" | n={process_count} processes / version",
               [stats["resources"][key] for key, *_ in metrics])
    return stats, panels


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--github-summary", type=Path, help="Append tables to a GitHub Actions step summary")
    parser.add_argument("--hardware", type=Path, help="Optional Windows processor metadata JSON")
    args = parser.parse_args()
    report = json.loads(args.report.read_text(encoding="utf-8"))
    if not report["accepted_performance_baseline"]:
        raise ValueError("Source/binary integrity verification failed; refuse performance charts")
    if args.hardware:
        report["hardware"] = json.loads(args.hardware.read_text(encoding="utf-8-sig"))
    output = args.output
    output.mkdir(parents=True, exist_ok=True)
    if any((output / name).with_suffix(ext).exists() for name in IMAGES + RUST_IMAGES for ext in (".png", ".svg")):
        raise ValueError("Choose a new output directory; existing charts are preserved")
    global VERSIONS, LABELS, COLORS
    rust_versions = report.get("comparison_kind") == "rust-versions"
    with_csharp = rust_versions and "csharp" in report.get("versions", [])
    runtime = report.get("csharp_runtime", "NativeAOT")
    if rust_versions:
        VERSIONS = ("old", "new")
        old_label = rust_label(report.get("old_version", report["old_ref"]))
        new_label = rust_label(report.get("current_version", report["new_ref"]))
        if old_label == new_label:
            old_label += " (before)"
            new_label += " (after)"
        LABELS = (old_label, new_label)
        baseline_label = report["old_ref"]
        current_label = report["new_ref"][:12]
        comparison_label = f"{old_label} vs {new_label}"
        if with_csharp:
            VERSIONS = ("csharp", "old", "new")
            LABELS = (f"C# {report['csharp_ref'][:12]}", old_label, new_label)
            comparison_label = f"C# {report['csharp_ref'][:12]} / " + comparison_label
        report["rust_commit"] = report["new_ref"]
        report["samples_per_round"] = report["samples"]
        report["warmups_per_case"] = report["warmups"]
        report["comparisons"] = [dict(case=row["case"], keys=row["keys"],
            median_total_ms=row["medians_ms"], current_over_baseline=row["new_over_old"],
            trace_matches=row["matching_projection"], eligible_for_speed_claim=row["eligible_for_speed_claim"],
            **{key: row[key] for key in ("new_over_csharp", "csharp_matching_projection",
                "eligible_for_csharp_speed_claim") if key in row})
            for row in report["results"]]
    else:
        VERSIONS = ("csharp", "rust")
        runtime = report["csharp_runtime"]
        LABELS = (f"C# {runtime}", "Rust Release")
        baseline_label = "v1.3.0" if report["baseline"] == "6310c44b728d5ad88284853f9ef37ea224993b18" else report["baseline"][:12]
        comparison_label = f"C# {baseline_label} {runtime} vs Rust {report['rust_commit'][:12]}"
        for row in report["comparisons"]:
            row["current_over_baseline"] = row["rust_over_csharp"]
    COLORS = ("#da8745", "#8c75b5", "#387cac") if with_csharp else ("#da8745", "#387cac")
    previous, current = ("old", "new") if rust_versions else VERSIONS
    rounds = report.get("rounds", len(report["order"]) // len(VERSIONS))
    process_count = sum(row["version"] == current for row in report["process_resources"])
    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11, "axes.titleweight": "bold"})
    compact = {k: v for k, v in report.items() if k not in ("rows", "evidence")}
    compact["rows"] = [{k: row[k] for k in ("case", "sample", "round", "version", "total_ms")}
                       for row in report["rows"]]
    compact["full_report_sha256"] = report.get("full_report_sha256", hashlib.sha256(args.report.read_bytes()).hexdigest())
    compact["plotting"] = dict(matplotlib=matplotlib.__version__, numpy=np.__version__,
                                quartile="linear interpolation (Hyndman-Fan type 7)",
                                whiskers="Most extreme observed points within Q1/Q3 +/- 1.5 IQR",
                                outliers="All observations retained; remote off-scale values listed in report",
                                axes="Per-panel limits; shared by both versions; automatic linear/log scale; outer 3 IQR fence",
                                speed_sample="one whole key sequence", resource_sample="one complete process run")
    (output / "summary.json").write_text(json.dumps(compact, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for name, rows, fields in (
        ("timings.csv", compact["rows"], ["case", "version", "round", "sample", "total_ms"]),
        ("resources.csv", [{k: v for k, v in row.items() if not isinstance(v, (dict, list))}
                           for row in report["process_resources"]], None),
    ):
        with (output / name).open("w", newline="", encoding="utf-8") as file:
            writer = csv.DictWriter(file, fieldnames=fields or list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    stats, panels = render_charts(report, output, IMAGES, comparison_label, rounds, process_count)
    rust_stats = None
    if with_csharp:
        full_versions, full_labels, full_colors = VERSIONS, LABELS, COLORS
        VERSIONS, LABELS, COLORS = full_versions[1:], full_labels[1:], full_colors[1:]
        rust_stats, _ = render_charts(report, output, RUST_IMAGES, " vs ".join(LABELS), rounds, process_count)
        VERSIONS, LABELS, COLORS = full_versions, full_labels, full_colors
        stats["rust_only"] = rust_stats
    (output / "boxplot-statistics.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
    matching = [r for r in report["comparisons"] if r["eligible_for_speed_claim"]]
    faster = sum(r["current_over_baseline"] < 1 for r in matching)
    rows = [f"# {report.get('host_os', 'Windows')} / {report.get('backend', 'core')}の測定（{report['measured_at'][:10]} UTC）", "",
            f"{comparison_label}。比較可能な入力は {len(matching)}/{len(report['comparisons'])}、現在版の中央値が短い入力は {faster}/{len(matching)}。",
            "", "## 条件と箱ひげ図の読み方", "",
            f"各入力・各版 {report['samples_per_round']} 試行 × {rounds} 巡、各巡 {report['warmups_per_case']} 回ウォームアップ。実行順は巡ごとに先頭を入れ替える循環順。CPU・RAMは各版 {process_count} プロセスを独立した観測値とする。",
            f"測定環境: {report['machine']} / {report.get('hardware', {}).get('Name',report['cpu'])} / {report['process_resources'][0]['logical_processors']}論理CPU / OS可視RAM {report['process_resources'][0]['system_memory_before']['total_bytes']/(1024**3):.2f} GiB。{report['rustc'].splitlines()[0]} {' / .NET ' + report.get('dotnet', 'not recorded (prebuilt)') if not rust_versions or with_csharp else ''}。", "",
            "四分位数は線形補間（type 7）。箱はQ1〜Q3、黒線は中央値、ひげはQ1−1.5×IQR〜Q3＋1.5×IQR内の最端の観測値。赤い菱形は外れ値。統計は全観測値から計算する。各パネルは通常値に合わせて表示範囲を自動調整し、全比較対象に同じ目盛を使う。正の値の幅が10倍を超える場合は対数、それ以外は線形。Q1−3×幅〜Q3＋3×幅から離れた外れ値（幅はIQRと中央値の5%の大きい方、最低1e-9）は軸の拡大に使わず、表示範囲外なら画像に件数、本文の表に値を記載する。青/橙の点は表示範囲内の観測値。", "",
            "速度はCoreの打鍵＋JSON応答生成。セッション生成・辞書構築・起動は区間外。CPU・RAMはそれらとウォームアップ・JSON出力を含むプロセス全体。外部変換・OSスペル・ライブ変換・予測・学習・TSF・候補描画・実アプリ入力は対象外。出力/比較対象の打鍵状態の一致は全UI・全候補の同等性を意味しない。", "",
            "## 全入力の中央値", ""]
    header = "|入力|" + "|".join(f"{label} (ms)" for label in LABELS) + "|現在/以前|Rust状態一致|"
    divider = "|---|" + "---:|" * len(VERSIONS) + "---:|:---:|"
    if with_csharp:
        header += "現在/C#|C#状態一致|"
        divider += "---:|:---:|"
    rows += [header, divider]
    for row in report["comparisons"]:
        line = "|" + row["case"] + "|" + "|".join(f"{row['median_total_ms'][v]:.4f}" for v in VERSIONS)
        line += f"|{row['current_over_baseline']:.4f}|{'一致' if row['trace_matches'] else '不一致'}|"
        if with_csharp:
            line += f"{row['new_over_csharp']:.4f}|{'一致' if row['csharp_matching_projection'] else '不一致'}|"
        rows.append(line)
    rows += ["", "## CPU・RAMのプロセス中央値", "",
             "|指標|" + "|".join(LABELS) + "|", "|---|" + "---:|" * len(VERSIONS)]
    for _, _, metrics in panels:
        for key, caption, unit, _ in metrics:
            values = stats["resources"][key]
            rows.append(f"|{caption.replace(chr(10), ' ')} ({unit})|" +
                        "|".join(f"{values[v]['med']:.4f}" if values[v]["med"] is not None else "未測定" for v in VERSIONS) + "|")
    rows += ["", *off_scale_table(stats)]
    if rust_stats:
        full_versions, full_labels = VERSIONS, LABELS
        VERSIONS, LABELS = full_versions[1:], full_labels[1:]
        rows += [line.replace("## 表示範囲外の測定値", "## Rustのみの図の表示範囲外の測定値")
                 for line in off_scale_table(rust_stats)]
        VERSIONS, LABELS = full_versions, full_labels
    rows += ["測定範囲: " + report.get("scope", "Coreのみ"), "", "ネイティブ接続部分の測定はOSのIMEサービス、画面描画、実アプリへの反映を含みません。OS間のCPUやランナーが異なるため、OSを横断した速度の順位には使いません。", "", "## 全画像", ""]
    image_groups = [("Rust前版・現在版（2者比較）", RUST_IMAGES),
                    ("C#・Rust前版・現在版（3者比較）", IMAGES)] if with_csharp else [("2者比較", IMAGES)]
    images = [name for _, names in image_groups for name in names]
    for heading, names in image_groups:
        rows += ["### " + heading, "", *[f"![{name}]({name})\n\n[拡大表示（SVG）]({Path(name).with_suffix('.svg')})\n" for name in names]]
    current_option = (f" --current-ref {report['new_ref']}" if report.get("current_source") == "tag/commit" else "")
    rows += ["## 証跡・再現", "", "[全測定の合計時間CSV](timings.csv)／[CPU・RAMの全観測CSV](resources.csv)／[中央値・四分位・全外れ値の数値](boxplot-statistics.json)／[測定条件・ハッシュ・コンパイラ・全合計時間](summary.json)", "",
             "```powershell", "$env:PYTHONUTF8='1'",
             (f"python tools/compare-rust-benchmarks.py --build --output dist/windows-benchmark --baseline-ref {report['old_ref']} --samples {report['samples_per_round']} --warmups {report['warmups_per_case']} --rounds {rounds}" + current_option + (" --backend native" if report.get("backend") == "native" else "") + (f" --with-csharp --csharp-ref {report['csharp_ref']}" + (' --jit' if runtime.startswith('JIT') else '') if with_csharp else '')
              if rust_versions else
              f"python tools/benchmark-windows.py --output dist/windows-benchmark --baseline-ref {report['baseline']} --samples {report['samples_per_round']} --warmups {report['warmups_per_case']} --rounds {rounds}"
              + (" --jit" if runtime.startswith("JIT") else "")),
             "# matplotlib/numpyを導入したPythonで実行。公開summary.jsonからも画像を再作成できる。",
             "python tools/plot-windows-benchmark.py <report.jsonまたはsummary.json> --output <新しいフォルダー>", "```", "",
             f"ソースとバイナリの前後一致: sources_unchanged={report['sources_unchanged']}, binaries_unchanged={report['binaries_unchanged']}。{'版ごとの同梱辞書を使用し、ハッシュを記録した。' if rust_versions else '全辞書のSHA-256一致を測定開始前に検査した。'}",
             "外部負荷やOSスケジューリングの影響を完全には除けず、今回の測定を普遍的な速度保証やIME全体の高速化率には使わない。", ""]
    if rust_versions:
        rows[3:3] = ["Rust両版を同じコンパイラでビルドし、全比較対象はそれぞれの版の同梱辞書を使用。コード・依存関係・辞書の差を含む版全体の比較です。", ""]
    if with_csharp:
        csharp_matching = [r for r in report["comparisons"] if r["eligible_for_csharp_speed_claim"]]
        rows[3:3] = [f"C# は {report['csharp_ref']} / {runtime}。現在版との状態一致: {len(csharp_matching)}/{len(report['comparisons'])}。不一致は速度改善の集計対象外。Rust現在版: {report.get('current_version', report['new_ref'])}。", ""]
    if args.github_summary:
        # Relative artifact images do not render in the Actions summary.
        summary = rows[:rows.index("## 全画像")]
        summary += ["", f"PNG {len(images)}枚・CSV・生データは、この実行のwindows-common-benchmark成果物に保存します。", "",
                    "GitHub共有ランナー上の観測値です。固定倍率による合否判定は行いません。", ""]
        with args.github_summary.open("a", encoding="utf-8") as file:
            file.write("\n".join(summary))
    (output / "README.md").write_text("\n".join(rows), encoding="utf-8")
    print(json.dumps({"output": str(output), "images": images, "matching_cases": len(matching), "faster_cases": faster}))


if __name__ == "__main__":
    main()
