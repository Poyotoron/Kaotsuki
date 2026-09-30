use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fs;
use std::path::Path;
use std::time::UNIX_EPOCH;

const OSC_PREFIX: &str = "/avatar/parameters/";

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawMap {
    format: String,
    version: u32,
    #[serde(default)]
    map_name: String,
    avatar_name: String,
    #[serde(default)]
    blueprint_id: String,
    #[serde(default)]
    avatars: Option<Vec<RawAvatar>>,
    #[serde(default)]
    generated_at: String,
    parameters: RawParameters,
    value_max: u32,
    slots: Vec<RawSlot>,
}

#[derive(Deserialize)]
struct RawParameters {
    enabled: String,
    channels: Vec<RawChannel>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawAvatar {
    #[serde(default)]
    name: String,
    #[serde(default)]
    blueprint_id: String,
}

#[derive(Deserialize)]
struct RawChannel {
    index: String,
    value: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawSlot {
    channel: u32,
    index: u32,
    mesh: String,
    blend_shape: String,
    default_weight: f32,
    #[serde(default)]
    group: String,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct AvatarRef {
    pub name: String,
    pub blueprint_id: String,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct MapView {
    pub path: String,
    pub avatar_name: String,
    pub map_name: String,
    pub avatars: Vec<AvatarRef>,
    pub generated_at: String,
    pub channel_count: u8,
    pub slots: Vec<SlotView>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct SlotView {
    pub channel: u8,
    pub index: u8,
    pub mesh: String,
    pub blend_shape: String,
    pub default_weight: f32,
    pub default_value: u8,
    pub group: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MapEntry {
    pub path: String,
    pub avatar_name: String,
    pub map_name: String,
    pub avatar_names: Vec<String>,
    pub blueprint_ids: Vec<String>,
    pub modified: u64,
}

#[derive(Clone)]
pub struct ChannelAddresses {
    pub index: String,
    pub value: String,
}

#[derive(Clone)]
pub struct ParamNames {
    pub enabled: String,
    pub channels: Vec<ChannelAddresses>,
}

pub const MAX_CHANNELS: usize = 15;

pub fn load(path: &Path) -> Result<(MapView, ParamNames), String> {
    let content = fs::read_to_string(path).map_err(|_| "対応していないファイルです".to_string())?;
    let content = content.strip_prefix('\u{feff}').unwrap_or(&content);
    let raw: RawMap =
        serde_json::from_str(content).map_err(|_| "対応していないファイルです".to_string())?;

    if raw.format != "kaotsuki-map"
        || raw.version != 1
        || raw.value_max != 255
        || raw.parameters.channels.is_empty()
        || raw.parameters.channels.len() > MAX_CHANNELS
    {
        return Err("対応していないファイルです".to_string());
    }

    let map_name = if raw.map_name.trim().is_empty() {
        raw.avatar_name.clone()
    } else {
        raw.map_name.trim().to_string()
    };
    let avatars = match raw.avatars {
        Some(list) => list
            .into_iter()
            .map(|avatar| AvatarRef {
                name: avatar.name,
                blueprint_id: avatar.blueprint_id,
            })
            .collect(),
        None if !raw.blueprint_id.is_empty() => vec![AvatarRef {
            name: raw.avatar_name.clone(),
            blueprint_id: raw.blueprint_id,
        }],
        None => Vec::new(),
    };
    let channel_count = raw.parameters.channels.len();
    let mut seen = HashSet::new();
    let slots = raw
        .slots
        .into_iter()
        .filter_map(|slot| {
            if !(1..=channel_count as u32).contains(&slot.channel)
                || !(1..=255).contains(&slot.index)
                || !seen.insert((slot.channel, slot.index))
            {
                return None;
            }

            Some(SlotView {
                channel: slot.channel as u8,
                index: slot.index as u8,
                mesh: slot.mesh,
                blend_shape: slot.blend_shape,
                default_weight: slot.default_weight,
                default_value: weight_to_value(slot.default_weight),
                group: slot.group,
            })
        })
        .collect();

    let params = ParamNames {
        enabled: format!("{OSC_PREFIX}{}", raw.parameters.enabled),
        channels: raw
            .parameters
            .channels
            .into_iter()
            .map(|channel| ChannelAddresses {
                index: format!("{OSC_PREFIX}{}", channel.index),
                value: format!("{OSC_PREFIX}{}", channel.value),
            })
            .collect(),
    };
    let view = MapView {
        path: path.to_string_lossy().into_owned(),
        avatar_name: raw.avatar_name,
        map_name,
        avatars,
        generated_at: raw.generated_at,
        channel_count: channel_count as u8,
        slots,
    };
    Ok((view, params))
}

pub fn list(folder: &Path) -> Vec<MapEntry> {
    let Ok(entries) = fs::read_dir(folder) else {
        return Vec::new();
    };

    let mut maps = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|value| value.to_str()) != Some("json") {
            continue;
        }

        let Ok((map, _)) = load(&path) else {
            continue;
        };
        let modified = entry
            .metadata()
            .and_then(|metadata| metadata.modified())
            .ok()
            .and_then(|time| time.duration_since(UNIX_EPOCH).ok())
            .map(|duration| duration.as_millis().min(u64::MAX as u128) as u64)
            .unwrap_or(0);
        maps.push(MapEntry {
            path: map.path,
            avatar_name: map.avatar_name,
            map_name: map.map_name,
            avatar_names: map
                .avatars
                .iter()
                .map(|avatar| avatar.name.clone())
                .collect(),
            blueprint_ids: map
                .avatars
                .iter()
                .filter(|avatar| !avatar.blueprint_id.is_empty())
                .map(|avatar| avatar.blueprint_id.clone())
                .collect(),
            modified,
        });
    }

    maps.sort_by_key(|entry| std::cmp::Reverse(entry.modified));
    maps
}

pub fn weight_to_value(weight: f32) -> u8 {
    (weight.clamp(0.0, 100.0) * 255.0 / 100.0).round() as u8
}

/// アバター ID が avatars に含まれるマップのパス。複数あれば更新日時が新しいもの。
pub fn find_by_avatar(folder: &Path, avatar_id: &str) -> Option<String> {
    list(folder)
        .into_iter()
        .find(|entry| entry.blueprint_ids.iter().any(|id| id == avatar_id))
        .map(|entry| entry.path)
}

#[cfg(test)]
mod tests {
    use super::{load, weight_to_value};
    use std::fs;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static NEXT_FILE_ID: AtomicUsize = AtomicUsize::new(0);

    fn with_map_file(content: &str, test: impl FnOnce(&std::path::Path)) {
        let path = std::env::temp_dir().join(format!(
            "kaotsuki-map-test-{}-{}.json",
            std::process::id(),
            NEXT_FILE_ID.fetch_add(1, Ordering::Relaxed)
        ));
        fs::write(&path, content).expect("write test map");
        test(&path);
        let _ = fs::remove_file(path);
    }

    #[test]
    fn weight_conversion_clamps_and_rounds() {
        assert_eq!(weight_to_value(-10.0), 0);
        assert_eq!(weight_to_value(50.0), 128);
        assert_eq!(weight_to_value(100.0), 255);
        assert_eq!(weight_to_value(200.0), 255);
    }

    #[test]
    fn broken_json_is_rejected() {
        with_map_file("{broken", |path| {
            assert_eq!(
                load(path).err().as_deref(),
                Some("対応していないファイルです")
            );
        });
    }

    #[test]
    fn wrong_format_is_rejected() {
        with_map_file(
            r#"{
                "format":"something-else",
                "version":1,
                "avatarName":"Test",
                "parameters":{"enabled":"Enabled","channels":[{"index":"Index","value":"Value"}]},
                "valueMax":255,
                "slots":[]
            }"#,
            |path| {
                assert_eq!(
                    load(path).err().as_deref(),
                    Some("対応していないファイルです")
                );
            },
        );
    }

    #[test]
    fn empty_channels_are_rejected() {
        with_map_file(
            r#"{
                "format":"kaotsuki-map",
                "version":1,
                "avatarName":"Test",
                "parameters":{"enabled":"Enabled","channels":[]},
                "valueMax":255,
                "slots":[]
            }"#,
            |path| {
                assert_eq!(
                    load(path).err().as_deref(),
                    Some("対応していないファイルです")
                );
            },
        );
    }

    #[test]
    fn slots_use_channel_index_pairs() {
        with_map_file(
            r#"{
                "format":"kaotsuki-map",
                "version":1,
                "avatarName":"Test",
                "parameters":{"enabled":"Enabled","channels":[
                    {"index":"Index","value":"Value"},
                    {"index":"Index2","value":"Value2"}
                ]},
                "valueMax":255,
                "slots":[
                    {"channel":1,"index":1,"mesh":"Body","blendShape":"A","defaultWeight":0},
                    {"channel":2,"index":1,"mesh":"Body","blendShape":"B","defaultWeight":0},
                    {"channel":2,"index":1,"mesh":"Body","blendShape":"Duplicate","defaultWeight":0},
                    {"channel":3,"index":1,"mesh":"Body","blendShape":"Outside","defaultWeight":0}
                ]
            }"#,
            |path| {
                let (map, params) = load(path).expect("load map");
                assert_eq!(map.channel_count, 2);
                assert_eq!(map.slots.len(), 2);
                assert_eq!((map.slots[1].channel, map.slots[1].index), (2, 1));
                assert_eq!(params.channels[1].index, "/avatar/parameters/Index2");
            },
        );
    }

    fn map_json(fields: serde_json::Value) -> String {
        let mut value = serde_json::json!({
            "format": "kaotsuki-map", "version": 1, "avatarName": "Foo_Casual",
            "blueprintId": "avtr_a", "valueMax": 255,
            "parameters": {"enabled": "Kaotsuki/Enabled", "channels": [{"index": "Kaotsuki/Index", "value": "Kaotsuki/Value"}]},
            "slots": [{"channel": 1, "index": 1, "mesh": "Body", "blendShape": "a", "defaultWeight": 0}]
        });
        for (key, entry) in fields.as_object().expect("fields object") {
            value[key] = entry.clone();
        }
        value.to_string()
    }

    #[test]
    fn legacy_map_gets_defaults() {
        with_map_file(&map_json(serde_json::json!({})), |path| {
            let (map, _) = load(path).expect("legacy map");
            assert_eq!(map.map_name, map.avatar_name);
            assert_eq!(map.avatars.len(), 1);
            assert_eq!(map.avatars[0].blueprint_id, "avtr_a");
            assert!(map.slots.iter().all(|slot| slot.group.is_empty()));
        });
    }

    #[test]
    fn legacy_map_without_blueprint_has_no_avatars() {
        with_map_file(&map_json(serde_json::json!({"blueprintId": ""})), |path| {
            let (map, _) = load(path).expect("legacy map");
            assert!(map.avatars.is_empty());
        });
    }

    #[test]
    fn new_fields_are_read() {
        let json = map_json(serde_json::json!({
            "mapName": " Foo ",
            "avatars": [{"name": "Foo_Casual", "blueprintId": "avtr_a"}, {"name": "Foo_Swimsuit", "blueprintId": "avtr_b"}],
            "slots": [
                {"channel": 1, "index": 1, "mesh": "Body", "blendShape": "a", "defaultWeight": 0, "group": ""},
                {"channel": 1, "index": 2, "mesh": "Body", "blendShape": "b", "defaultWeight": 0, "group": "目"}
            ]
        }));
        with_map_file(&json, |path| {
            let (map, _) = load(path).expect("map");
            assert_eq!(map.map_name, "Foo");
            assert_eq!(map.avatars.len(), 2);
            assert_eq!(map.slots[1].group, "目");
        });
    }

    #[test]
    fn explicit_empty_avatars_does_not_use_legacy_id() {
        with_map_file(&map_json(serde_json::json!({"avatars": []})), |path| {
            assert!(load(path).expect("map").0.avatars.is_empty());
        });
    }

    #[test]
    fn find_by_avatar_uses_avatars() {
        let folder = std::env::temp_dir().join(format!(
            "kaotsuki-map-folder-{}-{}",
            std::process::id(),
            NEXT_FILE_ID.fetch_add(1, Ordering::Relaxed)
        ));
        fs::create_dir_all(&folder).expect("create folder");
        let path = folder.join("shared.json");
        fs::write(&path, map_json(serde_json::json!({"blueprintId": "avtr_last", "avatars": [{"name": "Foo", "blueprintId": "avtr_a"}]}))).expect("write map");
        assert_eq!(
            super::find_by_avatar(&folder, "avtr_a"),
            Some(path.to_string_lossy().into_owned())
        );
        assert_eq!(super::find_by_avatar(&folder, "avtr_b"), None);
        assert_eq!(super::find_by_avatar(&folder, "avtr_last"), None);
        fs::remove_file(path).expect("remove map");
        fs::remove_dir(folder).expect("remove folder");
    }
}
