// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

export interface LoadOptions {
  /** 本体 (_framework/・mozc/) を置いた場所。npm・<script> で読んだときは、そのファイルの隣を使うので要らない */
  baseUrl?: string | URL;
  /** 覚えたこと (変換の選び方など) を残すか。既定は true */
  persist?: boolean;
  /** 覚えたことの置き場。既定はそのページの IndexedDB */
  storage?: LearningStorage;
  /** 読み込みの進み具合。phase は "core" (Meltype)・"dictionary" (漢字変換の辞書。loaded・total はバイト数)・"ready" */
  onProgress?: (progress: { phase: "core" | "dictionary" | "ready"; loaded?: number; total?: number }) => void;
}

/** 覚えたことの置き場。中身は JSON の文字列 1 つ */
export interface LearningStorage {
  get(): Promise<string | null | undefined>;
  set(json: string): Promise<void>;
  /** ほかのタブなどが保存した (json)・消した (null) ときに呼んでもらう。無ければ、ほかのタブとは学習を合わせない */
  onChange?(listener: (json: string | null) => void): void;
}

/** Core (判定・変換・学習) だけ。loadCore が返す。create・handle は、ほかの場所で動かすときは Promise を返してよい */
export interface Core {
  readonly version: string;
  readonly mozc: boolean;
  /** 入力欄 1 つの本体を作り、番号を返す */
  create(): number | Promise<number>;
  /** 入力欄の本体を消す */
  destroy?(host: number): void;
  /** 出来事 (ImeEvent の JSON) を渡し、答え (ImeOutput の JSON) を返す */
  handle(host: number, json: string): string | Promise<string>;
  /** 変換の結果が後から届いたときに呼ぶ。返す関数で外す */
  onReady(listener: () => void): () => void;
  clearLearning(): Promise<void>;
}

export interface EngineOptions extends LoadOptions {
  /** ほかの場所 (拡張機能のページなど) で動かしている Core。渡すとこのページでは Core を読まない */
  core?: Core;
}

export interface AttachOptions extends EngineOptions {
  /** 画面のキーボードを出すか。"auto" (既定) は指で操作する端末だけ */
  keyboard?: "auto" | "always" | "never";
  /** 確定した文字を入力欄に入れたとき */
  onCommit?: (text: string) => void;
}

export interface Attachment {
  /** 外す。変換中の文字は確定する */
  detach(): void;
}

export interface Engine {
  /** Meltype の版 */
  readonly version: string;
  /** 漢字変換 (Mozc) を読めたか。false なら、かなのまま確定する */
  readonly mozc: boolean;
  /** <textarea>・<input>・contenteditable の要素に付ける */
  attach(element: HTMLElement, options?: AttachOptions): Attachment;
  /** 覚えたことを消す */
  clearLearning(): Promise<void>;
}

/** 本体を読み込む。2 回目からは同じものを返す */
export function load(options?: EngineOptions): Promise<Engine>;

/** Core だけを読み込む (入力欄の画面は持たない) */
export function loadCore(options?: LoadOptions): Promise<Core>;

/** 入力欄に付ける。本体をまだ読んでいなければ読む */
export function attach(element: HTMLElement, options?: AttachOptions): Promise<Attachment>;

declare const Meltype: { load: typeof load; loadCore: typeof loadCore; attach: typeof attach };
export default Meltype;

declare global {
  /** <script src="meltype.js"> で読んだとき */
  var Meltype: { load: typeof load; loadCore: typeof loadCore; attach: typeof attach };
}
