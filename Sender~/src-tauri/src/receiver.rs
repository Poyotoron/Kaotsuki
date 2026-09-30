use crate::{commands, map, scheduler::Scheduler};
use rosc::{OscPacket, OscType};
use serde::Serialize;
use std::io::ErrorKind;
use std::net::UdpSocket;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter};

const RECEIVE_ERROR_WAIT: Duration = Duration::from_millis(50);

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ReceiveStatus {
    pub state: &'static str,
    pub port: u16,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct AvatarChanged {
    avatar_id: String,
    map_path: Option<String>,
}

pub struct Receiver {
    stop: Arc<AtomicBool>,
    thread: Option<JoinHandle<()>>,
}

impl Receiver {
    /// 127.0.0.1:<port> に bind して受信スレッドを始める。
    pub fn start(
        app: AppHandle,
        scheduler: Arc<Mutex<Scheduler>>,
        port: u16,
    ) -> Result<Self, String> {
        // NOTE: VRChat の出力先は既定でローカル。0.0.0.0 にするとファイアウォールの確認が出て、外部からの入力も受けてしまう。
        let socket = UdpSocket::bind(("127.0.0.1", port))
            .map_err(|error| format!("受信を開始できませんでした: {error}"))?;
        socket
            .set_read_timeout(Some(Duration::from_millis(200)))
            .map_err(|error| format!("受信を開始できませんでした: {error}"))?;
        let stop = Arc::new(AtomicBool::new(false));
        let thread_stop = Arc::clone(&stop);
        let thread = std::thread::Builder::new()
            .name("kaotsuki-receiver".to_string())
            .spawn(move || {
                let mut buffer = [0u8; 65535];
                while !thread_stop.load(Ordering::Relaxed) {
                    // NOTE: タイムアウトのほか、Windows では UDP の相手が閉じたときに接続リセットも返るため、次のパケットを待ち続ける。
                    // NOTE: タイムアウト以外のエラーが続いても CPU を使い切らないように、少し待ってから次を待つ。
                    let (size, _) = match socket.recv_from(&mut buffer) {
                        Ok(received) => received,
                        Err(error)
                            if matches!(
                                error.kind(),
                                ErrorKind::WouldBlock | ErrorKind::TimedOut
                            ) =>
                        {
                            continue;
                        }
                        Err(_) => {
                            std::thread::sleep(RECEIVE_ERROR_WAIT);
                            continue;
                        }
                    };
                    let Ok((_, packet)) = rosc::decoder::decode_udp(&buffer[..size]) else {
                        continue;
                    };
                    let mut incoming = Vec::new();
                    collect(packet, &mut incoming);
                    for message in incoming {
                        match message {
                            Incoming::AvatarChange(id) => {
                                if let Ok(mut scheduler) = scheduler.lock() {
                                    scheduler.avatar_changed(Instant::now());
                                }
                                let map_path = commands::map_folder(&app)
                                    .ok()
                                    .and_then(|folder| map::find_by_avatar(&folder, &id));
                                let _ = app.emit(
                                    "avatar-changed",
                                    AvatarChanged {
                                        avatar_id: id,
                                        map_path,
                                    },
                                );
                            }
                            Incoming::Bool { address, value } => {
                                let changed = scheduler.lock().ok().and_then(|mut scheduler| {
                                    scheduler.receive_enabled(&address, value, Instant::now())
                                });
                                if let Some(enabled) = changed {
                                    let _ = app.emit("enabled-changed", enabled);
                                }
                            }
                        }
                    }
                }
            })
            .map_err(|error| format!("受信を開始できませんでした: {error}"))?;
        Ok(Self {
            stop,
            thread: Some(thread),
        })
    }

    /// 停止を要求し、スレッドの終了を待つ。
    pub fn stop(mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

#[derive(Debug, PartialEq)]
enum Incoming {
    AvatarChange(String),
    Bool { address: String, value: bool },
}

/// パケットから扱うメッセージだけを取り出す。
fn collect(packet: OscPacket, out: &mut Vec<Incoming>) {
    match packet {
        OscPacket::Message(message) => {
            if message.addr == "/avatar/change" {
                if let Some(OscType::String(id)) = message.args.first() {
                    out.push(Incoming::AvatarChange(id.clone()));
                }
            } else if let Some(OscType::Bool(value)) = message.args.first() {
                out.push(Incoming::Bool {
                    address: message.addr,
                    value: *value,
                });
            }
        }
        OscPacket::Bundle(bundle) => {
            for packet in bundle.content {
                collect(packet, out);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{collect, Incoming};
    use rosc::{OscBundle, OscMessage, OscPacket, OscType};

    fn message(address: &str, value: OscType) -> OscPacket {
        OscPacket::Message(OscMessage {
            addr: address.to_string(),
            args: vec![value],
        })
    }

    #[test]
    fn collect_reads_avatar_change_and_bool_in_bundle() {
        let packet = OscPacket::Bundle(OscBundle {
            timetag: (0, 1).into(),
            content: vec![
                message("/avatar/change", OscType::String("avtr_a".to_string())),
                OscPacket::Bundle(OscBundle {
                    timetag: (0, 1).into(),
                    content: vec![message(
                        "/avatar/parameters/Kaotsuki/Enabled",
                        OscType::Bool(true),
                    )],
                }),
                message("/avatar/parameters/Other", OscType::Float(1.0)),
            ],
        });
        let mut incoming = Vec::new();
        collect(packet, &mut incoming);
        assert_eq!(
            incoming,
            vec![
                Incoming::AvatarChange("avtr_a".to_string()),
                Incoming::Bool {
                    address: "/avatar/parameters/Kaotsuki/Enabled".to_string(),
                    value: true
                }
            ]
        );
    }
}
