// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// ES モジュールで読むとき (npm・<script type="module">) の入り口。中身は meltype.js と同じ。
//   import { attach } from "meltype-web-engine";
//   await attach(document.querySelector("textarea"));

import "./meltype.js";

const { Meltype } = globalThis;
// 本体 (_framework/・mozc/) は、このファイルの隣に置いてある
Meltype.setBaseUrl(new URL(".", import.meta.url));

export const { load, loadCore, attach } = Meltype;
export default Meltype;
