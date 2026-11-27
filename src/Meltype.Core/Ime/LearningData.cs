// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Learning;

namespace Meltype.Ime;

/// <summary>
/// データフォルダーの学習データ: 選び直した変換 (conversions.json)・英字 / かなに直した語 (languages.json)・
/// 予測変換の語句 (phrases.txt)・選んだ英訳 (translations.json)・入力欄ごとの入力モード (fieldmodes.json)。
/// 入力する Meltype は <see cref="MeltypeRuntime"/> の中に 1 つ持ち、設定アプリは <see cref="OpenStandalone"/> で開く。
/// どちらも、ほかのプロセスが書き換えたファイルは保存せずに読み直す (<see cref="Config.FileStamp"/>)。一覧を返す前・消す前にも読み直す。
/// </summary>
public sealed class LearningData(string dataDirectory)
{
    internal ConversionHistory History { get; } = new(Path.Combine(dataDirectory, "conversions.json"));

    internal LanguageMemory LanguageMemory { get; } = new(Path.Combine(dataDirectory, "languages.json"));

    internal PhraseHistory Phrases { get; } = new(Path.Combine(dataDirectory, "phrases.txt"));

    internal TranslationHistory Translations { get; } = new(Path.Combine(dataDirectory, "translations.json"));

    internal FieldModes FieldModes { get; } = new(Path.Combine(dataDirectory, "fieldmodes.json"));

    /// <summary>
    /// 入力する Meltype の外 (設定アプリ) で開く。判定器は作らないので、学習した語が今有効か (<see cref="LearnedLanguage.Active"/>) を決める
    /// ローマ字の読みとよく使う日本語の読みだけをつなぐ (入力する Meltype では <see cref="MeltypeRuntime.Connect"/> がつなぐ)。
    /// </summary>
    public static LearningData OpenStandalone(string dataDirectory)
    {
        var data = new LearningData(dataDirectory);
        var romaji = RomajiDetector.CreateDefault(Path.Combine(dataDirectory, "dictionaries"));
        data.LanguageMemory.IsCommonJapanese = RomajiTypoCorrector.Load(romaji).IsCommonJapanese;
        data.LanguageMemory.IsReadableRomaji = LanguageMemory.ReadableBy(romaji);
        return data;
    }

    /// <summary>英字で打った語を英語・日本語のどちらで出すか学習したもの (新しく使ったものから)。</summary>
    public IReadOnlyList<LearnedLanguage> Languages()
    {
        Refresh();
        return LanguageMemory.Entries();
    }

    /// <summary>学習した語を忘れる。keys は <see cref="LearnedLanguage.EntryKey"/>。</summary>
    public void ForgetLanguages(IEnumerable<string> keys)
    {
        Refresh();
        LanguageMemory.Remove(keys);
    }

    /// <summary>読みに対して選び直した変換 (はし → 箸) の学習 (新しく使ったものから)。</summary>
    public IReadOnlyList<(string Reading, string Text, DateTime Used)> Conversions()
    {
        Refresh();
        return History.Entries();
    }

    /// <summary>読みに対して選び直した変換の学習を忘れる。</summary>
    public void ForgetConversion(string reading)
    {
        Refresh();
        History.Forget(reading);
    }

    /// <summary>
    /// sinceUtc より後に覚えたものだけを消す (一時的な入力・間違えて覚えたものを、全体をリセットせずに消す: issue #277)。消した数を返す。
    /// 選び直した変換・英字 / かなに直した語・予測変換の語句が対象。英訳の記録は覚えた時刻を持たないので、<see cref="Reset"/> で消す。
    /// </summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        Refresh();
        return History.ForgetSince(sinceUtc) + LanguageMemory.ForgetSince(sinceUtc) + Phrases.ForgetSince(sinceUtc);
    }

    /// <summary>学習データをすべて消す。ユーザー辞書と設定は消さない。</summary>
    public void Reset()
    {
        Refresh();
        History.Clear();
        LanguageMemory.Clear();
        Phrases.Clear();
        Translations.Clear();
        FieldModes.Clear();
        Diagnostics.Log.Info("学習データをリセットしました。");
    }

    /// <summary>
    /// ほかのプロセスが書き換えたファイルを読み直す (入力する Meltype が 1 秒ごとに呼ぶ)。
    /// 消す操作の前にも呼ぶ。読み直す前に消すと、保存のときに書き換えに気づいて読み直し、消したことが無くなる。
    /// ブラウザー版 (src/Meltype.Web) は、ほかのタブの学習データをファイルに書いたらすぐ呼ぶ。読み直す前に確定すると、
    /// 保存のときに書き換えに気づいて読み直し、確定して覚えたことが無くなる。
    /// </summary>
    public void Refresh()
    {
        History.Refresh();
        LanguageMemory.Refresh();
        Phrases.Refresh();
        Translations.Refresh();
        FieldModes.Refresh();
    }
}
