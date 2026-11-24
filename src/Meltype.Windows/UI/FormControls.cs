// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.UI;

/// <summary>一覧の画面 (学習した語・定型文・ユーザー辞書) で共通の部品。</summary>
internal static class FormControls
{
    /// <summary>行ごとに選ぶ、読み取り専用の一覧。</summary>
    public static DataGridView ReadOnlyGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
    };

    /// <summary>入力の誤りなどを出す赤い文字のラベル。</summary>
    public static Label ErrorLabel() => new() { AutoSize = true, ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 0) };
}
