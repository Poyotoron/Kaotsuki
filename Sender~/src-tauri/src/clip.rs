use crate::expression::Entry;
use crate::map::MapView;
use std::fmt::Write;
use std::fs;
use std::path::Path;

// NOTE: Unity の YAML は決まった形で書かれるので、YAML のクレートは使わず、AnimationClip のカーブの部分だけを行単位で読む。
//       ASCII 以外の名前は "\uXXXX" のエスケープ付きのダブルクォートで書かれる。

/// AnimationClip から取り出したブレンドシェイプのウェイト。
pub struct ClipFile {
    pub entries: Vec<Entry>,
    /// 値の違うキーを 2 つ以上持つカーブがあったか。
    pub animated: bool,
}

pub fn load(path: &Path) -> Result<ClipFile, String> {
    let bytes = fs::read(path).map_err(|_| "対応していないファイルです".to_string())?;
    let content = std::str::from_utf8(without_bom(&bytes))
        .map_err(|_| "対応していないファイルです".to_string())?;
    parse(content)
}

fn without_bom(content: &[u8]) -> &[u8] {
    content.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(content)
}

fn parse(content: &str) -> Result<ClipFile, String> {
    if !content
        .lines()
        .find(|line| !line.trim().is_empty())
        .is_some_and(|line| line.starts_with("%YAML"))
    {
        return Err("テキスト形式で保存された AnimationClip だけを読み込めます".to_string());
    }
    if !content.lines().any(|line| line.starts_with("--- !u!74 ")) {
        return Err("対応していないファイルです".to_string());
    }
    let lines: Vec<_> = content.lines().collect();
    let mut clip = read_section(&lines, "  m_FloatCurves:");
    if clip.entries.is_empty() {
        clip = read_section(&lines, "  m_EditorCurves:");
    }
    Ok(clip)
}

#[derive(Default)]
struct Curve {
    attribute: String,
    path: String,
    class_id: String,
    values: Vec<Option<f64>>,
    invalid: bool,
}

impl Curve {
    fn finish(self, clip: &mut ClipFile) {
        if self.invalid || self.class_id != "137" {
            return;
        }
        let Some(name) = self.attribute.strip_prefix("blendShape.") else {
            return;
        };
        let Some(Some(first)) = self.values.first() else {
            return;
        };
        if name.is_empty() || !first.is_finite() {
            return;
        }
        let mut min = *first;
        let mut max = *first;
        for value in self.values.iter().flatten() {
            min = min.min(*value);
            max = max.max(*value);
        }
        clip.animated |= max - min > 0.0001;
        clip.entries.push(Entry {
            mesh: self.path,
            blend_shape: name.to_string(),
            weight: first.clamp(0.0, 100.0),
        });
    }
}

fn read_section(lines: &[&str], header: &str) -> ClipFile {
    let mut clip = ClipFile {
        entries: Vec::new(),
        animated: false,
    };
    let Some(start) = lines.iter().position(|line| line.trim_end() == header) else {
        return clip;
    };
    let section = &lines[start + 1..];
    let end = section
        .iter()
        .position(|line| {
            !line.trim().is_empty() && indentation(line) <= 2 && !line.starts_with("  - ")
        })
        .unwrap_or(section.len());
    let mut rest = section[..end].iter().copied();
    let mut current: Option<Curve> = None;
    while let Some(line) = rest.next() {
        let item = if let Some(item) = line.strip_prefix("  - ") {
            if let Some(curve) = current.take() {
                curve.finish(&mut clip);
            }
            current = Some(Curve::default());
            Some(item)
        } else if indentation(line) == 4 {
            Some(&line[4..])
        } else {
            None
        };
        let Some(curve) = current.as_mut() else {
            continue;
        };
        if let Some(item) = item {
            let pair = item
                .split_once(": ")
                .or_else(|| item.strip_suffix(':').map(|key| (key, "")));
            if let Some((key, value)) = pair {
                if matches!(key, "attribute" | "path" | "classID") {
                    match read_scalar(value, &mut rest) {
                        Some(value) => match key {
                            "attribute" => curve.attribute = value,
                            "path" => curve.path = value,
                            _ => curve.class_id = value,
                        },
                        None => curve.invalid = true,
                    }
                }
            }
        } else if indentation(line) >= 6 {
            let item = line.trim().strip_prefix("- ").unwrap_or(line.trim());
            if let Some(value) = item.strip_prefix("value: ") {
                curve.values.push(value.trim().parse().ok());
            }
        }
    }
    if let Some(curve) = current {
        curve.finish(&mut clip);
    }
    clip
}

fn indentation(line: &str) -> usize {
    line.bytes().take_while(|value| *value == b' ').count()
}

fn read_scalar<'a>(first: &str, rest: &mut impl Iterator<Item = &'a str>) -> Option<String> {
    let first = first.trim();
    let quote = match first.as_bytes().first() {
        Some(b'"') => b'"',
        Some(b'\'') => b'\'',
        _ => return Some(first.to_string()),
    };
    let mut body = first[1..].to_string();
    loop {
        let bytes = body.as_bytes();
        let mut index = 0;
        while index < bytes.len() {
            if quote == b'"' && bytes[index] == b'\\' {
                index += 2;
            } else if bytes[index] == quote {
                if quote == b'\'' && bytes.get(index + 1) == Some(&b'\'') {
                    index += 2;
                } else {
                    return if quote == b'\'' {
                        Some(body[..index].replace("''", "'"))
                    } else {
                        unescape(&body[..index])
                    };
                }
            } else {
                index += 1;
            }
        }
        let next = rest.next()?.trim();
        if quote == b'"' && body.ends_with('\\') {
            body.pop();
        } else {
            body.push(' ');
        }
        body.push_str(next);
    }
}

fn unescape(value: &str) -> Option<String> {
    let mut units = Vec::new();
    let mut chars = value.chars();
    while let Some(value) = chars.next() {
        let decoded = if value != '\\' {
            value
        } else {
            match chars.next()? {
                '\\' => '\\',
                '"' => '"',
                '/' => '/',
                '0' => '\0',
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                code @ ('x' | 'u' | 'U') => {
                    let digits = match code {
                        'x' => 2,
                        'u' => 4,
                        _ => 8,
                    };
                    let mut number = 0;
                    for _ in 0..digits {
                        number = number * 16 + chars.next()?.to_digit(16)?;
                    }
                    if code == 'u' {
                        units.push(number as u16);
                        continue;
                    }
                    char::from_u32(number)?
                }
                _ => return None,
            }
        };
        units.extend_from_slice(decoded.encode_utf16(&mut [0; 2]));
    }
    String::from_utf16(&units).ok()
}

/// 全スロットのウェイトを 1 キーの定数カーブで書き、書いた件数を返す。
pub fn save(
    path: &Path,
    map: &MapView,
    desired: impl Fn(u8, u8) -> Option<u8>,
) -> Result<usize, String> {
    if path.exists() {
        let bytes = fs::read(path)
            .map_err(|error| format!("AnimationClip を保存できませんでした: {error}"))?;
        if !is_clip(&bytes) {
            return Err("AnimationClip ではないファイルは上書きできません".to_string());
        }
    }
    let curves: Vec<_> = map
        .slots
        .iter()
        .map(|slot| {
            let value = desired(slot.channel, slot.index).unwrap_or(slot.default_value);
            let weight = (f64::from(value) * 100.0 / 255.0 * 100.0).round() / 100.0;
            (slot.mesh.clone(), slot.blend_shape.clone(), weight)
        })
        .collect();
    let name = path
        .file_stem()
        .and_then(|name| name.to_str())
        .unwrap_or("Kaotsuki");
    fs::write(path, render(name, &curves))
        .map_err(|error| format!("AnimationClip を保存できませんでした: {error}"))?;
    Ok(curves.len())
}

fn is_clip(content: &[u8]) -> bool {
    let Ok(content) = std::str::from_utf8(without_bom(content)) else {
        return false;
    };
    content
        .lines()
        .find(|line| !line.trim().is_empty())
        .is_some_and(|line| line.starts_with("%YAML"))
        && content.lines().any(|line| line.starts_with("--- !u!74 "))
}

// NOTE: Unity 2022.3 がブレンドシェイプの定数カーブだけを持つクリップを保存したときと同じ形で書く。
//       既定値のスロットも含めて全スロットを書くのは、Write Defaults OFF のアバターで書かなかった値が前の表情のまま残るため。
fn render(name: &str, curves: &[(String, String, f64)]) -> String {
    let mut curve_text = String::new();
    for (mesh, blend_shape, weight) in curves {
        let attribute = yaml_string(&format!("blendShape.{blend_shape}"));
        let mesh = yaml_string(mesh);
        let _ = write!(curve_text, "  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: {weight}\n        inSlope: 0\n        outSlope: 0\n        tangentMode: 136\n        weightedMode: 0\n        inWeight: 0\n        outWeight: 0\n      m_PreInfinity: 2\n      m_PostInfinity: 2\n      m_RotationOrder: 4\n    attribute: {attribute}\n    path: {mesh}\n    classID: 137\n    script: {{fileID: 0}}\n    flags: 0\n");
    }
    let section = |header: &str| {
        if curves.is_empty() {
            format!("  {header}: []\n")
        } else {
            format!("  {header}:\n{curve_text}")
        }
    };
    let name = yaml_string(name);
    let float_curves = section("m_FloatCurves");
    let editor_curves = section("m_EditorCurves");
    format!("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!74 &7400000\nAnimationClip:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_Name: {name}\n  serializedVersion: 7\n  m_Legacy: 0\n  m_Compressed: 0\n  m_UseHighQualityCurve: 1\n  m_RotationCurves: []\n  m_CompressedRotationCurves: []\n  m_EulerCurves: []\n  m_PositionCurves: []\n  m_ScaleCurves: []\n{float_curves}  m_PPtrCurves: []\n  m_SampleRate: 60\n  m_WrapMode: 0\n  m_Bounds:\n    m_Center: {{x: 0, y: 0, z: 0}}\n    m_Extent: {{x: 0, y: 0, z: 0}}\n  m_ClipBindingConstant:\n    genericBindings: []\n    pptrCurveMapping: []\n  m_AnimationClipSettings:\n    serializedVersion: 2\n    m_AdditiveReferencePoseClip: {{fileID: 0}}\n    m_AdditiveReferencePoseTime: 0\n    m_StartTime: 0\n    m_StopTime: 0\n    m_OrientationOffsetY: 0\n    m_Level: 0\n    m_CycleOffset: 0\n    m_HasAdditiveReferencePose: 0\n    m_LoopTime: 0\n    m_LoopBlend: 0\n    m_LoopBlendOrientation: 0\n    m_LoopBlendPositionY: 0\n    m_LoopBlendPositionXZ: 0\n    m_KeepOriginalOrientation: 0\n    m_KeepOriginalPositionY: 1\n    m_KeepOriginalPositionXZ: 0\n    m_HeightFromFeet: 0\n    m_Mirror: 0\n{editor_curves}  m_EulerEditorCurves: []\n  m_HasGenericRootTransform: 0\n  m_HasMotionFloatCurves: 0\n  m_Events: []\n")
}

fn yaml_string(value: &str) -> String {
    if !value.is_empty()
        && value
            .as_bytes()
            .first()
            .is_some_and(|first| first.is_ascii_alphabetic() || *first == b'_')
        && value
            .bytes()
            .all(|value| value.is_ascii_alphanumeric() || b"_./()- ".contains(&value))
        && !value.ends_with(' ')
    {
        return value.to_string();
    }
    let mut result = String::from("\"");
    for value in value.chars() {
        match value {
            '\\' => result.push_str("\\\\"),
            '"' => result.push_str("\\\""),
            ' '..='~' => result.push(value),
            // NOTE: Unity の YAML パーサーはサロゲート単体のエスケープを拒否するため、補助平面の文字は 1 つの Unicode コードポイントで書く。
            value if u32::from(value) > 0xffff => {
                let _ = write!(result, "\\U{:08X}", u32::from(value));
            }
            _ => {
                for unit in value.encode_utf16(&mut [0; 2]) {
                    let _ = write!(result, "\\u{unit:04X}");
                }
            }
        }
    }
    result.push('"');
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::map::{weight_to_value, SlotView};
    use std::sync::atomic::{AtomicUsize, Ordering};

    const SAMPLE: &str = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!74 &7400000\nAnimationClip:\n  m_FloatCurves:\n  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve:\n      - serializedVersion: 3\n        time: 0\n        value: 50\n        inSlope: 0\n      m_PreInfinity: 2\n    attribute: blendShape.mouth_line_thick\n    path: Body\n    classID: 137\n  m_PPtrCurves: []\n";
    static NEXT_ID: AtomicUsize = AtomicUsize::new(0);

    fn with_file(test: impl FnOnce(&Path)) {
        let path = std::env::temp_dir().join(format!(
            "kaotsuki-clip-test-{}-{}.anim",
            std::process::id(),
            NEXT_ID.fetch_add(1, Ordering::Relaxed)
        ));
        test(&path);
        let _ = fs::remove_file(path);
    }

    #[test]
    fn unity_2022_and_legacy_curves() {
        for content in [
            SAMPLE.to_string(),
            SAMPLE.replace("  - serializedVersion: 2\n    curve:", "  - curve:"),
        ] {
            let clip = parse(&content).expect("clip");
            assert_eq!(clip.entries.len(), 1);
            assert_eq!(clip.entries[0].mesh, "Body");
            assert_eq!(clip.entries[0].blend_shape, "mouth_line_thick");
            assert_eq!(clip.entries[0].weight, 50.0);
            assert!(!clip.animated);
        }
    }

    #[test]
    fn unicode_and_surrogate_names() {
        for (scalar, name) in [
            (r#""blendShape.mouth_\u3007""#, "mouth_〇"),
            (r#""blendShape.\uD83D\uDE00""#, "😀"),
            (r#""blendShape.\U0001F600""#, "😀"),
            ("\"blendShape.😀\"", "😀"),
        ] {
            let clip =
                parse(&SAMPLE.replace("blendShape.mouth_line_thick", scalar)).expect("unicode");
            assert_eq!(clip.entries[0].blend_shape, name);
        }
    }

    #[test]
    fn unrelated_and_invalid_curves_are_ignored() {
        for content in [
            SAMPLE.replace("classID: 137", "classID: 1"),
            SAMPLE.replace("blendShape.mouth_line_thick", "m_IsActive"),
            SAMPLE.replace("blendShape.mouth_line_thick", "blendShape."),
            SAMPLE.replace("value: 50", "value: nope"),
            SAMPLE.replace("value: 50", "value: NaN"),
            SAMPLE.replace("value: 50", "value: inf"),
            SAMPLE.replace("        value: 50\n", ""),
            SAMPLE.replace("path: Body", r#"path: "\uD800""#),
            SAMPLE.replace("path: Body", r#"path: "\q""#),
        ] {
            assert!(parse(&content).expect("invalid curve").entries.is_empty());
        }
    }

    #[test]
    fn first_key_and_animation_threshold() {
        for (first, second, animated) in [
            (0.0, 100.0, true),
            (50.0, 50.0, false),
            (0.0, 0.00005, false),
            (0.0, 0.0002, true),
        ] {
            let content = SAMPLE.replace("value: 50", &format!("value: {first}\n      - serializedVersion: 3\n        time: 1\n        value: {second}"));
            let clip = parse(&content).expect("animated clip");
            assert_eq!(clip.entries[0].weight, first);
            assert_eq!(clip.animated, animated);
        }
    }

    #[test]
    fn weights_are_clamped() {
        for (source, expected) in [(120, 100.0), (-5, 0.0)] {
            assert_eq!(
                parse(&SAMPLE.replace("value: 50", &format!("value: {source}")))
                    .expect("clamped")
                    .entries[0]
                    .weight,
                expected
            );
        }
    }

    #[test]
    fn editor_fallback_and_no_duplicate_curves() {
        let editor = SAMPLE.replace("  m_FloatCurves:", "  m_FloatCurves: []\n  m_EditorCurves:");
        assert_eq!(parse(&editor).expect("editor fallback").entries.len(), 1);
        let both = format!(
            "{SAMPLE}{}",
            SAMPLE.split_once("  m_FloatCurves:").expect("section").1
        );
        assert_eq!(parse(&both).expect("section end").entries.len(), 1);
    }

    #[test]
    fn wrong_file_types() {
        assert_eq!(
            parse("binary").err().as_deref(),
            Some("テキスト形式で保存された AnimationClip だけを読み込めます")
        );
        assert_eq!(
            parse("%YAML 1.1\n--- !u!1 &1\n").err().as_deref(),
            Some("対応していないファイルです")
        );
    }

    #[test]
    fn multiline_and_single_quotes() {
        for (scalar, expected) in [
            ("\"Armature/Hip\n      /Body\"", "Armature/Hip /Body"),
            ("\"Armature/Hip\\\n      /Body\"", "Armature/Hip/Body"),
            ("'It''s'", "It's"),
            ("'Armature/Hip\n      /Body'", "Armature/Hip /Body"),
        ] {
            assert_eq!(
                parse(&SAMPLE.replace("path: Body", &format!("path: {scalar}")))
                    .expect("quoted path")
                    .entries[0]
                    .mesh,
                expected
            );
        }
    }

    #[test]
    fn all_scalar_escapes_and_invalid_sequences() {
        assert_eq!(
            read_scalar(
                r#""a\\\"\/\0\t\n\r\x41\u3042\U0001F600""#,
                &mut std::iter::empty()
            )
            .as_deref(),
            Some("a\\\"/\0\t\n\rAあ😀")
        );
        for scalar in [
            r#""\uD800""#,
            r#""\uDC00""#,
            r#""\U00110000""#,
            r#""\xZZ""#,
            r#""\q""#,
            "\"unclosed",
            "'unclosed",
        ] {
            assert!(read_scalar(scalar, &mut std::iter::empty()).is_none());
        }
        assert_eq!(
            read_scalar(r#""a\\""#, &mut std::iter::empty()).as_deref(),
            Some("a\\")
        );
    }

    #[test]
    fn bom_and_crlf_load() {
        with_file(|path| {
            fs::write(path, format!("\u{feff}{}", SAMPLE.replace('\n', "\r\n")))
                .expect("write BOM");
            assert_eq!(load(path).expect("BOM clip").entries.len(), 1);
        });
    }

    #[test]
    fn render_round_trip_and_empty() {
        let curves = vec![
            ("Body".to_string(), "目😀".to_string(), 50.2),
            ("Armature/Hip /Body".to_string(), "a\"b".to_string(), 100.0),
        ];
        let content = render("表情", &curves);
        assert!(content.starts_with(
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!74 &7400000\nAnimationClip:\n"
        ));
        assert!(content.ends_with("  m_Events: []\n"));
        assert!(!content.contains('\r'));
        let clip = parse(&content).expect("round trip");
        assert!(!clip.animated);
        let actual: Vec<_> = clip
            .entries
            .into_iter()
            .map(|entry| (entry.mesh, entry.blend_shape, entry.weight))
            .collect();
        assert_eq!(actual, curves);
        let empty = render("empty", &[]);
        assert!(empty.contains("  m_FloatCurves: []\n"));
        assert!(empty.contains("  m_EditorCurves: []\n"));
        assert!(parse(&empty).expect("empty clip").entries.is_empty());
    }

    #[test]
    fn yaml_strings() {
        for plain in [
            "Body",
            "Armature/Hip/Body",
            "blendShape.vrc.v_aa",
            "Mouth (L)",
        ] {
            assert_eq!(yaml_string(plain), plain);
        }
        for (value, expected) in [
            ("blendShape.目", r#""blendShape.\u76EE""#),
            ("blendShape.😀", r#""blendShape.\U0001F600""#),
            ("a\"b", r#""a\"b""#),
            ("1abc", "\"1abc\""),
            ("", "\"\""),
            ("a ", "\"a \""),
            ("a:b", "\"a:b\""),
        ] {
            assert_eq!(yaml_string(value), expected);
        }
    }

    fn test_map() -> MapView {
        MapView {
            path: String::new(),
            map_name: "sample".to_string(),
            avatar_name: "sample".to_string(),
            avatars: Vec::new(),
            generated_at: String::new(),
            channel_count: 1,
            has_lip_sync: false,
            slots: (1..=2)
                .map(|index| SlotView {
                    channel: 1,
                    index,
                    mesh: "Body".to_string(),
                    blend_shape: format!("shape{index}"),
                    default_weight: 100.0,
                    default_value: 255,
                    group: String::new(),
                })
                .collect(),
        }
    }

    #[test]
    fn save_all_slots_fallback_and_protected_overwrite() {
        with_file(|path| {
            let map = test_map();
            assert_eq!(
                save(path, &map, |_, index| (index == 1).then_some(128)).expect("save"),
                2
            );
            let content = fs::read_to_string(path).expect("read");
            assert!(content.contains("value: 50.2\n"));
            let clip = load(path).expect("load saved");
            assert_eq!(clip.entries[1].weight, 100.0);
            save(path, &map, |_, _| Some(0)).expect("overwrite clip");
            let json = r#"{"format":"kaotsuki-expression"}"#;
            fs::write(path, json).expect("write JSON");
            assert_eq!(
                save(path, &map, |_, _| None).err().as_deref(),
                Some("AnimationClip ではないファイルは上書きできません")
            );
            assert_eq!(fs::read_to_string(path).expect("protected file"), json);
        });
    }

    #[test]
    fn every_sender_value_round_trips() {
        with_file(|path| {
            for value in 0..=255 {
                save(path, &test_map(), |_, _| Some(value)).expect("save value");
                assert_eq!(
                    weight_to_value(load(path).expect("load value").entries[0].weight as f32),
                    value
                );
            }
        });
    }
}
