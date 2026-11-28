// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

using System.Globalization;
using System.Text.Json;
using Meltype.Config;

namespace Meltype.Dictionary;

/// <summary>化学式の質量計算に使う原子量。データフォルダーの atomic-weights.json があれば、その元素だけ値を上書きする。</summary>
internal sealed class AtomicWeights
{
    private const long CheckIntervalMs = 1000;
    private static readonly Dictionary<string, decimal> Standard = ParseStandard();
    private readonly object _gate = new();
    private readonly string? _path;
    private Dictionary<string, decimal> _overrides = new(StringComparer.Ordinal);
    private DateTime _writeTime;
    private long _checkedMs;
    private bool _loaded;

    public AtomicWeights(string? dataDirectory)
    {
        _path = dataDirectory is null ? null : Path.Combine(dataDirectory, "atomic-weights.json");
        Refresh(force: true);
    }

    public decimal? Get(string symbol)
    {
        lock (_gate)
        {
            if (_overrides.TryGetValue(symbol, out var weight)) return weight;
            return Standard.TryGetValue(symbol, out var standard) ? standard : null;
        }
    }

    /// <summary>打鍵のたびにファイルを読まないよう、Runtime のバックグラウンド確認から呼ぶ。</summary>
    public bool Refresh() => Refresh(force: false);

    private bool Refresh(bool force)
    {
        if (_path is null) return false;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            if (!force && now - _checkedMs < CheckIntervalMs) return false;
            _checkedMs = now;

            DateTime writeTime;
            try { writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue; }
            catch (Exception ex) when (DataFile.IsFileError(ex))
            {
                Diagnostics.Log.Warn($"原子量の上書きを確認できませんでした: {ex.Message}");
                return false;
            }
            if (_loaded && writeTime == _writeTime) return false;

            Dictionary<string, decimal> overrides;
            if (writeTime == DateTime.MinValue) overrides = new(StringComparer.Ordinal);
            else
            {
                try { overrides = ParseOverrides(File.ReadAllText(_path)); }
                catch (Exception ex)
                {
                    DataFile.ReportUnreadable("原子量の上書き (前の値を使い続けます)", ex);
                    _writeTime = writeTime;
                    _loaded = true;
                    return false;
                }
            }

            _overrides = overrides;
            _writeTime = writeTime;
            _loaded = true;
            Diagnostics.Log.Info("原子量の上書きを読み込みました。");
            return true;
        }
    }

    private static Dictionary<string, decimal> ParseOverrides(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("最上位は元素記号と原子量の組を持つ JSON オブジェクトにしてください。");
        var overrides = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!ChemicalFormula.IsElement(property.Name) || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDecimal(out var weight) || weight <= 0 || weight > 100_000)
                throw new FormatException($"元素記号「{property.Name}」または原子量が正しくありません。");
            overrides[property.Name] = weight;
        }
        return overrides;
    }

    private static Dictionary<string, decimal> ParseStandard()
    {
        // CIAAW 2024 の Abridged Standard Atomic Weights。計算に使う代表値を保ち、値が定められていない元素は含めない。
        // https://www.ciaaw.org/abridged-atomic-weights.htm
        const string values = """
            H 1.0080; He 4.0026; Li 6.94; Be 9.0122; B 10.81; C 12.011; N 14.007; O 15.999; F 18.998; Ne 20.180;
            Na 22.990; Mg 24.305; Al 26.982; Si 28.085; P 30.974; S 32.06; Cl 35.45; Ar 39.95; K 39.098; Ca 40.078;
            Sc 44.956; Ti 47.867; V 50.942; Cr 51.996; Mn 54.938; Fe 55.845; Co 58.933; Ni 58.693; Cu 63.546; Zn 65.38;
            Ga 69.723; Ge 72.630; As 74.922; Se 78.971; Br 79.904; Kr 83.798; Rb 85.468; Sr 87.62; Y 88.906; Zr 91.222;
            Nb 92.906; Mo 95.95; Ru 101.07; Rh 102.91; Pd 106.42; Ag 107.87; Cd 112.41; In 114.82; Sn 118.71; Sb 121.76;
            Te 127.60; I 126.90; Xe 131.29; Cs 132.91; Ba 137.33; La 138.91; Ce 140.12; Pr 140.91; Nd 144.24; Sm 150.36;
            Eu 151.96; Gd 157.25; Tb 158.93; Dy 162.50; Ho 164.93; Er 167.26; Tm 168.93; Yb 173.05; Lu 174.97; Hf 178.49;
            Ta 180.95; W 183.84; Re 186.21; Os 190.23; Ir 192.22; Pt 195.08; Au 196.97; Hg 200.59; Tl 204.38; Pb 207.2;
            Bi 208.98; Th 232.04; Pa 231.04; U 238.03
            """;
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var pair in values.Split([';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Chunk(2))
        {
            if (pair.Length == 2) result.Add(pair[0], decimal.Parse(pair[1], CultureInfo.InvariantCulture));
        }
        return result;
    }
}
