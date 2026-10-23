// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meltype.Config;

namespace Meltype.Input;

/// <summary>
/// 打鍵数のカウンター (typect / タイプ数カウンターのように、合計・日ごと・キーごとに数える)。
/// フックのスレッドから Count を呼び、ファイル (keycount.json) には Save でまとめて書く。
/// 打った文字は残さない (どのキーを何回押したかだけ)。
/// </summary>
internal sealed partial class KeyCounter
{
    public sealed class Data
    {
        public long Total { get; set; }
        /// <summary>日 (yyyy-MM-dd) ごとの打鍵数。</summary>
        public Dictionary<string, long> Days { get; set; } = [];
        /// <summary>時刻 (0〜23 時) ごとの打鍵数。</summary>
        public long[] Hours { get; set; } = new long[24];
        /// <summary>キーの名前 (A・Space・Enter など) ごとの打鍵数。</summary>
        public Dictionary<string, long> Keys { get; set; } = [];
    }

    private readonly string? _path;
    private readonly Lock _lock = new();
    private readonly HashSet<int> _held = [];
    private Data _data = new();
    private bool _dirty;

    public KeyCounter(string? path)
    {
        _path = path;
        DataFile.Load(path, "打鍵数", json =>
        {
            var data = JsonSerializer.Deserialize(json, KeyCountJsonContext.Default.Data) ?? new Data();
            data.Days ??= [];
            data.Keys ??= [];
            if (data.Hours is not { Length: 24 }) data.Hours = new long[24];
            _data = data;
        });
    }

    public static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>1 打鍵を数える。押しっぱなしの繰り返しは数えない (離すまで 1 回)。</summary>
    public void Count(KeyEvent e, DateTime now)
    {
        lock (_lock)
        {
            if (e.IsUp)
            {
                _held.Remove(e.Vk);
                return;
            }
            if (!_held.Add(e.Vk)) return;
            Add(KeyName(e.Vk), DayKey(now), 1);
            _data.Hours[now.Hour]++;
        }
    }

    private void Add(string? key, string? day, long count)
    {
        _data.Total += count;
        if (day is not null) _data.Days[day] = _data.Days.GetValueOrDefault(day) + count;
        if (key is not null) _data.Keys[key] = _data.Keys.GetValueOrDefault(key) + count;
        _dirty = true;
    }

    public long Total { get { lock (_lock) return _data.Total; } }

    public long OnDay(DateTime day) { lock (_lock) return _data.Days.GetValueOrDefault(DayKey(day)); }

    /// <summary>多い順のキー。</summary>
    public List<KeyValuePair<string, long>> TopKeys()
    {
        lock (_lock) return [.. _data.Keys.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)];
    }

    /// <summary>日の新しい順。</summary>
    public List<KeyValuePair<string, long>> RecentDays()
    {
        lock (_lock) return [.. _data.Days.OrderByDescending(p => p.Key, StringComparer.Ordinal)];
    }

    public sealed record Analysis(
        int ActiveDays, long DailyAverage, string? BestDay, long BestDayCount, int Streak,
        long Last7Days, long Previous7Days, int PeakHour, long[] Hours,
        double LeftHandShare, double CorrectionShare, double SpaceShare, double ModifierShare);

    // 英字・数字をタッチタイピングで打つ手 (左手)
    private static readonly HashSet<string> LeftHandKeys = [.. "QWERTASDFGZXCVB12345".Select(c => c.ToString())];
    private static readonly HashSet<string> RightHandKeys = [.. "YUIOPHJKLNM67890".Select(c => c.ToString()), "-", "^", "\\", "@", "[", ";", ":", "]", ",", ".", "/"];

    /// <summary>打鍵の傾向 (1 日の平均・いちばん打った日・続けて打った日数・先週との比較・よく打つ時間帯・左右の手・打ち直しの割合)。</summary>
    public Analysis Analyze(DateTime today)
    {
        lock (_lock)
        {
            var active = _data.Days.Where(p => p.Value > 0).ToList();
            var best = active.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
            var streak = 0;
            // 今日まだ打っていなければ昨日から数える
            var day = _data.Days.GetValueOrDefault(DayKey(today)) > 0 ? today : today.AddDays(-1);
            while (_data.Days.GetValueOrDefault(DayKey(day)) > 0) { streak++; day = day.AddDays(-1); }
            long Range(int from, int to) => Enumerable.Range(from, to - from).Sum(i => _data.Days.GetValueOrDefault(DayKey(today.AddDays(-i))));
            var keyTotal = (double)Math.Max(1, _data.Keys.Values.Sum());
            double Share(params string[] keys) => keys.Sum(k => _data.Keys.GetValueOrDefault(k)) / keyTotal;
            var left = _data.Keys.Where(p => LeftHandKeys.Contains(p.Key)).Sum(p => p.Value);
            var right = _data.Keys.Where(p => RightHandKeys.Contains(p.Key)).Sum(p => p.Value);
            var hours = (long[])_data.Hours.Clone();
            var peak = hours.Max() > 0 ? Array.IndexOf(hours, hours.Max()) : -1;
            return new Analysis(
                active.Count,
                active.Count == 0 ? 0 : active.Sum(p => p.Value) / active.Count,
                best.Key, best.Value, streak,
                Range(0, 7), Range(7, 14), peak, hours,
                left + right == 0 ? 0 : (double)left / (left + right),
                Share("BackSpace", "Delete"), Share("Space"), Share("Shift", "Ctrl", "Alt", "Win"));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _data = new Data();
            _dirty = true;
        }
    }

    /// <summary>変わっていればファイルに書く。</summary>
    public void Save()
    {
        if (_path is null) return;
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            json = JsonSerializer.Serialize(_data, KeyCountJsonContext.Default.Data);
            _dirty = false;
        }
        try { DataFile.Write(_path, temp => File.WriteAllText(temp, json)); }
        catch (Exception ex)
        {
            lock (_lock) _dirty = true;
            Diagnostics.Log.Warn($"打鍵数を保存できませんでした: {ex.Message}");
        }
    }

    public readonly record struct ImportResult(long Total, int Keys, int Days);

    /// <summary>
    /// typect (タイプ数カウンター) などで数えた打鍵数を足す。typect の一覧をコピーしたものや CSV / TSV を読む。
    ///   「A,123」「A	123」「A: 123」… キーごと
    ///   「2026/10/19,4567」 … 日ごと
    ///   「合計 12345」「Total=12345」「12345」 … 合計 (キーごと・日ごとの和より大きければこちらを合計にする)
    /// 数の桁区切り (12,345) も読む。読めなかった行は飛ばす。
    /// </summary>
    public ImportResult Import(string text)
    {
        var keys = new Dictionary<string, long>(StringComparer.Ordinal);
        var days = new Dictionary<string, long>(StringComparer.Ordinal);
        long? total = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Replace("\"", "").Trim().Trim('\uFEFF');
            if (line.Length == 0) continue;
            var m = ImportLine().Match(line);
            if (!m.Success) continue;
            var label = m.Groups[1].Value.Trim();
            if (!long.TryParse(m.Groups[2].Value.Replace(",", "").Replace("_", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var count)) continue;
            if (label.Length == 0 || IsTotalLabel(label))
            {
                total = Math.Max(total ?? 0, count);
                continue;
            }
            if (TryParseDay(label, out var day))
            {
                days[day] = days.GetValueOrDefault(day) + count;
                continue;
            }
            var key = NormalizeKeyName(label);
            if (key is null) continue;
            keys[key] = keys.GetValueOrDefault(key) + count;
        }
        var sum = Math.Max(keys.Values.Sum(), days.Values.Sum());
        var added = Math.Max(total ?? 0, sum);
        lock (_lock)
        {
            _data.Total += added;
            foreach (var (k, v) in keys) _data.Keys[k] = _data.Keys.GetValueOrDefault(k) + v;
            foreach (var (d, v) in days) _data.Days[d] = _data.Days.GetValueOrDefault(d) + v;
            if (added > 0) _dirty = true;
        }
        return new ImportResult(added, keys.Count, days.Count);
    }

    // 名前 (無くてもよい) と、行の最後の数。区切りは , タブ : = 空白
    [GeneratedRegex(@"^(?:(.*?\S)[\s,;:=]+)?([0-9][0-9,_]*)\s*(?:回|打|keys?)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ImportLine();

    private static bool IsTotalLabel(string label) =>
        label.Replace(" ", "").ToLowerInvariant() is "合計" or "総計" or "総数" or "total" or "合計打鍵数" or "総打鍵数" or "タイプ数" or "総タイプ数" or "全キー" or "sum" or "all";

    private static bool TryParseDay(string label, out string day)
    {
        foreach (var format in (string[])["yyyy/M/d", "yyyy-M-d", "yyyy.M.d", "yyyy年M月d日"])
        {
            if (DateTime.TryParseExact(label, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                day = DayKey(date);
                return true;
            }
        }
        day = "";
        return false;
    }

    /// <summary>ほかのソフトのキーの名前を、Meltype の名前にそろえる (知らない名前はそのまま残す)。</summary>
    internal static string? NormalizeKeyName(string label)
    {
        var name = label.Trim();
        if (name.Length == 0 || name.Length > 32) return null;
        var lower = name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
        if (lower.StartsWith("vk", StringComparison.Ordinal) && lower.Length > 2 &&
            int.TryParse(lower.AsSpan(2).TrimStart("0x"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk))
            return KeyName(vk);
        if (name.Length == 1) return char.ToUpperInvariant(name[0]).ToString();
        return Aliases.TryGetValue(lower, out var alias) ? alias : name;
    }

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["space"] = "Space", ["スペース"] = "Space", ["spacebar"] = "Space",
        ["enter"] = "Enter", ["return"] = "Enter", ["エンター"] = "Enter",
        ["backspace"] = "BackSpace", ["back"] = "BackSpace", ["bs"] = "BackSpace", ["バックスペース"] = "BackSpace",
        ["delete"] = "Delete", ["del"] = "Delete", ["tab"] = "Tab", ["escape"] = "Esc", ["esc"] = "Esc",
        ["shift"] = "Shift", ["lshift"] = "Shift", ["rshift"] = "Shift", ["leftshift"] = "Shift", ["rightshift"] = "Shift",
        ["ctrl"] = "Ctrl", ["control"] = "Ctrl", ["lctrl"] = "Ctrl", ["rctrl"] = "Ctrl", ["lcontrol"] = "Ctrl", ["rcontrol"] = "Ctrl",
        ["alt"] = "Alt", ["menu"] = "Alt", ["lalt"] = "Alt", ["ralt"] = "Alt", ["lmenu"] = "Alt", ["rmenu"] = "Alt",
        ["win"] = "Win", ["lwin"] = "Win", ["rwin"] = "Win", ["windows"] = "Win",
        ["半角/全角"] = "半角/全角", ["半角全角"] = "半角/全角", ["変換"] = "変換", ["無変換"] = "無変換",
        ["capslock"] = "CapsLock", ["caps"] = "CapsLock", ["英数"] = "CapsLock",
        ["left"] = "←", ["right"] = "→", ["up"] = "↑", ["down"] = "↓",
    };

    /// <summary>仮想キーコードの名前。左右のある修飾キーは 1 つにまとめる。</summary>
    internal static string KeyName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        VirtualKeys.Space => "Space",
        VirtualKeys.Return => "Enter",
        VirtualKeys.Back => "BackSpace",
        VirtualKeys.Tab => "Tab",
        VirtualKeys.Escape => "Esc",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        VirtualKeys.Left => "←",
        VirtualKeys.Up => "↑",
        VirtualKeys.Right => "→",
        VirtualKeys.Down => "↓",
        0x14 => "CapsLock",
        VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift => "Shift",
        VirtualKeys.Control or VirtualKeys.LControl or VirtualKeys.RControl => "Ctrl",
        VirtualKeys.Menu or VirtualKeys.LMenu or VirtualKeys.RMenu => "Alt",
        VirtualKeys.LWin or VirtualKeys.RWin => "Win",
        VirtualKeys.OemAuto or VirtualKeys.OemEnlw or VirtualKeys.Kanji => "半角/全角",
        VirtualKeys.Convert => "変換",
        VirtualKeys.NonConvert => "無変換",
        VirtualKeys.Kana or VirtualKeys.OemCopy => "カタカナ/ひらがな",
        VirtualKeys.OemMinus => "-",
        VirtualKeys.OemComma => ",",
        VirtualKeys.OemPeriod => ".",
        VirtualKeys.Oem2 => "/",
        VirtualKeys.Oem4 => "[",
        VirtualKeys.Oem6 => "]",
        0xBA => ":",
        0xBB => ";",
        0xC0 => "@",
        0xDC or 0xE2 => "\\",
        0xDE => "^",
        0x6A => "Num*",
        0x6B => "Num+",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        _ => $"VK{vk:X2}",
    };
}
