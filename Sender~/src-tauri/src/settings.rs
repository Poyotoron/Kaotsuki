use serde::{Deserialize, Serialize};
use std::fs;
use std::net::{SocketAddr, ToSocketAddrs};
use std::path::PathBuf;
use tauri::{AppHandle, Manager};

#[derive(Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Settings {
    pub host: String,
    pub port: u16,
    pub hold_ms: u64,
    pub last_map_path: Option<String>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            host: "127.0.0.1".to_string(),
            port: 9000,
            hold_ms: 250,
            last_map_path: None,
        }
    }
}

impl Settings {
    pub fn load(app: &AppHandle) -> Self {
        let Ok(path) = settings_path(app) else {
            return Self::default();
        };
        let Ok(content) = fs::read_to_string(path) else {
            return Self::default();
        };
        serde_json::from_str(&content).unwrap_or_default()
    }

    pub fn save(&self, app: &AppHandle) -> Result<(), String> {
        let path = settings_path(app)?;
        let parent = path
            .parent()
            .ok_or_else(|| "設定の保存先を取得できません".to_string())?;
        fs::create_dir_all(parent).map_err(|error| error.to_string())?;
        let content = serde_json::to_string_pretty(self).map_err(|error| error.to_string())?;
        fs::write(path, content).map_err(|error| error.to_string())
    }

    pub fn validate(&self) -> Result<SocketAddr, String> {
        if self.port == 0 {
            return Err("ポート番号は 1〜65535 で指定してください".to_string());
        }
        if !(100..=1000).contains(&self.hold_ms) {
            return Err("保持時間は 100〜1000 ミリ秒で指定してください".to_string());
        }

        (self.host.as_str(), self.port)
            .to_socket_addrs()
            .map_err(|_| format!("送信先を解決できません: {}", self.host))?
            .find(SocketAddr::is_ipv4)
            .ok_or_else(|| format!("送信先を解決できません: {}", self.host))
    }
}

fn settings_path(app: &AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|path| path.join("settings.json"))
        .map_err(|error| error.to_string())
}
