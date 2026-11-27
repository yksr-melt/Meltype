#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors
"""Compare the full synthetic key/session/JSON loop against a local baseline checkout.

No input method is installed. AOT is the default; --jit uses the release JIT.
Dictionary loading/startup/session creation are outside the timed region. The
converter returns no candidates. Measurements are local, not OS GUI latency.
"""
import argparse
import datetime
import hashlib
import json
import os
import platform
import statistics
import subprocess
import tempfile
from pathlib import Path
from xml.sax.saxutils import escape

CASES = ('long_url', 'natural_mixed', 'mixed_repeated', 'japanese')
FIELDS = ('case', 'length', 'sample', 'total_ms', 'allocated_bytes', 'key_p50_ms', 'key_p95_ms', 'key_max_ms', 'output_sha256')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path, help='local checkout containing src/Meltype.Core')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=15)
    parser.add_argument('--lengths', default='64,128,256')
    parser.add_argument('--jit', action='store_true')
    parser.add_argument('--common-dictionaries', action='store_true', help='embed the current dictionaries in both builds')
    args = parser.parse_args()
    if args.samples < 3:
        parser.error('at least three samples are required')
    baseline = args.baseline.resolve(strict=True)
    repo = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    lengths = [int(n) for n in args.lengths.split(',')]
    if not lengths or any(n < 1 or n > 256 for n in lengths):
        parser.error('lengths must be between 1 and 256')
    env = os.environ.copy()
    env['DOTNET_TieredCompilation'] = '0'
    rows = []
    with tempfile.TemporaryDirectory(prefix='meltype-benchmark-') as temporary:
        root = Path(temporary)
        programs = {}
        for label, checkout in [('baseline', baseline), ('current', repo)]:
            directory = root / label
            directory.mkdir()
            core = checkout / 'src/Meltype.Core/Meltype.Core.csproj'
            if args.common_dictionaries:
                import shutil
                copied = directory / 'Core'
                shutil.copytree(core.parent, copied, ignore=shutil.ignore_patterns('bin', 'obj'))
                core = copied / core.name
                core.write_text(core.read_text().replace('..\\..\\dictionaries\\*.txt', str(repo / 'dictionaries/*.txt')))
            (directory / 'Program.cs').write_bytes((repo / 'src/Meltype.Benchmark/Program.cs').read_bytes())
            project = directory / 'Benchmark.csproj'
            project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>
<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/>
<ProjectReference Include="%s"/></ItemGroup></Project>''' % escape(str(core)))
            command = ['dotnet', 'publish', str(project), '-c', 'Release', '-o', str(directory / 'bin')]
            if not args.jit:
                command += ['-p:PublishAot=true']
            with (output / (label + '-build.log')).open('w') as log:
                subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
            programs[label] = (['dotnet', str(directory / 'bin/Benchmark.dll')] if args.jit else [str(directory / 'bin/Benchmark')])
        # Alternate which version runs first across cases to reduce order bias.
        for index, case in enumerate(CASES):
            labels = ('baseline', 'current') if index % 2 == 0 else ('current', 'baseline')
            for label in labels:
                print('Measuring', label, case, flush=True)
                result = subprocess.check_output(programs[label] + [str(args.samples), ','.join(map(str, lengths)), case], env=env, text=True)
                with (output / (label + '.tsv')).open('a') as log:
                    log.write(result)
                for line in result.splitlines():
                    values = line.split('\t')
                    if len(values) != len(FIELDS):
                        raise RuntimeError('Invalid benchmark row: ' + line)
                    row = dict(zip(FIELDS, values))
                    row.update(version=label, length=int(row['length']), sample=int(row['sample']))
                    for field in FIELDS[3:8]:
                        row[field] = float(row[field])
                    rows.append(row)
    comparisons = []
    for length in lengths:
        for case in CASES:
            groups = {v: [r for r in rows if r['version'] == v and r['length'] == length and r['case'] == case] for v in ('baseline', 'current')}
            medians = {v: {field: statistics.median(r[field] for r in group) for field in FIELDS[3:8]} for v, group in groups.items()}
            hashes = {v: sorted({r['output_sha256'] for r in group}) for v, group in groups.items()}
            ratios = {field: medians['current'][field] / medians['baseline'][field] for field in FIELDS[3:8]}
            comparisons.append(dict(case=case, length=length, medians=medians, ratios=ratios, output_hashes=hashes,
                                    output_matches=hashes['baseline'] == hashes['current']))
    report = dict(measured_at=datetime.datetime.now(datetime.timezone.utc).isoformat(), machine=platform.platform(),
                  dotnet=subprocess.check_output(['dotnet', '--version'], text=True).strip(), aot=not args.jit,
                  samples=args.samples, warmups=3, common_dictionaries=args.common_dictionaries,
                  scope='synthetic key/session/JSON loop, null converter; excludes startup and OS GUI',
                  program_sha256=digest(repo / 'src/Meltype.Benchmark/Program.cs'),
                  dictionaries={v: {p.name: digest(p) for p in (checkout / 'dictionaries').glob('*.txt')}
                                for v, checkout in [('baseline', repo if args.common_dictionaries else baseline), ('current', repo)]},
                  comparisons=comparisons, rows=rows)
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    for row in comparisons:
        print(row['case'], row['length'], 'time ratio', round(row['ratios']['total_ms'], 4), 'output matches', row['output_matches'])
    if not all(row['output_matches'] for row in comparisons):
        raise SystemExit('Committed output differs from the baseline')


if __name__ == '__main__':
    main()
