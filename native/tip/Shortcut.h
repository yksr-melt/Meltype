// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W
#pragma once
#include <vector>

namespace meltype
{
  // Meltype.Input.ToggleShortcut と修飾キーフラグの定義を一致させる。
  // Shift=1, Ctrl=2, Alt=4, Win=8
  struct ToggleShortcut
  {
    unsigned key;
    int mods;

    // 半角/全角キー
    static constexpr unsigned Hankaku = 0x19;
    // バッククォートキー(`)
    static constexpr unsigned Grave = 0xC0;

    // JIS の半角/全角キーは、単独押しと Alt との組み合わせで通知されるキーコードが異なる場合がある。
    // 確認した入力は、単独押しで 0xF3 の解放と 0xF4 の押下、Alt との組み合わせで 0x19 の押下。
    // US の Alt+バッククォートも 0x19 になるため、入力の識別には NormalizeShortcutKey で配列情報も使う。
    static unsigned NormalizeHankakuKey(unsigned vk) { return vk == 0xF3 || vk == 0xF4 ? Hankaku : vk; }

    static unsigned NormalizeShortcutKey(unsigned vk, unsigned scan, bool extended, unsigned unmodifiedKey) {
      return scan == 0x29 && !extended && unmodifiedKey == Grave && NormalizeHankakuKey(vk) == Hankaku
          ? Grave : NormalizeHankakuKey(vk);
    }

    // 通常キー自身が修飾キー単独 (Shift/Ctrl/Alt/Win) かどうか
    static bool IsModifier(unsigned vk) { return vk == 0x10 || vk == 0x11 || vk == 0x12 ||
        (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C; }

    bool operator==(const ToggleShortcut &other) const { return key == other.key && mods == other.mods; }

    bool Matches(unsigned vk, int modifiers) const
    {
      return !IsModifier(key) &&                   // 登録されているキー自体が修飾キー単独でないこと
             mods == modifiers &&                  // 登録された修飾キーと、今押された修飾キー(modifiers)が一致
             NormalizeHankakuKey(vk) == NormalizeHankakuKey(key);
    }

    // Meltype の修飾キーフラグを TSF 用に変換する。
    // Meltype: Shift=1, Ctrl=2, Alt=4, Win=8
    // TSF (TF_MOD_*): Alt=1, Ctrl=2, Shift=4。Win は TSF では表現できない。
    unsigned TsfModifiers() const { return ((mods & 4) ? 1u : 0u) | ((mods & 2) ? 2u : 0u) | ((mods & 1) ? 4u : 0u); }
  };

  // TSF の ITfKeystrokeMgr::PreserveKey 用に登録するキーとフラグ
  struct PreservedShortcut
  {
    unsigned key;
    unsigned flags;
  };

  // 登録されたショートカット一覧から、TSF に横取り登録 (PreserveKey) すべきキー一覧を生成する。
  inline std::vector<PreservedShortcut> PreservedShortcutKeys(const std::vector<ToggleShortcut> &shortcuts, bool gravePosition = false)
  {
    std::vector<PreservedShortcut> result;
    auto add = [&](unsigned key, unsigned flags)
    {
      for (const auto &existing : result)
        if (existing.key == key && existing.flags == flags)
          return;
      result.push_back({key, flags});
    };
    for (const auto &b : shortcuts)
    {
      // TSF は Win を表す修飾フラグを持たない。Win を含む組み合わせは通常のイベント経路で処理する。
      if ((b.mods & 8) || ToggleShortcut::IsModifier(b.key))
        continue;
      auto key = [&](unsigned vk)
      {
        add(vk, b.TsfModifiers());
        // Alt を先に離した場合も、飲み込んだ主キーの解放は対にする。
        add(vk, 0x0200u | 0x0400u); // TF_MOD_ON_KEYUP | TF_MOD_IGNORE_ALL_MODIFIER
      };
      key(b.key);
      // JIS 半角/全角のコード変化と、US の Alt+バッククォートが 0x19 になる場合に対応する。
      // 登録通知を受けた後も、配列情報を含めて割り当てとの一致を確認する。
      if (ToggleShortcut::NormalizeHankakuKey(b.key) == ToggleShortcut::Hankaku || b.key == ToggleShortcut::Grave && gravePosition)
      {
        key(ToggleShortcut::Hankaku);
        key(0xF3);
        key(0xF4);
      }
    }
    return result;
  }
} // namespace meltype
