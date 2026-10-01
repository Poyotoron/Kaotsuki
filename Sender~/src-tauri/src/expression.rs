use crate::map::{self, MapView, SlotView};
use serde::Serialize;
use std::collections::HashSet;
use std::fs;
use std::path::Path;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SlotValue {
    pub channel: u8,
    pub index: u8,
    pub value: u8,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApplyResult {
    pub values: Vec<SlotValue>,
    pub applied: usize,
    pub unmatched: usize,
    pub animated: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    pub mesh: String,
    pub blend_shape: String,
    pub weight: f64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ExpressionFile<'a> {
    format: &'static str,
    version: u32,
    map_name: &'a str,
    saved_at: &'a str,
    blend_shapes: Vec<Entry>,
}

/// 既定値と違うスロットを書き出し、書いた件数を返す。
pub fn save(
    path: &Path,
    map: &MapView,
    saved_at: &str,
    desired: impl Fn(u8, u8) -> Option<u8>,
) -> Result<usize, String> {
    let blend_shapes: Vec<Entry> = map
        .slots
        .iter()
        .filter_map(|slot| {
            let value = desired(slot.channel, slot.index)?;
            (value != slot.default_value).then(|| Entry {
                mesh: slot.mesh.clone(),
                blend_shape: slot.blend_shape.clone(),
                weight: (f64::from(value) * 100.0 / 255.0 * 100.0).round() / 100.0,
            })
        })
        .collect();
    let count = blend_shapes.len();
    let file = ExpressionFile {
        format: "kaotsuki-expression",
        version: 1,
        map_name: &map.map_name,
        saved_at,
        blend_shapes,
    };
    let content = serde_json::to_string_pretty(&file)
        .map_err(|error| format!("表情を保存できませんでした: {error}"))?;
    if let Some(parent) = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
    {
        fs::create_dir_all(parent)
            .map_err(|error| format!("表情を保存できませんでした: {error}"))?;
    }
    fs::write(path, content).map_err(|error| format!("表情を保存できませんでした: {error}"))?;
    Ok(count)
}

/// 表情ファイルを読む。
pub fn load(path: &Path) -> Result<Vec<Entry>, String> {
    let unsupported = || "対応していないファイルです".to_string();
    let content = fs::read_to_string(path).map_err(|_| unsupported())?;
    let content = content.strip_prefix('\u{feff}').unwrap_or(&content);
    let value: serde_json::Value = serde_json::from_str(content).map_err(|_| unsupported())?;
    if value["format"].as_str() != Some("kaotsuki-expression")
        || value["version"].as_u64() != Some(1)
    {
        return Err(unsupported());
    }
    let entries = value["blendShapes"].as_array().ok_or_else(unsupported)?;
    Ok(entries
        .iter()
        .filter_map(|entry| {
            let mesh = entry["mesh"].as_str()?;
            let blend_shape = entry["blendShape"].as_str()?;
            let weight = entry["weight"].as_f64()?;
            (weight.is_finite() && (0.0..=100.0).contains(&weight)).then(|| Entry {
                mesh: mesh.to_string(),
                blend_shape: blend_shape.to_string(),
                weight,
            })
        })
        .collect())
}

/// 照合して全スロットの値を決める。
pub fn apply(entries: &[Entry], slots: &[SlotView]) -> ApplyResult {
    let mut used = HashSet::new();
    let values = slots
        .iter()
        .map(|slot| {
            let exact = entries
                .iter()
                .position(|entry| entry.mesh == slot.mesh && entry.blend_shape == slot.blend_shape);
            // NOTE: 別のアバターでは顔のメッシュのパスが違うことがある。同じ名前が 1 つだけなら同じブレンドシェイプとみなす。
            let matched = exact.or_else(|| {
                let mut same_name = entries
                    .iter()
                    .enumerate()
                    .filter(|(_, entry)| entry.blend_shape == slot.blend_shape);
                let (index, _) = same_name.next()?;
                same_name.next().is_none().then_some(index)
            });
            let value = match matched {
                Some(index) => {
                    used.insert(index);
                    map::weight_to_value(entries[index].weight as f32)
                }
                None => slot.default_value,
            };
            SlotValue {
                channel: slot.channel,
                index: slot.index,
                value,
            }
        })
        .collect();
    ApplyResult {
        values,
        applied: used.len(),
        unmatched: entries.len() - used.len(),
        animated: false,
    }
}

#[cfg(test)]
mod tests {
    use super::{apply, load, save, Entry};
    use crate::map::{weight_to_value, MapView, SlotView};
    use std::fs;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static NEXT_FILE_ID: AtomicUsize = AtomicUsize::new(0);

    fn with_file(test: impl FnOnce(&std::path::Path)) {
        let path = std::env::temp_dir().join(format!(
            "kaotsuki-expression-test-{}-{}.json",
            std::process::id(),
            NEXT_FILE_ID.fetch_add(1, Ordering::Relaxed)
        ));
        test(&path);
        let _ = fs::remove_file(path);
    }

    fn slot(index: u8, mesh: &str, name: &str) -> SlotView {
        SlotView {
            channel: 1,
            index,
            mesh: mesh.to_string(),
            blend_shape: name.to_string(),
            default_weight: 0.0,
            default_value: 0,
            group: String::new(),
        }
    }

    fn entry(mesh: &str, name: &str, weight: f64) -> Entry {
        Entry {
            mesh: mesh.to_string(),
            blend_shape: name.to_string(),
            weight,
        }
    }

    #[test]
    fn save_writes_only_changed_slots() {
        with_file(|path| {
            let map = MapView {
                path: String::new(),
                map_name: "Foo".to_string(),
                avatar_name: "Foo_Casual".to_string(),
                avatars: Vec::new(),
                generated_at: String::new(),
                channel_count: 1,
                has_lip_sync: false,
                slots: vec![
                    slot(1, "Body", "a"),
                    slot(2, "Body", "b"),
                    slot(3, "Body", "c"),
                ],
            };
            let count = save(path, &map, "2026-09-30T12:00:00+09:00", |_, index| {
                Some(if index == 2 { 128 } else { 0 })
            })
            .expect("save expression");
            assert_eq!(count, 1);
            let bytes = fs::read(path).expect("read bytes");
            assert!(!bytes.starts_with(&[0xef, 0xbb, 0xbf]));
            let entries = load(path).expect("load expression");
            assert_eq!(entries.len(), 1);
            assert_eq!(entries[0].blend_shape, "b");
            assert_eq!(entries[0].weight, 50.2);
        });
    }

    #[test]
    fn round_trip_keeps_value() {
        for value in 0..=255u8 {
            let weight = (f64::from(value) * 100.0 / 255.0 * 100.0).round() / 100.0;
            assert_eq!(weight_to_value(weight as f32), value);
        }
    }

    #[test]
    fn apply_matches_exact_then_unique_name() {
        let slots = [
            slot(1, "Body", "a"),
            slot(2, "Face", "b"),
            slot(3, "Body", "c"),
        ];
        let entries = [
            entry("Body", "a", 50.0),
            entry("Other", "b", 20.0),
            entry("X", "c", 10.0),
            entry("Y", "c", 30.0),
            entry("Z", "zz", 5.0),
        ];
        let result = apply(&entries, &slots);
        assert_eq!(
            result
                .values
                .iter()
                .map(|value| value.value)
                .collect::<Vec<_>>(),
            [128, 51, 0]
        );
        assert_eq!(result.applied, 2);
        assert_eq!(result.unmatched, 3);
    }

    #[test]
    fn invalid_entries_are_ignored() {
        with_file(|path| {
            fs::write(path, r#"{"format":"kaotsuki-expression","version":1,"blendShapes":[
                {"mesh":"Body","blendShape":"a","weight":120}, {"mesh":"Body","blendShape":"b","weight":"x"},
                {"mesh":1,"blendShape":"c","weight":10}, {"mesh":"Body","blendShape":"d","weight":20}] }"#).expect("write expression");
            let entries = load(path).expect("load expression");
            assert_eq!(entries.len(), 1);
            assert_eq!(entries[0].blend_shape, "d");
        });
    }

    #[test]
    fn wrong_format_is_rejected() {
        with_file(|path| {
            fs::write(
                path,
                r#"{"format":"kaotsuki-map","version":1,"blendShapes":[]}"#,
            )
            .expect("write expression");
            assert_eq!(
                load(path).err().as_deref(),
                Some("対応していないファイルです")
            );
        });
    }

    #[test]
    fn entries_can_be_reused_but_duplicates_use_first_exact_match() {
        let slots = [slot(1, "Body", "a"), slot(2, "Face", "a")];
        let result = apply(&[entry("Body", "a", 10.0)], &slots);
        assert_eq!(result.applied, 1);
        assert_eq!(result.unmatched, 0);
        assert_eq!(result.values[1].value, 26);
        let result = apply(
            &[entry("Body", "a", 10.0), entry("Body", "a", 90.0)],
            &slots,
        );
        assert_eq!(result.values[0].value, 26);
        assert_eq!(result.values[1].value, 0);
        assert_eq!(result.unmatched, 1);
    }

    #[test]
    fn bom_is_accepted_and_non_array_is_rejected() {
        with_file(|path| {
            fs::write(
                path,
                "\u{feff}{\"format\":\"kaotsuki-expression\",\"version\":1,\"blendShapes\":[]}",
            )
            .expect("write expression");
            assert!(load(path).expect("load BOM").is_empty());
            fs::write(
                path,
                r#"{"format":"kaotsuki-expression","version":1,"blendShapes":{}}"#,
            )
            .expect("write expression");
            assert_eq!(
                load(path).err().as_deref(),
                Some("対応していないファイルです")
            );
        });
    }
}
