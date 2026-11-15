// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Settings page content and actions. Java only renders these nodes and forwards
//! field values; all page-specific control requests are constructed here.
use super::*;
use serde_json::{json, Value};

type Result<T> = std::result::Result<T, String>;

pub(super) fn request(handle: jlong, value: Value) -> Result<Value> {
    let input = CString::new(value.to_string()).map_err(|e| e.to_string())?;
    let output = unsafe { take(meltype_control(handle as *mut c_void, input.as_ptr())) }
        .ok_or("設定を読み込めません")?;
    let value: Value = serde_json::from_str(&output).map_err(|e| e.to_string())?;
    if let Some(error) = value.get("error") {
        return Err(error.as_str().unwrap_or("設定の操作に失敗しました").into());
    }
    Ok(value)
}
fn label(nodes: &mut Vec<Value>, text: impl Into<String>) {
    nodes.push(json!({"type":"label","text":text.into()}));
}
fn field(nodes: &mut Vec<Value>, id: &str, hint: &str, value: &str, lines: i32) {
    nodes.push(json!({"type":"field","id":id,"hint":hint,"value":value,"lines":lines}));
}
fn button(nodes: &mut Vec<Value>, text: &str, action: Value) {
    nodes.push(json!({"type":"button","text":text,"action":action}));
}
const TOGGLES: &[(&str, &str)] = &[
    ("Enabled", "入力を有効にする"),
    ("LearningEnabled", "入力から学習する"),
    ("PredictiveCandidates", "予測候補"),
    ("TranslationCandidates", "英訳候補"),
    ("ShowCandidateMeanings", "候補の意味"),
    ("ShowTypedKeys", "入力したキーを表示"),
    ("CorrectTypos", "書き間違いの提案"),
    ("AutoCorrectAfterCommit", "次の語に合わせて確定後に訂正"),
    ("SpaceAroundEnglish", "英語の前後に空白"),
    ("SlashAsMiddleDot", "スラッシュを中点にする"),
    ("TabConversion", "Tabで変換"),
    ("ContinueAfterConversion", "変換後も入力を続ける"),
    ("SeparateKeyRoles", "変換キーの役割を分ける"),
];
fn ai_installed(handle: jlong, id: &str) -> bool {
    let Some(model) = meltype_core::ai_catalog::find(id).filter(|m| m.family == "Shunti") else {
        return false;
    };
    sessions().lock().ok().is_some_and(|sessions| {
        sessions
            .get(&(handle as usize))
            .and_then(|s| s.data_directory())
            .is_some_and(|root| {
                let directory = root.join("ai").join(format!("{}-data", model.directory));
                let receipt = std::fs::read(directory.join("receipt.json"))
                    .ok()
                    .and_then(|bytes| serde_json::from_slice::<Value>(&bytes).ok());
                receipt.is_some_and(|r| {
                    model.files.iter().all(|f| {
                        r["files"][&f.name] == f.sha256
                            && directory
                                .join(&f.name)
                                .metadata()
                                .is_ok_and(|m| m.len() == f.size)
                    })
                })
            })
    })
}
pub(super) fn page(handle: jlong, page: jint) -> Result<Value> {
    let mut nodes = Vec::new();
    match page {
        0 => {
            let settings = request(handle, json!({"op":"settings_get"}))?;
            let selected = settings["AiModel"] == "Shunti"
                || settings["AiModel"].is_null() && settings["UseShunti"] == true;
            label(&mut nodes, "AIモデル");
            nodes.push(json!({"type":"select","text":"AIモデル","choices":["使用しない","shunti"],"selected":i32::from(selected),"action":{"action":"ai_select"},"enabled":!ai_ui::busy(handle)}));
            if selected {
                label(
                    &mut nodes,
                    "shuntiの辞書・モデルは任意ダウンロードです。パス入力は不要です。",
                );
                let variants: Vec<_> = meltype_core::ai_catalog::models()
                    .iter()
                    .filter(|m| m.family == "Shunti")
                    .collect();
                let id = settings["ShuntiModelVariant"]
                    .as_str()
                    .unwrap_or("shunti-v7-s8p");
                nodes.push(json!({"type":"select","text":"バージョン・モデルサイズ","choices":variants.iter().map(|m| &m.label).collect::<Vec<_>>(),"selected":variants.iter().position(|m| m.id == id).unwrap_or(0),"action":{"action":"ai_variant"},"enabled":!ai_ui::busy(handle)}));
                let ready = ai_installed(handle, id);
                nodes.push(json!({"type":"button","text":if ready {"インストール済み"} else {"このモデルをインストール"},"action":{"action":"ai_install"},"enabled":!ready && !ai_ui::busy(handle)}));
                label(
                    &mut nodes,
                    if ready {
                        "インストール済み"
                    } else {
                        "未導入。インストール後に有効になります。"
                    },
                );
            }
            for (name, text) in TOGGLES {
                nodes.push(json!({"type":"toggle","text":text,"checked":settings[name].as_bool().unwrap_or(false),"action":{"action":"toggle","name":name}}));
            }
            label(
                &mut nodes,
                "利用状況は、アプリの版・OSの版・起動回数だけを匿名で送ります。入力内容・入力履歴・辞書・設定値は送りません。利用状況の送信は停止できません。",
            );
            label(
                &mut nodes,
                "ソフトキーボードはローマ字入力です。JISかな入力は外付けキーボードで使用できます。",
            );
            label(&mut nodes, "通貨の換算");
            // Android はネットからレートを取らないので、固定と手入力の 2 つ
            let modes = ["Fixed", "Manual"];
            let mode = settings["CurrencyRateMode"].as_str().unwrap_or("Fixed");
            nodes.push(json!({
                "type":"select",
                "text":"通貨のレート",
                "choices":["固定（同梱のレート）","手入力"],
                "selected":modes.iter().position(|m| *m == mode).unwrap_or(0),
                "action":{"action":"currency_mode"},
                "enabled":true,
            }));
            let digits = settings["CurrencyDecimals"]
                .as_i64()
                .unwrap_or(1)
                .clamp(0, 4);
            nodes.push(json!({
                "type":"select",
                "text":"換算した額の小数の桁",
                "choices":["0","1","2","3","4"],
                "selected":digits as i32,
                "action":{"action":"currency_digits"},
                "enabled":true,
            }));
            field(
                &mut nodes,
                "currency",
                "手入力のレート（1単位あたりの円）",
                settings["CurrencyManualRates"].as_str().unwrap_or(""),
                2,
            );
            button(
                &mut nodes,
                "レートを保存",
                json!({"action":"currency_save"}),
            );
            field(
                &mut nodes,
                "mark",
                "定型文の開始記号",
                settings["SnippetMark"].as_str().unwrap_or(""),
                1,
            );
            button(&mut nodes, "開始記号を保存", json!({"action":"mark"}));
        }
        1 => {
            field(&mut nodes, "reading", "読み（ひらがな）", "", 1);
            field(&mut nodes, "word", "単語", "", 1);
            button(&mut nodes, "登録", json!({"action":"dictionary_add"}));
            button(
                &mut nodes,
                "辞書を取り込む",
                json!({"action":"pick","operation":"dictionary_import"}),
            );
            button(
                &mut nodes,
                "辞書を書き出す",
                json!({"action":"pick","operation":"dictionary_export"}),
            );
            let entries = request(handle, json!({"op":"dictionary_list"}))?;
            let entries = entries.as_object().ok_or("辞書の形式が正しくありません")?;
            for (reading, words) in entries {
                for word in words.as_array().ok_or("辞書の形式が正しくありません")? {
                    let word = word.as_str().ok_or("辞書の形式が正しくありません")?;
                    label(&mut nodes, format!("{reading} → {word}"));
                    button(
                        &mut nodes,
                        "この単語を削除",
                        json!({"action":"dictionary_remove","reading":reading,"word":word}),
                    );
                }
            }
        }
        2 => {
            field(&mut nodes, "name", "呼び出し名", "", 1);
            field(&mut nodes, "text", "展開する文字列", "", 2);
            button(&mut nodes, "保存", json!({"action":"snippet_save"}));
            let entries = request(handle, json!({"op":"snippets_list"}))?;
            for (name, text) in entries
                .as_object()
                .ok_or("定型文の形式が正しくありません")?
            {
                let text = text.as_str().ok_or("定型文の形式が正しくありません")?;
                label(&mut nodes, format!("{name} → {text}"));
                button(
                    &mut nodes,
                    "編集",
                    json!({"action":"snippet_edit","name":name,"text":text}),
                );
                button(
                    &mut nodes,
                    "削除",
                    json!({"action":"snippet_remove","name":name}),
                );
            }
        }
        3 => {
            let learned = request(handle, json!({"op":"learning_list"}))?;
            label(&mut nodes, "覚えた入力言語");
            for (name, entry) in learned["languages"]
                .as_object()
                .ok_or("学習の形式が正しくありません")?
            {
                let language = if entry["english"].as_bool().unwrap_or(false) {
                    "英語"
                } else {
                    "日本語"
                };
                let pending = if entry["active"].as_bool().unwrap_or(true) {
                    ""
                } else {
                    "（もう一度英語で確定すると有効）"
                };
                label(&mut nodes, format!("{name} → {language}{pending}"));
            }
            label(&mut nodes, "覚えた変換");
            for (name, text) in learned["conversions"]
                .as_object()
                .ok_or("学習の形式が正しくありません")?
            {
                label(
                    &mut nodes,
                    format!(
                        "{name} → {}",
                        text.as_str().ok_or("学習の形式が正しくありません")?
                    ),
                );
            }
            label(&mut nodes, "覚えたフレーズ");
            for phrase in learned["phrases"]
                .as_array()
                .ok_or("学習の形式が正しくありません")?
            {
                label(
                    &mut nodes,
                    format!(
                        "{} → {}",
                        phrase["reading"]
                            .as_str()
                            .ok_or("学習の形式が正しくありません")?,
                        phrase["text"]
                            .as_str()
                            .ok_or("学習の形式が正しくありません")?
                    ),
                );
            }
            field(&mut nodes, "key", "削除する学習キー", "", 1);
            button(
                &mut nodes,
                "指定した学習を削除",
                json!({"action":"learning_remove"}),
            );
            button(
                &mut nodes,
                "すべての学習を削除",
                json!({"action":"learning_confirm"}),
            );
        }
        4 => {
            let settings = request(handle, json!({"op":"settings_get"}))?;
            label(
                &mut nodes,
                format!(
                    "使用中: {}",
                    settings["ActiveProfile"].as_str().unwrap_or("")
                ),
            );
            field(&mut nodes, "name", "プロファイル名", "", 1);
            for (text, action) in [
                ("新規作成", "profile_add"),
                ("切り替え", "profile_switch"),
                ("削除", "profile_delete"),
            ] {
                button(&mut nodes, text, json!({"action":action}));
            }
            for profile in settings["Profiles"]
                .as_array()
                .ok_or("プロファイルの形式が正しくありません")?
            {
                let name = profile["Name"]
                    .as_str()
                    .ok_or("プロファイルの形式が正しくありません")?;
                button(
                    &mut nodes,
                    name,
                    json!({"action":"profile_select","name":name}),
                );
            }
        }
        5 => {
            button(
                &mut nodes,
                "バックアップを保存",
                json!({"action":"pick","operation":"backup_create"}),
            );
            button(
                &mut nodes,
                "バックアップを検査・復元",
                json!({"action":"pick","operation":"backup_restore"}),
            );
            let diagnostics = request(handle, json!({"op":"diagnostics"}))?;
            label(
                &mut nodes,
                serde_json::to_string_pretty(&diagnostics).map_err(|e| e.to_string())?,
            );
        }
        _ => return Err("設定ページが見つかりません".into()),
    }
    Ok(json!({"nodes":nodes}))
}
fn string<'a>(value: &'a Value, key: &str) -> Result<&'a str> {
    value
        .get(key)
        .and_then(Value::as_str)
        .ok_or_else(|| format!("入力項目がありません: {key}"))
}
pub(super) fn action(handle: jlong, action: Value, fields: Value) -> Result<Value> {
    let kind = string(&action, "action")?;
    let (request, page) = match kind {
        "ai_select" => {
            if ai_ui::busy(handle) {
                return Ok(json!({}));
            }
            let model = match action["value"].as_i64() {
                Some(0) => "None",
                Some(1) => "Shunti",
                _ => return Err("AIモデルが正しくありません".into()),
            };
            let current = request(handle, json!({"op":"settings_get"}))?;
            if current["AiModel"] == model {
                return Ok(json!({}));
            }
            request(
                handle,
                json!({"op":"settings_set","name":"UseShunti","value":false}),
            )?;
            request(
                handle,
                json!({"op":"settings_set","name":"AiModel","value":model}),
            )?;
            if model == "Shunti" {
                let id = current["ShuntiModelVariant"]
                    .as_str()
                    .unwrap_or("shunti-v7-s8p");
                let ready = ai_installed(handle, id);
                // The runtime verifies the fixed hashes again before inference.
                request(
                    handle,
                    json!({"op":"settings_set","name":"UseShunti","value":ready}),
                )?;
            }
            return Ok(json!({"page":0}));
        }
        "ai_variant" => {
            if ai_ui::busy(handle) {
                return Ok(json!({}));
            }
            let variants: Vec<_> = meltype_core::ai_catalog::models()
                .iter()
                .filter(|m| m.family == "Shunti")
                .collect();
            let model = action["value"]
                .as_u64()
                .and_then(|i| variants.get(i as usize))
                .ok_or("AIモデルが正しくありません")?;
            let current = request(handle, json!({"op":"settings_get"}))?;
            if current["ShuntiModelVariant"]
                .as_str()
                .unwrap_or("shunti-v7-s8p")
                == model.id
            {
                return Ok(json!({}));
            }
            request(
                handle,
                json!({"op":"settings_set","name":"ShuntiModelVariant","value":model.id}),
            )?;
            request(
                handle,
                json!({"op":"settings_set","name":"UseShunti","value":ai_installed(handle,&model.id)}),
            )?;
            return Ok(json!({"page":0}));
        }
        "ai_install" => {
            if ai_ui::busy(handle) {
                let current = request(handle, json!({"op":"settings_get"}))?;
                if ai_installed(
                    handle,
                    current["ShuntiModelVariant"]
                        .as_str()
                        .unwrap_or("shunti-v7-s8p"),
                ) {
                    return Ok(json!({}));
                }
                return Ok(json!({}));
            }
            return Ok(
                json!({"confirm":{"title":"shuntiの環境を作成してよいですか？","message":"shuntiの辞書（CC BY-SA 4.0）・モデル（CC BY 4.0）を別ダウンロードします。入力内容は送信しません。","positive":"はい、作成する","action":{"action":"ai_install"}}}),
            );
        }
        "toggle" => {
            let name = string(&action, "name")?;
            if !TOGGLES.iter().any(|(allowed, _)| name == *allowed) {
                return Err("設定項目が見つかりません".into());
            }
            let value = fields["checked"]
                .as_bool()
                .ok_or("設定値が正しくありません")?;
            (json!({"op":"settings_set","name":name,"value":value}), None)
        }
        "mark" => (
            json!({"op":"settings_set","name":"SnippetMark","value":string(&fields,"mark")?}),
            None,
        ),
        "currency_mode" => {
            let mode = match action["value"].as_i64() {
                Some(0) => "Fixed",
                Some(1) => "Manual",
                _ => return Err("通貨のレートが正しくありません".into()),
            };
            // 画面を作り直すと、選択の通知 (onItemSelected) で同じ操作が続く。
            // 表示は選択の部品が自分で変わるので、作り直さずに値だけ保存する。
            (
                json!({"op":"settings_set","name":"CurrencyRateMode","value":mode}),
                None,
            )
        }
        "currency_digits" => {
            let digits = action["value"]
                .as_i64()
                .filter(|digits| {
                    (0..=i64::from(meltype_core::currency::MAX_DECIMALS)).contains(digits)
                })
                .ok_or("小数の桁が正しくありません")?;
            (
                json!({"op":"settings_set","name":"CurrencyDecimals","value":digits}),
                None,
            )
        }
        "currency_save" => (
            json!({"op":"settings_set","name":"CurrencyManualRates","value":string(&fields,"currency")?}),
            Some(0),
        ),
        "dictionary_add" => (
            json!({"op":"dictionary_set","reading":string(&fields,"reading")?,"word":string(&fields,"word")?}),
            Some(1),
        ),
        "dictionary_remove" => (
            json!({"op":"dictionary_remove","reading":string(&action,"reading")?,"word":string(&action,"word")?}),
            Some(1),
        ),
        "snippet_save" => (
            json!({"op":"snippet_set","name":string(&fields,"name")?,"text":string(&fields,"text")?}),
            Some(2),
        ),
        "snippet_remove" => (
            json!({"op":"snippet_remove","name":string(&action,"name")?}),
            Some(2),
        ),
        "snippet_edit" => {
            return Ok(
                json!({"edit":{"name":string(&action,"name")?,"text":string(&action,"text")?}}),
            )
        }
        "learning_remove" => (
            json!({"op":"learning_remove","key":string(&fields,"key")?}),
            Some(3),
        ),
        "learning_confirm" => {
            return Ok(
                json!({"confirm":{"message":"学習した内容をすべて削除しますか？","positive":"削除","action":{"action":"learning_clear"}}}),
            )
        }
        "learning_clear" => (json!({"op":"learning_clear"}), Some(3)),
        "profile_add" | "profile_switch" | "profile_delete" => {
            (json!({"op":kind,"name":string(&fields,"name")?}), Some(4))
        }
        "profile_select" => return Ok(json!({"edit":{"name":string(&action,"name")?}})),
        "pick" => {
            let operation = string(&action, "operation")?;
            let (output, _) = super::documents::specification(operation)?;
            return Ok(json!({"pick":{"operation":operation,"output":output}}));
        }
        _ => return Err("設定操作が見つかりません".into()),
    };
    request_control(handle, request, page)
}
fn request_control(handle: jlong, input: Value, page: Option<i32>) -> Result<Value> {
    request(handle, input)?;
    Ok(match page {
        Some(page) => json!({"page":page}),
        None => json!({}),
    })
}
pub(super) fn reply(env: &mut JNIEnv, result: Result<Value>) -> jstring {
    let value = result.unwrap_or_else(|error| json!({"error":error}));
    env.new_string(value.to_string())
        .map_or(std::ptr::null_mut(), JString::into_raw)
}
#[no_mangle]
pub extern "system" fn Java_moe_meltype_rust_Native_settingsPage(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
    number: jint,
) -> jstring {
    let result = std::panic::catch_unwind(|| page(handle, number))
        .unwrap_or_else(|_| Err("設定画面の生成に失敗しました".into()));
    reply(&mut env, result)
}
#[no_mangle]
pub extern "system" fn Java_moe_meltype_rust_Native_settingsAction(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
    input: JString,
    fields: JString,
) -> jstring {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let input: String = env.get_string(&input).map_err(|e| e.to_string())?.into();
        let fields: String = env.get_string(&fields).map_err(|e| e.to_string())?.into();
        action(
            handle,
            serde_json::from_str(&input).map_err(|e| e.to_string())?,
            serde_json::from_str(&fields).map_err(|e| e.to_string())?,
        )
    }))
    .unwrap_or_else(|_| Err("設定操作に失敗しました".into()));
    reply(&mut env, result)
}

#[cfg(test)]
mod tests {
    use super::*;
    struct Profile {
        handle: jlong,
        directory: tempfile::TempDir,
    }
    impl Profile {
        fn new() -> Self {
            let directory = tempfile::tempdir().unwrap();
            let path = CString::new(directory.path().to_str().unwrap()).unwrap();
            let handle = unsafe { meltype_create_at(path.as_ptr()) as jlong };
            assert_ne!(handle, 0);
            Self { handle, directory }
        }
        fn act(&self, input: Value, fields: Value) -> Result<Value> {
            action(self.handle, input, fields)
        }
    }
    impl Drop for Profile {
        fn drop(&mut self) {
            meltype_destroy(self.handle as *mut c_void);
        }
    }
    #[test]
    fn settings_pages_actions_and_storage_roundtrip() {
        let profile = Profile::new();
        let h = profile.handle;
        let original = request(h, json!({"op":"settings_get"})).unwrap()["ActiveProfile"].clone();
        for number in 0..6 {
            assert!(!page(h, number).unwrap()["nodes"]
                .as_array()
                .unwrap()
                .is_empty());
        }
        let settings = page(h, 0).unwrap();
        assert_eq!(
            settings["nodes"]
                .as_array()
                .unwrap()
                .iter()
                .filter(|n| n["type"] == "toggle")
                .count(),
            13
        );
        profile
            .act(
                json!({"action":"toggle","name":"PredictiveCandidates"}),
                json!({"checked":false}),
            )
            .unwrap();
        assert_eq!(
            request(h, json!({"op":"settings_get"})).unwrap()["PredictiveCandidates"],
            false
        );
        profile
            .act(json!({"action":"mark"}), json!({"mark":";"}))
            .unwrap();
        assert_eq!(
            request(h, json!({"op":"settings_get"})).unwrap()["SnippetMark"],
            ";"
        );
        assert_eq!(
            profile
                .act(
                    json!({"action":"dictionary_add"}),
                    json!({"reading":"めるたいぷ","word":"Meltype😀"})
                )
                .unwrap()["page"],
            1
        );
        let dictionary = page(h, 1).unwrap().to_string();
        assert!(dictionary.contains("めるたいぷ → Meltype😀"));
        profile
            .act(
                json!({"action":"dictionary_remove","reading":"めるたいぷ","word":"Meltype😀"}),
                json!({}),
            )
            .unwrap();
        assert!(!page(h, 1).unwrap().to_string().contains("Meltype😀"));
        profile
            .act(
                json!({"action":"snippet_save"}),
                json!({"name":"mail","text":"一行目\n\"二行目\"😀"}),
            )
            .unwrap();
        let nodes = page(h, 2).unwrap();
        let edit = nodes["nodes"]
            .as_array()
            .unwrap()
            .iter()
            .find(|n| n["action"]["action"] == "snippet_edit")
            .unwrap();
        assert_eq!(
            profile.act(edit["action"].clone(), json!({})).unwrap()["edit"]["text"],
            "一行目\n\"二行目\"😀"
        );
        let path = CString::new(profile.directory.path().to_str().unwrap()).unwrap();
        let reopened = unsafe { meltype_create_at(path.as_ptr()) as jlong };
        assert_eq!(
            request(reopened, json!({"op":"snippets_list"})).unwrap()["mail"],
            "一行目\n\"二行目\"😀"
        );
        meltype_destroy(reopened as *mut c_void);
        profile
            .act(json!({"action":"snippet_remove","name":"mail"}), json!({}))
            .unwrap();
        assert!(request(h, json!({"op":"snippets_list"})).unwrap()["mail"].is_null());
        profile
            .act(json!({"action":"profile_add"}), json!({"name":"テスト"}))
            .unwrap();
        profile
            .act(json!({"action":"profile_switch"}), json!({"name":"テスト"}))
            .unwrap();
        assert_eq!(
            request(h, json!({"op":"settings_get"})).unwrap()["ActiveProfile"],
            "テスト"
        );
        assert_eq!(
            profile
                .act(
                    json!({"action":"profile_select","name":"テスト"}),
                    json!({})
                )
                .unwrap()["edit"]["name"],
            "テスト"
        );
        // The upstream management operation removes the active profile and
        // switches back to another profile; it is not the strict editor API.
        profile
            .act(json!({"action":"profile_delete"}), json!({"name":"テスト"}))
            .unwrap();
        assert_ne!(
            request(h, json!({"op":"settings_get"})).unwrap()["ActiveProfile"],
            "テスト"
        );
        profile
            .act(json!({"action":"profile_add"}), json!({"name":"テスト"}))
            .unwrap();
        profile
            .act(json!({"action":"profile_switch"}), json!({"name":original}))
            .unwrap();
        profile
            .act(json!({"action":"profile_delete"}), json!({"name":"テスト"}))
            .unwrap();
        profile
            .act(
                json!({"action":"learning_remove"}),
                json!({"key":"missing"}),
            )
            .unwrap();
        let confirmation = profile
            .act(json!({"action":"learning_confirm"}), json!({}))
            .unwrap();
        assert_eq!(confirmation["confirm"]["positive"], "削除");
        profile
            .act(confirmation["confirm"]["action"].clone(), json!({}))
            .unwrap();
        assert!(
            request(h, json!({"op":"learning_list"})).unwrap()["languages"]
                .as_object()
                .unwrap()
                .is_empty()
        );
    }
    #[test]
    fn invalid_settings_actions_cannot_mutate_storage_or_choose_file_direction() {
        let profile = Profile::new();
        let before = request(profile.handle, json!({"op":"settings_get"})).unwrap();
        for (operation, fields) in [
            (
                json!({"action":"toggle","name":"Profiles"}),
                json!({"checked":true}),
            ),
            (
                json!({"action":"toggle","name":"Enabled"}),
                json!({"checked":"false"}),
            ),
            (
                json!({"action":"dictionary_add"}),
                json!({"reading":"める"}),
            ),
            (
                json!({"action":"pick","operation":"learning_clear"}),
                json!({}),
            ),
            (json!({"action":"unknown"}), json!({})),
        ] {
            assert!(profile.act(operation, fields).is_err());
        }
        assert_eq!(
            before,
            request(profile.handle, json!({"op":"settings_get"})).unwrap()
        );
        for (operation, output) in [
            ("dictionary_import", false),
            ("dictionary_export", true),
            ("backup_create", true),
            ("backup_restore", false),
        ] {
            let result = profile
                .act(
                    json!({"action":"pick","operation":operation}),
                    json!({"output":!output}),
                )
                .unwrap();
            assert_eq!(result["pick"]["operation"], operation);
            assert_eq!(result["pick"]["output"], output);
        }
        assert!(page(profile.handle, -1).is_err());
        assert!(page(profile.handle, 6).is_err());
        assert!(page(0, 0).is_err());
    }
}
