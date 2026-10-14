// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 再現結果に対するbotの判定と報告が、期待する結果・報告された事象を取り違えないことを確認する。
 * 検証範囲: report処理がlocalhostの偽APIへ送るコメントとラベル要求。
 * 制約: 再現結果は固定データで与え、変換エンジン自体の正しさは検査しない。
 * 実際のGitHubへの書込みは行わない。
 */

import { test } from '@playwright/test';
import type { BotReportCase } from '../../src/schemas/bot-report';
import { verifyBotReport } from '../../src/runners/bot-report';

{
  // Issue #648 / PR #649: 英字が同じでもかなが異なる。localhostの偽APIで検査する。
  const item = {
    "id": "B01",
    "operation": {
      "committed": "abcあ",
      "composing": "",
      "converted": null,
      "expected": "abcい",
      "reported": "other"
    },
    "expected": {
      "verdict": "different"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 英字が同じでもかなが異なる`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 余分な末尾を一致にしない。localhostの偽APIで検査する。
  const item = {
    "id": "B02",
    "operation": {
      "committed": "abcあ追加",
      "composing": "",
      "converted": null,
      "expected": "abcあ",
      "reported": "other"
    },
    "expected": {
      "verdict": "different"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 余分な末尾を一致にしない`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 全角と半角を一致にしない。localhostの偽APIで検査する。
  const item = {
    "id": "B03",
    "operation": {
      "committed": "Ａ",
      "composing": "",
      "converted": null,
      "expected": "A",
      "reported": "other"
    },
    "expected": {
      "verdict": "different"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 全角と半角を一致にしない`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 確定と未確定を合わせて比較する。localhostの偽APIで検査する。
  const item = {
    "id": "B04",
    "operation": {
      "committed": "abc",
      "composing": "あ",
      "converted": null,
      "expected": "abcあ",
      "reported": "other"
    },
    "expected": {
      "verdict": "match"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 確定と未確定を合わせて比較する`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 変換結果を比較する。localhostの偽APIで検査する。
  const item = {
    "id": "B05",
    "operation": {
      "committed": "にほんご",
      "composing": null,
      "converted": "日本語",
      "expected": "日本語",
      "reported": "other"
    },
    "expected": {
      "verdict": "match"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 変換結果を比較する`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 報告と同じ結果を再現と判定する。localhostの偽APIで検査する。
  const item = {
    "id": "B06",
    "operation": {
      "committed": "報告と同じ",
      "composing": null,
      "converted": null,
      "expected": "修正後",
      "reported": "報告と同じ"
    },
    "expected": {
      "verdict": "reproduced"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 報告と同じ結果を再現と判定する`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}

{
  // Issue #648 / PR #649: 期待とも報告とも異なる結果を区別する。localhostの偽APIで検査する。
  const item = {
    "id": "B07",
    "operation": {
      "committed": "別の結果",
      "composing": null,
      "converted": null,
      "expected": "修正後",
      "reported": "報告と同じ"
    },
    "expected": {
      "verdict": "different"
    }
  } as const satisfies BotReportCase;
  test(`[${item.id}] 期待とも報告とも異なる結果を区別する`, async ({}, info) => {
    await verifyBotReport(item, info);
  });
}
