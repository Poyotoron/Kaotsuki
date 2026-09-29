use crate::map::{self, MapEntry, MapView};
use crate::scheduler::Scheduler;
use crate::settings::Settings;
use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager, State};

pub struct AppState {
    pub scheduler: Arc<Mutex<Scheduler>>,
    pub settings: Mutex<Settings>,
}

#[tauri::command]
pub fn list_maps(app: AppHandle) -> Result<Vec<MapEntry>, String> {
    let folder = app
        .path()
        .local_data_dir()
        .map_err(|error| error.to_string())?
        .join("Kaotsuki")
        .join("Maps");
    Ok(map::list(&folder))
}

#[tauri::command]
pub fn load_map(
    app: AppHandle,
    state: State<'_, AppState>,
    path: String,
) -> Result<MapView, String> {
    let (map, params) = map::load(Path::new(&path))?;
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .apply_map(&map, params);

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
    settings: Settings,
) -> Result<(), String> {
    let target = settings.validate()?;
    settings.save(&app)?;
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .set_target(target, Duration::from_millis(settings.hold_ms));
    *state
        .settings
        .lock()
        .map_err(|_| "設定を更新できません".to_string())? = settings;
    Ok(())
}

#[tauri::command]
pub fn set_slot(state: State<'_, AppState>, channel: u8, index: u8, value: u8) {
    if let Ok(mut scheduler) = state.scheduler.lock() {
        scheduler.set_slot(channel, index, value);
    }
}

#[tauri::command]
pub fn send_enabled(state: State<'_, AppState>, enabled: bool) -> Result<(), String> {
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .send_enabled(enabled)
}

#[tauri::command]
pub fn reset(state: State<'_, AppState>) -> Result<(), String> {
    state
        .scheduler
        .lock()
        .map_err(|_| "送り手の状態を更新できません".to_string())?
        .reset()
}
