# Using the Mozc-UT dictionary

[日本語版: MOZC-UT.md](MOZC-UT.md)

Meltype's Mozc engine can include the [Mozc-UT](https://github.com/utuhiro78/merge-ut-dictionaries) dictionary (`mozcdic-ut.txt`),
which adds personal names, place names and Wikipedia-derived words to the conversion candidates. This is an optional feature for builds from source.

**Nothing is merged by default.** Unless you ask for it, the helper is built from plain Mozc exactly as before.
The UT dictionary is added only when you explicitly point to one.

- The same environment variable works on Windows and Linux: `MELTYPE_UT_DICTIONARY=/path/to/mozcdic-ut.txt`
- The C# conversion engine is not modified. Only the conversion helper (`meltype_mozc_helper`) is rebuilt.
- Learning data is unaffected.
- The macOS build uses azooKey and is not affected by this feature.

## Read this first: licenses and redistribution

Mozc-UT dictionaries are optional and are **not bundled with Meltype**. Meltype only merges the file you pass in `MELTYPE_UT_DICTIONARY` into your own local Mozc build; it never downloads or redistributes that data.

UT data is mixed-license: Japan Post ZIP-code data (no copyright asserted), Apache-2.0 (SudachiDict, NEologd, the personal-names file), CC BY-SA (Wikipedia-derived entries and EDICT2), GPL-2.0-or-later (SKK-JISYO), and GPL-2.0-only (alt-cannadic).
If you build a UT-enabled binary and distribute it, **you** — not Meltype — must comply with each source's license. In particular, the Wikipedia-derived parts are CC BY-SA: give attribution, state that the data was modified, include the license text or a link, and keep the ShareAlike terms.
The UT project does not state which CC BY-SA version applies, and CC BY-SA 3.0 material cannot be combined with GPLv3. **alt-cannadic is GPL-2.0-only and must not be combined with Meltype.**

A UT-merged binary is your own build, not an official Meltype artifact, and is not covered by Meltype's support or warranties.
Per-dictionary details are in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) (Japanese) under "Mozc-UT 辞書".

## Getting a UT dictionary

### Build one with merge-ut-dictionaries (recommended)

`mozcdic-ut.txt` is produced by [merge-ut-dictionaries](https://github.com/utuhiro78/merge-ut-dictionaries).
With the default settings it does **not** fetch the 4.2 GB Wikipedia dump; it clones the per-dictionary repositories, which ship pre-built `.txt.bz2` files, and concatenates them.

```bash
git clone --depth 1 https://github.com/utuhiro78/merge-ut-dictionaries.git
cd merge-ut-dictionaries/src/merge
```

`make.sh` selects which dictionaries to use. `jawiki` (Wikipedia-derived) is enabled by default. Keep it if you want those words, or change `jawiki="true"` to `#jawiki="true"` to leave it out (the Meltype verification used `personal_names`, `place_names` and `sudachidict`).

```sh
sh make.sh
ls -lh mozcdic-ut.txt
```

Notes:

- `make.sh` calls `python`. On systems that only have `python3`, provide a `python` alias.
- For cost adjustment, `merge_dictionaries.py` downloads the current Mozc source zip and the Wikipedia multistream **index** (about 31 MB). That is not the 4.2 GB article dump.
- `generate_latest="true"` regenerates every dictionary from the latest upstream data. That path downloads the 4.2 GB dump, so do not make it a requirement of a normal build.

### Record what you built

Keep the versions and hashes so the same dictionary gives the same result. Meltype writes what it merged into `native/mozc/bin/MOZC-UT.json` (dictionary SHA-256, Mozc commit, `id.def` SHA-256, POS-ID remapping, number of removed entries).

The combination used for the verification (kept here for reproducibility):

| Item | Version |
| --- | --- |
| merge-ut-dictionaries | `640844881690e877d0e54dff024edb8ec7b77290` |
| personal names | `b557dd4c1548cc7c2164d5f8b14170c40cbb6f81` |
| place names | `98cf8e9d7731695a52e5bc72ed633933c71d1913` |
| SudachiDict | `13129ddebe1cb8eabb63feccbe157adec4e1de01` |
| mozcdic-ut.txt produced | 1,142,950 lines, SHA-256 `0c16fa79ca6c4958bb5c4cc7378dadd17d382e987c586b90d82a50e452030c15` |
| Mozc | `a069a88d4cb5c011de0f9aebb6c149a1c808d904` (`native/mozc/MOZC_COMMIT`) |

## Build on Linux

The requirements are the same as for a normal build (see `native/mozc/README.md`). Just add the environment variable.
Handling a UT dictionary needs Python 3.10 or later (`PYTHON` selects the interpreter).

```bash
MELTYPE_UT_DICTIONARY=~/mozcdic-ut.txt native/mozc/build-mozc-helper.sh
```

`native/mozc/bin/linux/meltype_mozc_helper` now contains the UT dictionary.

## Build on Windows

Set the environment variable in PowerShell, then run the usual script.

```powershell
$env:MELTYPE_UT_DICTIONARY = 'C:\path\to\mozcdic-ut.txt'
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

You can also pass `-UtDictionary 'C:\path\to\mozcdic-ut.txt'`; the parameter wins over the environment variable.
`native\mozc\bin\` then holds the UT-enabled helper. `Build-Package.ps1` / `Install-Meltype.ps1` package `bin\` as before.

To do it in GitHub Actions, add the environment variable to the `mozc` job of `.github/workflows/build.yml`:

```yaml
      - name: Build Mozc helper
        env:
          MELTYPE_UT_DICTIONARY: C:\ut\mozcdic-ut.txt
        run: ...
```

## Check that it is enabled

1. `native/mozc/bin/MOZC-UT.json` exists (on Linux: `native/mozc/bin/linux/MOZC-UT.json`) and `dictionary.entries_used` is greater than 0.
2. A word that only exists in that dictionary shows up as a candidate. On Linux you can ask the helper directly:

```bash
printf 'C\t\tいちじょうりゅうのすけ\nQ\n' | native/mozc/bin/linux/meltype_mozc_helper
```

After `READY`, the next line holds the candidates separated by `<US>` (0x1F). A UT-enabled build lists `一条龍之介`; a plain Mozc build does not.
On Windows, type the same reading in Meltype and check that the candidate appears.

## Going back to plain Mozc

Rebuild without the environment variable.

```bash
native/mozc/build-mozc-helper.sh
```

```powershell
Remove-Item Env:\MELTYPE_UT_DICTIONARY
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

A build without the variable empties the file that holds the UT words (`dictionary_ut.txt`) before building, so no UT words remain (if you built with v1.3.1-pre.1, see "Updating from v1.3.1-pre.1" below).

## Updating from v1.3.1-pre.1

If you built with `MELTYPE_UT_DICTIONARY` on v1.3.1-pre.1, Mozc's `dictionary00.txt` still contains the Mozc-UT words.
The current build scripts never modify `dictionary00.txt`, so those words also end up in builds without the variable, and a newer UT dictionary keeps the old dictionary's words and costs.
After updating, run the following once to restore the file. Building with the variable afterwards adds the Mozc-UT words again.

```bash
git -C ~/mozc checkout HEAD -- src/data/dictionary_oss/dictionary00.txt
```

```powershell
git -C $env:USERPROFILE\mozc checkout HEAD -- src/data/dictionary_oss/dictionary00.txt
```

If you keep Mozc somewhere else, replace `~/mozc` (`$env:USERPROFILE\mozc` on Windows) with that location.

## Performance and size (measured)

Measured on Linux x86_64 (6 cores, clang 21.1.8, Bazel 9.0.2, Mozc `a069a88d`) with `personal_names`, `place_names` and `sudachidict` merged (1,142,950 entries).
These numbers are specific to that machine.

| | Plain Mozc | With Mozc-UT |
| --- | --- | --- |
| Helper size | 24,625,632 bytes (23.5 MiB) | 41,351,072 bytes (39.4 MiB) (+68%) |
| Startup (to `READY`, median of 9) | 9.96 ms | 11.5 ms |
| One conversion (median of 200) | 0.209 ms | 0.213 ms |
| Memory (RSS right after startup) | 21.5 MB | 27.8 MB |
| Dictionary text (`dictionary00.txt` + `dictionary_ut.txt`) | 7.2 MB | 76.7 MB |

- Conversion speed is essentially unchanged (+2% at the median). Memory and binary size grow.
- Each additional dictionary increases build time, size and memory.
- Build caching: Bazel keys its cache on input contents, so plain and UT builds cannot be confused. After a UT build, rebuilding plain Mozc produced a binary **byte-for-byte identical** to the original plain build (SHA-256 `84929176b0f5265c41e64690775398312ff9ed7cd8e41ca43937d70ea9e71c82`).

## How it works

1. The build script adds `dictionary_ut.txt` to the dictionary list in Mozc's `src/data/dictionary_oss/BUILD.bazel` and empties `dictionary_ut.txt`. Mozc's own dictionaries (`dictionary00.txt` to `dictionary09.txt`) are never modified.
2. When the variable is set, `native/mozc/merge-ut-dictionary.py` reads the given `mozcdic-ut.txt` and validates it: UTF-8 (BOM and NUL bytes rejected), exactly 5 columns, no empty columns, POS IDs (lid/rid) present in `id.def`, cost within 0–65535, and readings in hiragana (a warning by default; an error with `--strict-readings`).
3. It aligns POS IDs with the `id.def` of **the Mozc being built**. UT output is written with the POS IDs of the Mozc it was generated from, so IDs can mean something else on a different version. `merge-ut-dictionaries` emits a single POS (general noun by default); if that ID is not the current general-noun ID, it is remapped and recorded in `MOZC-UT.json`. Custom dictionaries with several POS IDs keep the IDs that exist.
4. It drops entries that duplicate Mozc's existing dictionary (same reading and surface) and duplicates within the UT file. If you edited Mozc's dictionary locally, it compares against the edited contents.
5. It writes the entries in a deterministic order to `dictionary_ut.txt`, replacing any previous contents. Mozc does not depend on dictionary line order.
6. Bazel rebuilds the embedded Mozc dictionary and links the helper.

No code from `merge-ut-dictionaries` is used; the merge is implemented for Meltype.

## Troubleshooting

| Message | Meaning and fix |
| --- | --- |
| `辞書ファイルを UTF-8 として読めません` | Not UTF-8. Check that the file is `mozcdic-ut.txt` |
| `辞書ファイルの先頭に BOM があります` | Remove the BOM |
| `列が N 個あります (5 列ではありません)` | Not a `mozcdic-ut.txt` (reading<Tab>lid<Tab>rid<Tab>cost<Tab>surface, 5 columns) |
| `品詞 ID が、ビルドする Mozc の id.def にありません` | The ID does not exist in this Mozc version. Drop `--keep-pos-ids` or regenerate the dictionary |
| `Mozc の id.def が見つかりません` | The Mozc checkout is incomplete. Delete the Mozc folder and retry |
| `BUILD.bazel に dictionary_ut.txt がありません` | `merge-ut-dictionary.py` was run directly. Run it through the build script |

You can also validate a dictionary without writing `dictionary_ut.txt` (this still reads the whole UT dictionary and
Mozc's dictionary, so expect roughly 1 GB of memory for a dictionary with over a million entries):

```bash
python3 native/mozc/merge-ut-dictionary.py --mozc-src ~/mozc/src \
    --dictionary ~/mozcdic-ut.txt --check
```

## Known limitations

- **Only the Mozc engine** is affected (the Windows/Linux `meltype_mozc_helper`). The macOS build uses azooKey.
- The UT dictionary is added to Mozc at build time, so switching dictionaries requires rebuilding the helper (re-embedding the dictionary). Bazel's cache makes later builds take a few minutes.
- The build script leaves `dictionary_ut.txt` listed in Mozc's `src/data/dictionary_oss/BUILD.bazel`. If you delete `dictionary_ut.txt`, running `bazel build` directly in that Mozc checkout fails. Build through the build script.
- Candidate quality and ordering come from the UT dictionary; Meltype neither reorders nor filters it.
- Licenses differ per dictionary. In particular, **do not use a dictionary that includes alt-cannadic (GPL-2.0-only)**.
