use crate::map::{ChannelAddresses, MapView, ParamNames};
use crate::osc::OscSender;
use std::net::SocketAddr;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter};

const TICK: Duration = Duration::from_millis(20);
const VALUE_INTERVAL: Duration = Duration::from_millis(50);
const ENABLED_ECHO_GUARD: Duration = Duration::from_millis(500);
// NOTE: VRChat が新しいアバターを読み込み終える前に送ると届かないため、少し待ってから ON にする。
const AVATAR_CHANGE_DELAY: Duration = Duration::from_millis(1500);
const AVATAR_CHANGE_GUARD: Duration = Duration::from_millis(2000);

// NOTE: 枠ごとに Index / Value が別なので、枠どうしは互いを待たずに送ってよい。
struct Channel {
    addresses: ChannelAddresses,
    active: [bool; 256],
    defaults: [u8; 256],
    desired: [u8; 256],
    sent: [u8; 256],
    current: u8,
    last_send: Option<Instant>,
}

impl Channel {
    fn new(addresses: ChannelAddresses) -> Self {
        Self {
            addresses,
            active: [false; 256],
            defaults: [0; 256],
            desired: [0; 256],
            sent: [0; 256],
            current: 0,
            last_send: None,
        }
    }

    fn tick(
        &mut self,
        osc: &OscSender,
        target: SocketAddr,
        hold: Duration,
        now: Instant,
    ) -> Option<String> {
        let elapsed = self
            .last_send
            .map(|last| now.saturating_duration_since(last))
            .unwrap_or(Duration::MAX);
        let current = self.current as usize;
        if current != 0 && self.desired[current] != self.sent[current] {
            if elapsed >= VALUE_INTERVAL {
                // NOTE: アバター側の Index はメニュー操作や再読み込みで 0 に戻ることがあるため、同じスロットでも毎回 Index から送り直す。
                return self.send_slot(osc, target, current, now);
            }
            // NOTE: 同じスロットの更新中は、保持時間が過ぎても別スロットへ切り替えない。
            return None;
        }

        if elapsed < hold {
            return None;
        }

        let next = (1..=255)
            .map(|offset| ((usize::from(self.current) + offset - 1) % 255) + 1)
            .find(|&index| self.active[index] && self.desired[index] != self.sent[index]);
        let index = next?;

        // NOTE: Value を先に送ると切り替え前のスロットへ値が残るため、必ず Index → Value の順に送る。
        self.send_slot(osc, target, index, now)
    }

    fn send_slot(
        &mut self,
        osc: &OscSender,
        target: SocketAddr,
        index: usize,
        now: Instant,
    ) -> Option<String> {
        let index_result = osc.send_int(target, &self.addresses.index, index as i32);
        let value_result = osc.send_int(
            target,
            &self.addresses.value,
            i32::from(self.desired[index]),
        );
        self.current = index as u8;
        self.sent[index] = self.desired[index];
        self.last_send = Some(now);
        index_result.err().or_else(|| value_result.err())
    }

    fn reset_state(&mut self, now: Instant) {
        self.desired = self.defaults;
        self.sent = self.defaults;
        self.current = 0;
        self.last_send = Some(now);
    }
}

pub struct Scheduler {
    osc: OscSender,
    target: SocketAddr,
    hold: Duration,
    enabled_address: Option<String>,
    lip_sync_address: Option<String>,
    lip_sync: bool,
    channels: Vec<Channel>,
    pending_enable_at: Option<Instant>,
    enabled: bool,
    ignore_enabled_until: Option<Instant>,
    last_error: Option<(String, Instant)>,
}

impl Scheduler {
    pub fn new(osc: OscSender, target: SocketAddr, hold: Duration) -> Self {
        Self {
            osc,
            target,
            hold,
            enabled_address: None,
            lip_sync_address: None,
            lip_sync: false,
            channels: Vec::new(),
            pending_enable_at: None,
            enabled: true,
            ignore_enabled_until: None,
            last_error: None,
        }
    }

    pub fn apply_map(&mut self, map: &MapView, params: ParamNames) -> Result<(), String> {
        self.enabled_address = Some(params.enabled);
        self.lip_sync_address = params.lip_sync;
        // NOTE: LipSync は保存されないので、アバターを読み込むと false から始まる。送り手の表示もそれに合わせる。
        self.lip_sync = false;
        self.channels = params.channels.into_iter().map(Channel::new).collect();
        for slot in &map.slots {
            if slot.channel == 0 {
                continue;
            }
            let Some(channel) = self.channels.get_mut(usize::from(slot.channel - 1)) else {
                continue;
            };
            let index = slot.index as usize;
            channel.active[index] = true;
            channel.defaults[index] = slot.default_value;
        }
        for channel in &mut self.channels {
            channel.desired = channel.defaults;
            channel.sent = channel.defaults;
        }
        if self.enabled && self.pending_enable_at.is_none() {
            let now = Instant::now();
            self.ignore_enabled_until = Some(now + ENABLED_ECHO_GUARD);
            if let Some(address) = &self.enabled_address {
                return self.osc.send_bool(self.target, address, true);
            }
        }
        Ok(())
    }

    pub fn set_slot(&mut self, channel: u8, index: u8, value: u8) {
        if channel == 0 {
            return;
        }
        let Some(channel) = self.channels.get_mut(usize::from(channel - 1)) else {
            return;
        };
        let index = index as usize;
        if channel.active[index] {
            channel.desired[index] = value;
        }
    }

    pub fn enabled(&self) -> bool {
        self.enabled
    }

    pub fn lip_sync(&self) -> bool {
        self.lip_sync
    }

    pub fn set_lip_sync(&mut self, value: bool) -> Result<(), String> {
        let Some(address) = self.lip_sync_address.as_ref() else {
            return Ok(());
        };
        self.lip_sync = value;
        self.ignore_enabled_until = Some(Instant::now() + ENABLED_ECHO_GUARD);
        self.osc.send_bool(self.target, address, value)
    }

    pub fn receive_lip_sync(&mut self, address: &str, value: bool, now: Instant) -> Option<bool> {
        if self.lip_sync_address.as_deref() != Some(address)
            || self.ignore_enabled_until.is_some_and(|until| now < until)
            || value == self.lip_sync
        {
            return None;
        }
        self.lip_sync = value;
        Some(value)
    }

    pub fn desired(&self, channel: u8, index: u8) -> Option<u8> {
        let channel = self.channels.get(usize::from(channel.checked_sub(1)?))?;
        let index = usize::from(index);
        channel.active[index].then_some(channel.desired[index])
    }

    pub fn set_enabled(&mut self, enabled: bool) -> Result<(), String> {
        let now = Instant::now();
        self.enabled = enabled;
        self.pending_enable_at = None;
        // NOTE: アバター側は OFF で既定値に戻るので送信済みの値だけ揃え、スライダーの位置（desired）は残す。ON に戻すと作りかけの表情が送り直される。
        if !enabled {
            for channel in &mut self.channels {
                channel.sent = channel.defaults;
                channel.current = 0;
                channel.last_send = Some(now);
            }
        }
        let Some(address) = self.enabled_address.as_ref() else {
            return Ok(());
        };
        self.ignore_enabled_until = Some(now + ENABLED_ECHO_GUARD);
        self.osc.send_bool(self.target, address, enabled)
    }

    pub fn reset(&mut self) -> Result<(), String> {
        let now = Instant::now();
        self.reset_channels(now);
        if !self.enabled {
            return Ok(());
        }
        let Some(address) = self.enabled_address.as_ref() else {
            return Ok(());
        };
        // NOTE: スロットを 1 本ずつ送り直すより、アバター側の OFF 時のリセットを使うほうが一度で既定値に戻る。
        let result = self.osc.send_bool(self.target, address, false);
        self.pending_enable_at = Some(now + self.hold);
        self.ignore_enabled_until = Some(now + self.hold + ENABLED_ECHO_GUARD);
        result
    }

    pub fn set_target(&mut self, target: SocketAddr, hold: Duration) {
        self.target = target;
        self.hold = hold;
    }

    // NOTE: Enabled は保存されないので、アバターを読み込むと OFF・既定値から始まる。トグルが ON なら送り手が ON に戻す。
    pub fn avatar_changed(&mut self, now: Instant) {
        self.lip_sync = false;
        self.reset_channels(now);
        self.pending_enable_at = self.enabled.then_some(now + AVATAR_CHANGE_DELAY);
        self.ignore_enabled_until = Some(now + AVATAR_CHANGE_GUARD);
    }

    pub fn receive_enabled(&mut self, address: &str, value: bool, now: Instant) -> Option<bool> {
        if self.enabled_address.as_deref() != Some(address) {
            return None;
        }
        // NOTE: 自分が送った Enabled の折り返しや、アバター読み込み直後の OFF を、利用者の操作と取り違えないため。
        if self.ignore_enabled_until.is_some_and(|until| now < until) || value == self.enabled {
            return None;
        }
        self.enabled = value;
        if !value {
            self.pending_enable_at = None;
            for channel in &mut self.channels {
                channel.sent = channel.defaults;
                channel.current = 0;
                channel.last_send = Some(now);
            }
        }
        Some(value)
    }

    pub fn tick(&mut self, now: Instant) -> Option<String> {
        let enabled_address = self.enabled_address.as_ref()?;

        if let Some(enable_at) = self.pending_enable_at {
            if now >= enable_at {
                let result = self.osc.send_bool(self.target, enabled_address, true);
                self.pending_enable_at = None;
                self.ignore_enabled_until = Some(now + ENABLED_ECHO_GUARD);
                for channel in &mut self.channels {
                    channel.last_send = Some(now);
                }
                return result.err();
            }
            return None;
        }

        // NOTE: OFF の間はスライダーを動かしても送らず、ON にしたときにまとめて送る。
        if !self.enabled {
            return None;
        }

        let mut first_error = None;
        for channel in &mut self.channels {
            if let Some(error) = channel.tick(&self.osc, self.target, self.hold, now) {
                if first_error.is_none() {
                    first_error = Some(error);
                }
            }
        }
        first_error
    }

    fn reset_channels(&mut self, now: Instant) {
        for channel in &mut self.channels {
            channel.reset_state(now);
        }
    }

    fn should_emit_error(&mut self, message: &str, now: Instant) -> bool {
        let should_emit = self.last_error.as_ref().is_none_or(|(last, at)| {
            last != message || now.saturating_duration_since(*at) >= Duration::from_secs(1)
        });
        if should_emit {
            self.last_error = Some((message.to_string(), now));
        }
        should_emit
    }
}

pub fn start(app: AppHandle, shared: Arc<Mutex<Scheduler>>) {
    std::thread::spawn(move || loop {
        std::thread::sleep(TICK);
        let now = Instant::now();
        let error = {
            let Ok(mut scheduler) = shared.lock() else {
                return;
            };
            scheduler
                .tick(now)
                .filter(|message| scheduler.should_emit_error(message, now))
        };
        if let Some(message) = error {
            let _ = app.emit("send-error", message);
        }
    });
}

#[cfg(test)]
mod tests {
    use super::Scheduler;
    use crate::map::{ChannelAddresses, MapView, ParamNames, SlotView};
    use crate::osc::OscSender;
    use rosc::{OscPacket, OscType};
    use std::net::UdpSocket;
    use std::time::{Duration, Instant};

    fn setup() -> (Scheduler, UdpSocket, Instant) {
        let receiver = UdpSocket::bind("127.0.0.1:0").expect("bind receiver");
        receiver
            .set_read_timeout(Some(Duration::from_millis(30)))
            .expect("set timeout");
        let target = receiver.local_addr().expect("receiver address");
        let mut scheduler = Scheduler::new(
            OscSender::bind("127.0.0.1:0").expect("bind sender"),
            target,
            Duration::from_millis(250),
        );
        scheduler
            .apply_map(
                &MapView {
                    path: String::new(),
                    avatar_name: "Test".to_string(),
                    map_name: "Test".to_string(),
                    avatars: Vec::new(),
                    generated_at: String::new(),
                    channel_count: 1,
                    has_lip_sync: false,
                    slots: vec![slot(1, 3), slot(1, 7)],
                },
                ParamNames {
                    enabled: "/enabled".to_string(),
                    lip_sync: None,
                    channels: vec![addresses("/index", "/value")],
                },
            )
            .expect("apply map");
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(true))
        );
        (scheduler, receiver, Instant::now())
    }

    fn addresses(index: &str, value: &str) -> ChannelAddresses {
        ChannelAddresses {
            index: index.to_string(),
            value: value.to_string(),
        }
    }

    fn slot(channel: u8, index: u8) -> SlotView {
        SlotView {
            channel,
            index,
            mesh: "Body".to_string(),
            blend_shape: format!("shape-{channel}-{index}"),
            default_weight: 0.0,
            default_value: 0,
            group: String::new(),
        }
    }

    fn receive(socket: &UdpSocket) -> (String, OscType) {
        let mut buffer = [0u8; 1024];
        let size = socket.recv(&mut buffer).expect("receive packet");
        let (_, packet) = rosc::decoder::decode_udp(&buffer[..size]).expect("decode packet");
        let OscPacket::Message(message) = packet else {
            panic!("expected OSC message");
        };
        (
            message.addr,
            message.args.into_iter().next().expect("argument"),
        )
    }

    fn assert_no_packet(socket: &UdpSocket) {
        let mut buffer = [0u8; 64];
        assert!(socket.recv(&mut buffer).is_err());
    }

    #[test]
    fn map_load_sends_only_enabled_true() {
        let (_, receiver, _) = setup();
        assert_no_packet(&receiver);
    }

    #[test]
    fn changed_slots_are_sent_in_index_value_order() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_slot(1, 3, 31);
        scheduler.set_slot(1, 7, 71);
        assert_eq!(scheduler.tick(start), None);
        assert_eq!(receive(&receiver), ("/index".to_string(), OscType::Int(3)));
        assert_eq!(receive(&receiver), ("/value".to_string(), OscType::Int(31)));

        scheduler.tick(start + Duration::from_millis(249));
        assert_no_packet(&receiver);
        scheduler.tick(start + Duration::from_millis(250));
        assert_eq!(receive(&receiver), ("/index".to_string(), OscType::Int(7)));
        assert_eq!(receive(&receiver), ("/value".to_string(), OscType::Int(71)));
    }

    #[test]
    fn same_slot_resends_index_then_value() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_slot(1, 3, 31);
        scheduler.tick(start);
        receive(&receiver);
        receive(&receiver);

        scheduler.set_slot(1, 3, 32);
        scheduler.tick(start + Duration::from_millis(49));
        assert_no_packet(&receiver);
        scheduler.tick(start + Duration::from_millis(50));
        assert_eq!(receive(&receiver), ("/index".to_string(), OscType::Int(3)));
        assert_eq!(receive(&receiver), ("/value".to_string(), OscType::Int(32)));
        assert_no_packet(&receiver);
    }

    #[test]
    fn reset_disables_waits_and_enables() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.reset().expect("reset");
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(false))
        );
        scheduler.set_slot(1, 3, 31);
        scheduler.tick(start + Duration::from_millis(100));
        assert_no_packet(&receiver);
        scheduler.tick(start + Duration::from_millis(300));
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(true))
        );
        assert_no_packet(&receiver);
    }

    #[test]
    fn channels_send_independently() {
        let receiver = UdpSocket::bind("127.0.0.1:0").expect("bind receiver");
        receiver
            .set_read_timeout(Some(Duration::from_millis(30)))
            .expect("set timeout");
        let target = receiver.local_addr().expect("receiver address");
        let mut scheduler = Scheduler::new(
            OscSender::bind("127.0.0.1:0").expect("bind sender"),
            target,
            Duration::from_millis(250),
        );
        scheduler
            .apply_map(
                &MapView {
                    path: String::new(),
                    avatar_name: "Test".to_string(),
                    generated_at: String::new(),
                    channel_count: 2,
                    has_lip_sync: false,
                    map_name: "Test".to_string(),
                    avatars: Vec::new(),
                    slots: vec![slot(1, 3), slot(2, 1)],
                },
                ParamNames {
                    enabled: "/enabled".to_string(),
                    lip_sync: None,
                    channels: vec![
                        addresses("/index", "/value"),
                        addresses("/index2", "/value2"),
                    ],
                },
            )
            .expect("apply map");
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(true))
        );

        scheduler.set_slot(1, 3, 31);
        scheduler.set_slot(2, 1, 21);
        assert_eq!(scheduler.tick(Instant::now()), None);
        assert_eq!(receive(&receiver), ("/index".to_string(), OscType::Int(3)));
        assert_eq!(receive(&receiver), ("/value".to_string(), OscType::Int(31)));
        assert_eq!(receive(&receiver), ("/index2".to_string(), OscType::Int(1)));
        assert_eq!(
            receive(&receiver),
            ("/value2".to_string(), OscType::Int(21))
        );
    }

    #[test]
    fn slot_outside_channel_is_ignored() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_slot(2, 1, 10);
        scheduler.tick(start);
        assert_no_packet(&receiver);
    }

    #[test]
    fn off_keeps_desired_and_resends_on_enable() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_slot(1, 3, 31);
        scheduler.set_enabled(false).expect("disable");
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(false))
        );
        scheduler.tick(start + Duration::from_millis(300));
        assert_no_packet(&receiver);
        scheduler.set_enabled(true).expect("enable");
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(true))
        );
        scheduler.tick(start + Duration::from_millis(600));
        assert_eq!(receive(&receiver), ("/index".to_string(), OscType::Int(3)));
        assert_eq!(receive(&receiver), ("/value".to_string(), OscType::Int(31)));
    }

    #[test]
    fn slots_changed_while_off_are_not_sent() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_enabled(false).expect("disable");
        receive(&receiver);
        scheduler.set_slot(1, 7, 71);
        scheduler.tick(start + Duration::from_secs(1));
        assert_no_packet(&receiver);
    }

    #[test]
    fn reset_while_off_sends_nothing() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_enabled(false).expect("disable");
        receive(&receiver);
        scheduler.reset().expect("reset");
        scheduler.tick(start + Duration::from_secs(1));
        assert_no_packet(&receiver);
        assert!(!scheduler.enabled());
    }

    #[test]
    fn avatar_change_enables_after_delay() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_slot(1, 3, 31);
        scheduler.avatar_changed(start);
        scheduler.tick(start + Duration::from_millis(1499));
        assert_no_packet(&receiver);
        scheduler.tick(start + Duration::from_millis(1500));
        assert_eq!(
            receive(&receiver),
            ("/enabled".to_string(), OscType::Bool(true))
        );
        scheduler.tick(start + Duration::from_millis(2000));
        assert_no_packet(&receiver);
    }

    #[test]
    fn received_enabled_false_is_mirrored() {
        let (mut scheduler, receiver, start) = setup();
        assert_eq!(
            scheduler.receive_enabled("/enabled", false, start + Duration::from_secs(10)),
            Some(false)
        );
        assert!(!scheduler.enabled());
        scheduler.set_slot(1, 3, 31);
        scheduler.tick(start + Duration::from_secs(11));
        assert_no_packet(&receiver);
    }

    #[test]
    fn received_enabled_is_ignored_during_guard() {
        let (mut scheduler, receiver, _) = setup();
        scheduler.set_enabled(true).expect("enable");
        receive(&receiver);
        assert_eq!(
            scheduler.receive_enabled(
                "/enabled",
                false,
                Instant::now() + Duration::from_millis(100)
            ),
            None
        );
    }

    #[test]
    fn received_enabled_with_other_address_is_ignored() {
        let (mut scheduler, _, start) = setup();
        assert_eq!(
            scheduler.receive_enabled("/other", false, start + Duration::from_secs(10)),
            None
        );
    }

    #[test]
    fn switching_maps_keeps_avatar_change_delay() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.avatar_changed(start);
        let map = MapView {
            path: String::new(),
            avatar_name: "Other".to_string(),
            map_name: "Shared".to_string(),
            avatars: Vec::new(),
            generated_at: String::new(),
            channel_count: 1,
            has_lip_sync: false,
            slots: vec![slot(1, 7)],
        };
        scheduler
            .apply_map(
                &map,
                ParamNames {
                    enabled: "/new-enabled".to_string(),
                    lip_sync: None,
                    channels: vec![addresses("/index", "/value")],
                },
            )
            .expect("apply map");
        assert_no_packet(&receiver);
        scheduler.tick(start + Duration::from_millis(1500));
        assert_eq!(
            receive(&receiver),
            ("/new-enabled".to_string(), OscType::Bool(true))
        );
    }

    fn apply_lip_sync_map(scheduler: &mut Scheduler, address: Option<&str>) {
        scheduler
            .apply_map(
                &MapView {
                    path: String::new(),
                    avatar_name: "Test".into(),
                    map_name: "Test".into(),
                    avatars: Vec::new(),
                    generated_at: String::new(),
                    channel_count: 1,
                    has_lip_sync: address.is_some(),
                    slots: vec![slot(1, 3)],
                },
                ParamNames {
                    enabled: "/enabled".into(),
                    lip_sync: address.map(str::to_string),
                    channels: vec![addresses("/index", "/value")],
                },
            )
            .expect("apply map");
    }

    #[test]
    fn lip_sync_is_sent() {
        let (mut scheduler, receiver, _) = setup();
        assert!(!scheduler.lip_sync());
        apply_lip_sync_map(&mut scheduler, Some("/lipsync"));
        assert_eq!(receive(&receiver), ("/enabled".into(), OscType::Bool(true)));
        assert_no_packet(&receiver);
        scheduler.set_lip_sync(true).expect("lip sync");
        assert_eq!(receive(&receiver), ("/lipsync".into(), OscType::Bool(true)));
        assert!(scheduler.lip_sync());
        apply_lip_sync_map(&mut scheduler, Some("/lipsync"));
        assert!(!scheduler.lip_sync());
        assert_eq!(receive(&receiver), ("/enabled".into(), OscType::Bool(true)));
        assert_no_packet(&receiver);
        scheduler.set_enabled(false).expect("disable");
        receive(&receiver);
        scheduler.set_lip_sync(true).expect("lip sync while off");
        assert_eq!(receive(&receiver), ("/lipsync".into(), OscType::Bool(true)));
        assert!(!scheduler.enabled());
        scheduler.avatar_changed(Instant::now());
        assert!(!scheduler.lip_sync());
        assert_no_packet(&receiver);
    }

    #[test]
    fn lip_sync_without_address_sends_nothing() {
        let (mut scheduler, receiver, start) = setup();
        scheduler.set_lip_sync(true).expect("legacy lip sync");
        assert!(!scheduler.lip_sync());
        assert_eq!(
            scheduler.receive_lip_sync("/lipsync", true, start + Duration::from_secs(10)),
            None
        );
        assert_no_packet(&receiver);
    }

    #[test]
    fn received_lip_sync_is_mirrored() {
        let (mut scheduler, receiver, start) = setup();
        apply_lip_sync_map(&mut scheduler, Some("/lipsync"));
        receive(&receiver);
        assert_eq!(
            scheduler.receive_lip_sync("/other", true, start + Duration::from_secs(10)),
            None
        );
        assert_eq!(
            scheduler.receive_lip_sync("/lipsync", true, start + Duration::from_secs(10)),
            Some(true)
        );
        assert!(scheduler.lip_sync());
        assert_eq!(
            scheduler.receive_lip_sync("/lipsync", true, start + Duration::from_secs(10)),
            None
        );
        assert_no_packet(&receiver);
        scheduler.set_lip_sync(false).expect("lip sync");
        receive(&receiver);
        assert_eq!(
            scheduler.receive_lip_sync(
                "/lipsync",
                true,
                Instant::now() + Duration::from_millis(100)
            ),
            None
        );
        assert_eq!(
            scheduler.receive_enabled(
                "/enabled",
                false,
                Instant::now() + Duration::from_millis(100)
            ),
            None
        );
        assert!(!scheduler.lip_sync());
    }
}
