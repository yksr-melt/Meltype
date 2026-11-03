/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

export const LIMITS = {
  maxFiles: 3,
  maxFileBytes: 10 * 1024 * 1024,
  maxTotalBytes: 20 * 1024 * 1024,
  text: 200,
  textarea: 5000,
  code: 20000,
  contact: 100,
  fileName: 200,
};

export const FILE_TYPES = {
  png: { mime: "image/png", group: "image" },
  jpg: { mime: "image/jpeg", group: "image" },
  jpeg: { mime: "image/jpeg", group: "image" },
  webp: { mime: "image/webp", group: "image" },
  gif: { mime: "image/gif", group: "image" },
  mp4: { mime: "video/mp4", group: "video" },
  webm: { mime: "video/webm", group: "video" },
  log: { mime: "text/plain; charset=utf-8", group: "text" },
  txt: { mime: "text/plain; charset=utf-8", group: "text" },
  json: { mime: "application/json; charset=utf-8", group: "json" },
};

export const FILE_ACCEPT = Object.keys(FILE_TYPES).map((ext) => "." + ext).join(",");

export function fileTypeOf(name) {
  const match = /\.([a-z0-9]+)$/i.exec(String(name || ""));
  return match ? FILE_TYPES[match[1].toLowerCase()] || null : null;
}

const OS_DETAILED = ["Windows 11", "Windows 10", "Mac (プレビュー版)", "Linux (プレビュー版)"];
const OS_SIMPLE = ["Windows", "Mac (プレビュー版)", "Linux (プレビュー版)"];

const CONTACT = {
  id: "contact",
  label: "連絡先",
  type: "text",
  placeholder: "GitHub: @name / Discord: name",
  hint: "詳しく聞くために連絡してもよい場合だけ書いてください。メールアドレスでなくてもかまいません",
  max: LIMITS.contact,
};

const ENVIRONMENT = {
  id: "environment",
  label: "実行環境",
  type: "code",
  hint: "トレイのアイコンを右クリック →「不具合の報告・提案...」の「実行環境」をそのまま貼ってください",
};

export const PRIVATE_FIELDS = ["contact", "log", "environment"];

export const FORMS = [
  {
    id: "bug",
    label: "不具合",
    summary: "動かない・止まる・落ちる・表示がおかしい",
    template: "1-bug.yml",
    prefix: "不具合",
    note: "日本語・英語の判定や変換の間違い (「日本語」が「にほんgo」になった など) は「変換・判定の間違い」から送ってください。",
    required: [
      { id: "os", label: "OS", type: "select", options: OS_DETAILED },
      { id: "version", label: "Meltype の版", type: "text", placeholder: "1.2.1", hint: "Windows: トレイのアイコンを右クリック →「Meltype について...」" },
      { id: "app", label: "どのアプリで", type: "text", placeholder: "Discord / Chrome の検索欄 / VS Code / メモ帳" },
      { id: "steps", label: "何をしたら", type: "textarea", placeholder: "1. Discord のメッセージ欄をクリック\n2. kyouha と打って Space", hint: "できるだけ順番に。打ったキーはそのまま書いてください" },
      { id: "actual", label: "どうなったか", type: "textarea", hint: "画面の様子は、スクリーンショットや録画にして下の「添付」に入れてもらえると助かります" },
    ],
    optional: [
      { id: "mode", label: "使っていたモード (Windows)", type: "select", options: ["Meltype IME (入力欄に直接入力)", "Meltype キーボード (変換ボックスで入力)", "分からない / Mac・Linux"] },
      { id: "os-detail", label: "OS の詳細", type: "text", placeholder: "Ubuntu 24.04 / GNOME / Wayland", hint: "Mac: macOS 15.2 / M2 など。Linux: Ubuntu 24.04 / GNOME / Wayland など" },
      { id: "expected", label: "どうなってほしかったか", type: "textarea" },
      { id: "frequency", label: "起きる頻度", type: "select", options: ["毎回", "ときどき", "一度だけ"] },
      { id: "log", label: "ログ", type: "code", hint: "トレイのアイコンを右クリック →「ログ / 判定理由...」→「コピー」。Windows の更新の不具合は %LOCALAPPDATA%\\Meltype\\update\\install.log の中身。見られて困る部分は消してから貼ってください" },
      ENVIRONMENT,
      CONTACT,
    ],
  },
  {
    id: "misdetection",
    label: "変換・判定の間違い",
    summary: "日本語のつもりが英字に・英語のつもりがかなに・変換の候補がおかしい",
    template: "2-misdetection.yml",
    prefix: "誤判定",
    note: "「打ったもの」と「期待した結果」があれば、そのままテストに入れて直せます。パスワードや個人的な文章は、似た例に書き換えてもらって大丈夫です。",
    required: [
      { id: "kind", label: "種類", type: "select", options: ["日本語のつもりが英字になった (にほんgo)", "英語のつもりがかなになった (みいてぃんg)", "漢字の変換が違う (曲 → 癖)", "英字の大文字・小文字・綴りが違う", "打ち間違いの直し方が違う", "そのほか"] },
      { id: "typed", label: "打ったもの (キーをそのまま)", type: "text", placeholder: "nihongowohanasu" },
      { id: "actual", label: "出たもの", type: "text", placeholder: "にほんgoを話す" },
      { id: "expected", label: "期待した結果", type: "text", placeholder: "日本語を話す" },
      { id: "zenzai", label: "Zenzai (zenz) を使っているか", type: "select", options: ["使っていない", "使っている", "分からない"], hint: "設定の「Zenzai を使う」です。ON だと変換の結果が変わります" },
    ],
    optional: [
      { id: "key", label: "最後に押したキー", type: "select", options: ["Space (変換)", "Enter (確定)", "打っている途中 (まだ確定していない)", "句読点・記号", "そのほか"] },
      { id: "context", label: "前後の文", type: "textarea", placeholder: "前の行は「明日の会議の資料」", hint: "前の文によって判定が変わることがあります (英文の途中か、日本語の文の途中か)" },
      { id: "app", label: "どのアプリで", type: "text", placeholder: "VS Code のエディター / Slack", hint: "コードエディター・チャット・検索欄などで判定の仕方が変わります" },
      { id: "os", label: "OS", type: "select", options: OS_SIMPLE },
      { id: "version", label: "Meltype の版", type: "text", placeholder: "1.2.1" },
      ENVIRONMENT,
      CONTACT,
    ],
  },
  {
    id: "dictionary",
    label: "辞書に足してほしい語",
    summary: "出ない変換候補・英字にしてほしい固有名詞・同音異義語",
    template: "3-dictionary.yml",
    prefix: "辞書",
    note: "自分で辞書 (dictionaries/) を直して Pull Request を送ってもらうこともできます。",
    required: [
      { id: "kind", label: "種類", type: "select", options: ["カタカナで打ったときに英字の候補を出してほしい (しゃおみ → Xiaomi)", "ローマ字として読めても英字のままにしてほしい語 (固有名詞・英単語)", "変換の候補に出してほしい語 (同音異義語・新語)", "そのほか"] },
      { id: "words", label: "語", type: "textarea", placeholder: "しゃおみ → Xiaomi\nふぁーうぇい → Huawei", hint: "1 行に 1 語。「読み → 出してほしい語」の形で書いてください" },
    ],
    optional: [
      { id: "note", label: "補足", type: "textarea", hint: "どんなときに使う語か など" },
      CONTACT,
    ],
  },
  {
    id: "idea",
    label: "改善・機能の提案",
    summary: "こうなったら使いやすい、こんな機能がほしい",
    template: "4-idea.yml",
    prefix: "提案",
    note: "今は不具合の修正と判定・変換の精度を優先しているため、すぐには入れられないことがあります。",
    required: [
      { id: "problem", label: "困っていること", type: "textarea", placeholder: "Discord で絵文字を打つとき、毎回 : を打つのが面倒", hint: "どんな場面で、何が不便か" },
      { id: "idea", label: "こうなってほしい", type: "textarea" },
    ],
    optional: [
      { id: "examples", label: "具体例", type: "textarea", placeholder: "Python ファイルで nihongo → 日本語", hint: "入力の例と、期待する動き" },
      { id: "os", label: "対象の OS", type: "checks", options: ["Windows", "Mac", "Linux"] },
      CONTACT,
    ],
  },
];

export function formById(id) {
  return FORMS.find((form) => form.id === id) || null;
}

export function formByTemplate(template) {
  const name = String(template || "").toLowerCase();
  if (name.startsWith("5-")) return formById("idea");
  return FORMS.find((form) => form.template === name) || null;
}

export function maxLength(field) {
  return field.max || LIMITS[field.type === "select" || field.type === "checks" ? "text" : field.type] || LIMITS.text;
}

function cleanValue(field, raw) {
  if (field.type === "checks") {
    const list = Array.isArray(raw) ? raw : raw == null || raw === "" ? [] : [raw];
    return field.options.filter((option) => list.includes(option));
  }
  if (raw == null) return "";
  const text = String(raw).replace(/\r\n?/g, "\n");
  return field.type === "code" ? text.replace(/\s+$/, "") : text.trim();
}

function isEmpty(value) {
  return Array.isArray(value) ? value.length === 0 : value === "";
}

export function validateFields(form, input) {
  const errors = {};
  const fields = {};
  const source = input && typeof input === "object" ? input : {};
  for (const [field, required] of [...form.required.map((f) => [f, true]), ...form.optional.map((f) => [f, false])]) {
    const value = cleanValue(field, source[field.id]);
    if (isEmpty(value)) {
      if (required) errors[field.id] = field.type === "select" ? "選んでください" : "書いてください";
      continue;
    }
    if (field.type === "select" && !field.options.includes(value)) {
      errors[field.id] = "選択肢から選んでください";
      continue;
    }
    if (typeof value === "string" && value.length > maxLength(field)) {
      errors[field.id] = `${maxLength(field).toLocaleString("ja-JP")} 文字までです`;
      continue;
    }
    fields[field.id] = value;
  }
  return { errors, fields };
}

export function validateFiles(files) {
  const list = Array.isArray(files) ? files : [];
  if (list.length > LIMITS.maxFiles) return { error: `添付は ${LIMITS.maxFiles} ファイルまでです` };
  let total = 0;
  const clean = [];
  for (const file of list) {
    const name = String(file && file.name || "").replace(/[\u0000-\u001f\u007f\\/]/g, "_").slice(0, LIMITS.fileName);
    const size = Number(file && file.size);
    const type = fileTypeOf(name);
    if (!name || !type) return { error: `${name || "名前のないファイル"} は添付できない種類です` };
    if (!Number.isSafeInteger(size) || size <= 0) return { error: `${name} は空のファイルです` };
    if (size > LIMITS.maxFileBytes) return { error: `${name} は大きすぎます (1 ファイル ${formatBytes(LIMITS.maxFileBytes)} まで)` };
    total += size;
    clean.push({ name, size, mime: type.mime, group: type.group });
  }
  if (total > LIMITS.maxTotalBytes) return { error: `添付は合計 ${formatBytes(LIMITS.maxTotalBytes)} までです` };
  return { files: clean };
}

function firstLine(text, length = 60) {
  const line = String(text || "").split("\n").find((l) => l.trim()) || "";
  const trimmed = line.trim();
  return trimmed.length > length ? trimmed.slice(0, length - 1) + "…" : trimmed;
}

export function titleFor(form, fields) {
  switch (form.id) {
    case "bug":
      return `[${form.prefix}] ${firstLine(fields.app, 30)}: ${firstLine(fields.actual)}`;
    case "misdetection":
      return `[${form.prefix}] ${firstLine(fields.typed, 40)} → ${firstLine(fields.actual, 30)} (期待: ${firstLine(fields.expected, 30)})`;
    case "dictionary": {
      const lines = String(fields.words || "").split("\n").filter((l) => l.trim());
      return `[${form.prefix}] ${firstLine(lines[0])}${lines.length > 1 ? ` ほか ${lines.length - 1} 語` : ""}`;
    }
    default:
      return `[${form.prefix}] ${firstLine(fields.problem)}`;
  }
}

export function privateLabels(form, fields) {
  return [...form.required, ...form.optional]
    .filter((field) => PRIVATE_FIELDS.includes(field.id) && fields[field.id] != null && !isEmpty(fields[field.id]))
    .map((field) => field.label);
}

export function toMarkdown(form, fields) {
  const parts = [];
  for (const field of [...form.required, ...form.optional]) {
    if (PRIVATE_FIELDS.includes(field.id)) continue;
    const value = fields[field.id];
    if (value == null || isEmpty(value)) continue;
    const body = Array.isArray(value)
      ? value.join(", ")
      : field.type === "code"
        ? "```text\n" + value + "\n```"
        : value;
    parts.push(`### ${field.label}\n\n${body}`);
  }
  return parts.join("\n\n");
}

export function formatBytes(bytes) {
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(bytes >= 10 * 1024 * 1024 ? 0 : 1)} MB`;
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${bytes} B`;
}
