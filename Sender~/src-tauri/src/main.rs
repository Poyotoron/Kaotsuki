#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod expression;
mod map;
mod osc;
mod receiver;
mod scheduler;
mod settings;

use commands::AppState;
use osc::OscSender;
use scheduler::Scheduler;
use settings::Settings;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::Manager;

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let handle = app.handle().clone();
            let mut settings = Settings::load(&handle);
            let target = match settings.validate() {
                Ok(target) => target,
                Err(_) => {
                    settings = Settings::default();
                    settings.validate().expect("default settings must be valid")
                }
            };
            let scheduler = Arc::new(Mutex::new(Scheduler::new(
                OscSender::new()?,
                target,
                Duration::from_millis(settings.hold_ms),
            )));
            app.manage(AppState {
                scheduler: Arc::clone(&scheduler),
                settings: Mutex::new(settings.clone()),
                receiver: Mutex::new(None),
                current_map: Mutex::new(None),
                receive_status: Mutex::new(receiver::ReceiveStatus {
                    state: "disabled",
                    port: settings.receive_port,
                }),
            });
            commands::restart_receiver(&handle, &app.state::<AppState>(), &settings);
            scheduler::start(handle, scheduler);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::list_maps,
            commands::load_map,
            commands::initial_map_path,
            commands::get_settings,
            commands::save_settings,
            commands::set_slot,
            commands::set_enabled,
            commands::get_enabled,
            commands::get_receive_status,
            commands::expression_folder,
            commands::save_expression,
            commands::apply_expression,
            commands::reset,
        ])
        .run(tauri::generate_context!())
        .expect("Kaotsuki Sender の起動に失敗しました");
}
