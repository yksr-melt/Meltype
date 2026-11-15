// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
use meltype_core::{settings::Settings, settings_schema};
use serde_json::json;

#[test]
fn schema_covers_defaults_choices_labels_and_optional_fields() {
    let schema = settings_schema::schema();
    assert_eq!(schema["additionalProperties"], false);
    let mut fields = std::collections::HashSet::new();
    for field in settings_schema::metadata()["fields"].as_array().unwrap() {
        let key = field["key"].as_str().unwrap();
        assert!(fields.insert(key), "duplicate setting field: {key}");
    }
    for (key, value) in Settings::default().0 {
        assert!(schema["properties"].get(&key).is_some(), "missing {key}");
        assert_eq!(schema["properties"][&key]["default"], value, "{key}");
        if let Some(choices) = schema["properties"][&key]["enum"].as_array() {
            assert!(choices.contains(&value), "default for {key} outside enum");
        }
    }
    // 通貨の換算のレートの出どころ (固定・自動・手入力)。既定は同梱の固定
    let currency = &schema["properties"]["CurrencyRateMode"];
    assert_eq!(currency["title"], "通貨のレート");
    assert_eq!(currency["default"], "Fixed");
    assert_eq!(currency["enum"].as_array().unwrap().len(), 3);
    assert_eq!(
        schema["properties"]["CurrencyManualRates"]["type"],
        "string"
    );
    // 換算した額の小数の桁 (0〜4。どのレートの出どころでも同じ設定を使う)
    assert_eq!(schema["properties"]["CurrencyDecimals"]["default"], 1);
    assert_eq!(schema["properties"]["CurrencyDecimals"]["type"], "integer");
    let mut digits = Settings::default();
    digits.set("CurrencyDecimals", json!(9)).unwrap();
    assert_eq!(digits.integer("CurrencyDecimals"), 4);
    digits.set("CurrencyDecimals", json!(-2)).unwrap();
    assert_eq!(digits.integer("CurrencyDecimals"), 0);
    assert!(
        digits.set("CurrencyDecimals", json!("2")).is_err(),
        "数でない値は受け付けない"
    );
    assert_eq!(digits.integer("CurrencyDecimals"), 0);
    let mut settings = Settings::default();
    settings.set("CurrencyRateMode", json!("Manual")).unwrap();
    settings.normalize();
    assert_eq!(settings.text("CurrencyRateMode"), "Manual");
    settings.set("CurrencyRateMode", json!("unknown")).unwrap();
    settings.normalize();
    assert_eq!(settings.text("CurrencyRateMode"), "Fixed");
    let punctuation = &schema["properties"]["Punctuation"];
    assert_eq!(punctuation["title"], "句読点");
    assert_eq!(punctuation["enum"].as_array().unwrap().len(), 4);
    for key in settings_schema::OPTIONAL_BOOLEANS {
        assert_eq!(schema["properties"][*key]["default"], false);
        let mut settings = Settings::default();
        assert!(!settings.boolean(key));
        assert!(settings.set(key, json!("true")).is_err());
        settings.set(key, json!(true)).unwrap();
        assert!(Settings::parse(&serde_json::to_string(&settings).unwrap())
            .unwrap()
            .boolean(key));
    }
}

#[cfg(feature = "filesystem")]
#[test]
fn issue_1073_explicit_export_links_schema_and_preserves_current_settings() {
    use meltype_core::{control, storage::Store};
    let directory = std::env::temp_dir().join(format!("meltype-schema-{}", std::process::id()));
    std::fs::create_dir_all(&directory).unwrap();
    let store = Store::new(&directory);
    store
        .change_settings(|s| s.set("Punctuation", json!("FullWidthCommaPeriod")))
        .unwrap();
    control::execute(&store, &json!({"op":"settings_schema_export"})).unwrap();
    let settings = store.settings().unwrap();
    assert_eq!(settings.text("$schema"), "./config.schema.json");
    assert_eq!(settings.text("Punctuation"), "FullWidthCommaPeriod");
    let file: serde_json::Value =
        serde_json::from_slice(&std::fs::read(directory.join("config.schema.json")).unwrap())
            .unwrap();
    assert_eq!(file, settings_schema::schema());
    assert_eq!(
        control::execute(&store, &json!({"op":"settings_schema"})).unwrap(),
        file
    );
    store
        .change_settings(|s| s.set("LiveConversion", json!(false)))
        .unwrap();
    assert_eq!(
        store.settings().unwrap().text("$schema"),
        "./config.schema.json"
    );
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn optional_fields_keep_profile_isolation_and_survive_profile_export_import() {
    let mut settings = Settings::default();
    settings.normalize();
    let original = settings.text("ActiveProfile").to_owned();
    settings.add_profile("カタカナ").unwrap();
    settings.set("MuhenkanKatakanaCycle", json!(true)).unwrap();
    settings
        .set("$schema", json!("./config.schema.json"))
        .unwrap();
    let exported = serde_json::to_string(&settings.export_profile()).unwrap();
    settings.switch_profile(&original).unwrap();
    assert!(!settings.boolean("MuhenkanKatakanaCycle"));
    assert_eq!(settings.text("$schema"), "./config.schema.json");
    settings.switch_profile("カタカナ").unwrap();
    assert!(settings.boolean("MuhenkanKatakanaCycle"));
    let mut imported = Settings::default();
    imported.normalize();
    let name = imported.import_profile(&exported).unwrap();
    imported.switch_profile(&name).unwrap();
    assert!(imported.boolean("MuhenkanKatakanaCycle"));
}
