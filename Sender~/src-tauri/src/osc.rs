use rosc::{OscMessage, OscPacket, OscType};
use std::net::{SocketAddr, UdpSocket};

pub struct OscSender {
    socket: UdpSocket,
}

impl OscSender {
    pub fn new() -> std::io::Result<Self> {
        Self::bind("0.0.0.0:0")
    }

    #[cfg(test)]
    pub fn bind(address: &str) -> std::io::Result<Self> {
        Ok(Self {
            socket: UdpSocket::bind(address)?,
        })
    }

    #[cfg(not(test))]
    fn bind(address: &str) -> std::io::Result<Self> {
        Ok(Self {
            socket: UdpSocket::bind(address)?,
        })
    }

    pub fn send_int(&self, target: SocketAddr, address: &str, value: i32) -> Result<(), String> {
        self.send(target, address, OscType::Int(value))
    }

    pub fn send_bool(&self, target: SocketAddr, address: &str, value: bool) -> Result<(), String> {
        self.send(target, address, OscType::Bool(value))
    }

    fn send(&self, target: SocketAddr, address: &str, value: OscType) -> Result<(), String> {
        let packet = OscPacket::Message(OscMessage {
            addr: address.to_string(),
            args: vec![value],
        });
        let bytes = rosc::encoder::encode(&packet).map_err(|error| error.to_string())?;
        self.socket
            .send_to(&bytes, target)
            .map(|_| ())
            .map_err(|error| error.to_string())
    }
}
