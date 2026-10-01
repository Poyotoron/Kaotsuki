use crate::clip;
use crate::expression::{self, ApplyResult};
use crate::map::{self, MapEntry, MapView};
use crate::receiver::{ReceiveStatus, Receiver};
use crate::scheduler::Scheduler;
use crate::settings::Settings;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager, State};

pub struct AppState {
    pub scheduler: Arc<Mutex<Scheduler>>,
    pub settings: Mutex<Settings>,
    pub receiver: Mutex<Option<Receiver>>,
    pub receive_status: Mutex<ReceiveStatus>,
    pub current_map: Mutex<Option<MapView>>,
}

pub fn map_folder(app: &AppHandle) -> Result<PathBuf, String> {
    Ok(app
        .path()
        .local_data_dir()
        .map_err(|error| error.to_string())?
        .join("Kaotsuki")
        .join("Maps"))
}

#[tauri::command]
pub fn list_maps(app: AppHandle) -> Result<Vec<MapEntry>, String> {
    Ok(map::list(&map_folder(&app)?))
}

pub fn restart_receiver(app: &AppHandle, state: &AppState, settings: &Settings) {
    let Ok(mut receiver) = state.receiver.lock() else {
        return;
    };
    if let Some(previous) = receiver.take() {
        previous.stop();
    }
    let status = if !settings.receive {
        ReceiveStatus {
            state: "disabled",
            port: settings.receive_port,
        }
    } else {
        match Receiver::start(
            app.clone(),
            Arc::clone(&state.scheduler),
            settings.receive_port,
        ) {
            Ok(started) => {
                *receiver = Some(started);
                ReceiveStatus {
                    state: "listening",
                    port: settings.receive_port,
                }
            }
            Err(_) => ReceiveStatus {
                state: "failed",
                port: settings.receive_port,
            },
        }
    };
    if let Ok(mut stored) = state.receive_status.lock() {
        *stored = status.clone();
    }
    drop(receiver);
    let _ = app.emit("receive-status", status);
}

#[tauri::command]
pub fn get_receive_status(state: State<'_, AppState>) -> ReceiveStatus {
    state
        .receive_status
        .lock()
        .map(|status| status.clone())
        .unwrap_or(ReceiveStatus {
            state: "failed",
            port: 9001,
        })
}

#[tauri::command]
pub fn load_map(
    app: AppHandle,
    state: State<'_, AppState>,
    path: String,
) -> Result<MapView, String> {
    let (map, params) = map::load(Path::new(&path))?;
    let mut current = state
        .current_map
        .lock()
        .map_err(|_| "マップを更新できません".to_string())?;
    let send_result = state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .apply_map(&map, params);
    *current = Some(map.clone());
    drop(current);
    if let Err(message) = send_result {
        let _ = app.emit("send-error", message);
    }

    let save_error = match state.settings.lock() {
        Ok(mut settings) => {
            settings.last_map_path = Some(path);
            settings.save(&app).err()
        }
        Err(_) => Some("設定を更新できません".to_string()),
    };
    if let Some(error) = save_error {
        let _ = app.emit("send-error", format!("設定を保存できませんでした: {error}"));
    }
    Ok(map)
}

#[tauri::command]
pub fn initial_map_path(state: State<'_, AppState>) -> Option<String> {
    if let Some(argument) = std::env::args().nth(1) {
        if Path::new(&argument).is_file() {
            return Some(argument);
        }
    }

    state
        .settings
        .lock()
        .ok()
        .and_then(|settings| settings.last_map_path.clone())
        .filter(|path| Path::new(path).is_file())
}

#[tauri::command]
pub fn get_settings(state: State<'_, AppState>) -> Settings {
    state
        .settings
        .lock()
        .map(|settings| settings.clone())
        .unwrap_or_default()
}

#[tauri::command]
pub fn save_settings(
    app: AppHandle,
    state: State<'_, AppState>,
    mut settings: Settings,
) -> Result<(), String> {
    let target = settings.validate()?;
    // NOTE: ポートを使っていたアプリを閉じた後、設定を保存し直すだけで受信を再開できるようにする。
    let retry = settings.receive
        && state
            .receive_status
            .lock()
            .map(|status| status.state == "failed")
            .unwrap_or(false);
    let changed = {
        let mut previous = state
            .settings
            .lock()
            .map_err(|_| "設定を更新できません".to_string())?;
        // NOTE: 前回のマップとフォルダは送り手が更新する。画面が古い値を持ったまま保存して巻き戻さないように、こちらの値を残す。
        settings.last_map_path = previous.last_map_path.clone();
        settings.last_clip_dir = previous.last_clip_dir.clone();
        settings.save(&app)?;
        let changed = previous.receive != settings.receive
            || previous.receive_port != settings.receive_port
            || retry;
        *previous = settings.clone();
        changed
    };
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .set_target(target, Duration::from_millis(settings.hold_ms));
    if changed {
        restart_receiver(&app, &state, &settings);
    }
    Ok(())
}

#[tauri::command]
pub fn set_slot(state: State<'_, AppState>, channel: u8, index: u8, value: u8) {
    if let Ok(mut scheduler) = state.scheduler.lock() {
        scheduler.set_slot(channel, index, value);
    }
}

#[tauri::command]
pub fn set_enabled(state: State<'_, AppState>, enabled: bool) -> Result<(), String> {
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .set_enabled(enabled)
}

#[tauri::command]
pub fn get_enabled(state: State<'_, AppState>) -> bool {
    state
        .scheduler
        .lock()
        .map(|scheduler| scheduler.enabled())
        .unwrap_or(true)
}

#[tauri::command]
pub fn set_lip_sync(state: State<'_, AppState>, enabled: bool) -> Result<(), String> {
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .set_lip_sync(enabled)
}

#[tauri::command]
pub fn get_lip_sync(state: State<'_, AppState>) -> bool {
    state
        .scheduler
        .lock()
        .map(|scheduler| scheduler.lip_sync())
        .unwrap_or(false)
}

#[tauri::command]
pub fn reset(state: State<'_, AppState>) -> Result<(), String> {
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .reset()
}

#[tauri::command]
pub fn expression_folder(app: AppHandle) -> Result<String, String> {
    let folder = app
        .path()
        .local_data_dir()
        .map_err(|error| error.to_string())?
        .join("Kaotsuki")
        .join("Expressions");
    std::fs::create_dir_all(&folder)
        .map_err(|error| format!("表情のフォルダを作れませんでした: {error}"))?;
    Ok(folder.to_string_lossy().into_owned())
}

#[tauri::command]
pub fn save_expression(
    state: State<'_, AppState>,
    path: String,
    saved_at: String,
) -> Result<usize, String> {
    let (map, values) = {
        let current = state
            .current_map
            .lock()
            .map_err(|_| "マップを取得できません".to_string())?;
        let map = current
            .as_ref()
            .ok_or_else(|| "マップを開いてください".to_string())?;
        let scheduler = state
            .scheduler
            .lock()
            .map_err(|_| "送り手の状態を取得できません".to_string())?;
        let values: std::collections::HashMap<(u8, u8), u8> = map
            .slots
            .iter()
            .filter_map(|slot| {
                scheduler
                    .desired(slot.channel, slot.index)
                    .map(|value| ((slot.channel, slot.index), value))
            })
            .collect();
        (map.clone(), values)
    };
    expression::save(Path::new(&path), &map, &saved_at, |channel, index| {
        values.get(&(channel, index)).copied()
    })
}

#[tauri::command]
pub fn apply_expression(
    app: AppHandle,
    state: State<'_, AppState>,
    path: String,
) -> Result<ApplyResult, String> {
    let path_ref = Path::new(&path);
    let is_clip = path_ref
        .extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| extension.eq_ignore_ascii_case("anim"));
    let (entries, animated) = if is_clip {
        let clip = clip::load(path_ref)?;
        (clip.entries, clip.animated)
    } else {
        (expression::load(path_ref)?, false)
    };
    let result = {
        let current = state
            .current_map
            .lock()
            .map_err(|_| "マップを取得できません".to_string())?;
        let map = current
            .as_ref()
            .ok_or_else(|| "マップを開いてください".to_string())?;
        let mut result = expression::apply(&entries, &map.slots);
        result.animated = animated;
        let mut scheduler = state
            .scheduler
            .lock()
            .map_err(|_| "送り手の状態を更新できません".to_string())?;
        for slot in &result.values {
            scheduler.set_slot(slot.channel, slot.index, slot.value);
        }
        result
    };
    if is_clip {
        remember_clip_dir(&app, &state, path_ref);
    }
    Ok(result)
}

/// 前回 AnimationClip を扱ったフォルダを覚える。失敗はイベントで知らせるだけにする。
fn remember_clip_dir(app: &AppHandle, state: &AppState, path: &Path) {
    let Some(parent) = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
    else {
        return;
    };
    let save_error = match state.settings.lock() {
        Ok(mut settings) => {
            settings.last_clip_dir = Some(parent.to_string_lossy().into_owned());
            settings.save(app).err()
        }
        Err(_) => Some("設定を更新できません".to_string()),
    };
    if let Some(error) = save_error {
        let _ = app.emit("send-error", format!("設定を保存できませんでした: {error}"));
    }
}

#[tauri::command]
pub fn clip_folder(state: State<'_, AppState>) -> Option<String> {
    state
        .settings
        .lock()
        .ok()
        .and_then(|settings| settings.last_clip_dir.clone())
        .filter(|path| Path::new(path).is_dir())
}

#[tauri::command]
pub fn save_clip(
    app: AppHandle,
    state: State<'_, AppState>,
    path: String,
) -> Result<usize, String> {
    let (map, values) = {
        let current = state
            .current_map
            .lock()
            .map_err(|_| "マップを取得できません".to_string())?;
        let map = current
            .as_ref()
            .ok_or_else(|| "マップを開いてください".to_string())?;
        let scheduler = state
            .scheduler
            .lock()
            .map_err(|_| "送り手の状態を取得できません".to_string())?;
        let values: std::collections::HashMap<(u8, u8), u8> = map
            .slots
            .iter()
            .filter_map(|slot| {
                scheduler
                    .desired(slot.channel, slot.index)
                    .map(|value| ((slot.channel, slot.index), value))
            })
            .collect();
        (map.clone(), values)
    };
    let path = Path::new(&path);
    let count = clip::save(path, &map, |channel, index| {
        values.get(&(channel, index)).copied()
    })?;
    remember_clip_dir(&app, &state, path);
    Ok(count)
}
