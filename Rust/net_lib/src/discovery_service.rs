use crate::discovery_ws::web_service;
use crate::msg_serde::*;
use async_std::net::SocketAddr;
use async_std::net::{TcpListener, TcpStream};
use async_std::prelude::*;
use async_std::sync::{Arc, Mutex};
use async_std::task::sleep;
use futures::executor::ThreadPool;
use futures::task::SpawnExt;
use log::*;
use rand::rngs::SmallRng;
use rand::{Rng, SeedableRng};
use std::collections::BTreeSet;
use urlparse::Url;

pub struct DiscoveryServer {
    stream: TcpStream,
}

impl DiscoveryServer {
    fn new(stream: TcpStream) -> Self {
        Self { stream }
    }

    async fn on_connect(
        &self,
        msg: DiscoveryMessage,
        rng: &mut SmallRng,
    ) -> Result<(), Box<dyn std::error::Error>> {
        match msg.uri.port {
            None => {
                let n = rng.gen_range(1024, std::u16::MAX);
                let new_address = Url::parse(&format!(
                    "{}://{}:{}{}",
                    msg.uri.scheme, msg.uri.netloc, n, msg.uri.path
                ));
                write_msg(
                    self.stream.clone(),
                    DiscoveryMessage {
                        state: DiscoveryState::ConnectResponse,
                        uri: new_address,
                        channels: vec![],
                    },
                )
                .await?;
            }
            Some(_) => {
                write_msg(
                    self.stream.clone(),
                    DiscoveryMessage {
                        state: DiscoveryState::ConnectResponse,
                        uri: msg.uri,
                        channels: vec![],
                    },
                )
                .await?;
            }
        }
        Ok(())
    }

    async fn on_connect_response(
        &self,
        _: DiscoveryMessage,
    ) -> Result<(), Box<dyn std::error::Error>> {
        Ok(())
    }

    async fn on_queue_data(
        &self,
        msg: DiscoveryMessage,
        channels: Arc<Mutex<BTreeSet<Arc<Channel>>>>,
        local_channels: &mut BTreeSet<Arc<Channel>>,
    ) -> Result<(), Box<dyn std::error::Error>> {
        loop {
            for ch in msg.channels.iter() {
                local_channels.insert(ch.clone());
            }
            if let Some(mut channels) = channels.try_lock() {
                for ch in local_channels.iter() {
                    channels.insert(ch.clone());
                }

                write_msg(
                    self.stream.clone(),
                    DiscoveryMessage {
                        state: DiscoveryState::QueueData,
                        uri: msg.uri,
                        channels: channels.iter().cloned().collect::<Vec<_>>(),
                    },
                )
                .await?;
                break;
            }
        }
        Ok(())
    }

    async fn on_error(&self, _: DiscoveryMessage) -> Result<(), Box<dyn std::error::Error>> {
        Ok(())
    }

    async fn run_loop(
        &self,
        channels: Arc<Mutex<BTreeSet<Arc<Channel>>>>,
        mut local_channels: &mut BTreeSet<Arc<Channel>>,
    ) -> Result<(), Box<dyn std::error::Error>> {
        let mut rng = SmallRng::from_entropy();
        loop {
            let stream = self.stream.clone();
            let msg = read_msg(stream).await?;
            info!("Received message {}", msg);
            match msg.state {
                DiscoveryState::Connect => self.on_connect(msg, &mut rng).await?,
                DiscoveryState::ConnectResponse => self.on_connect_response(msg).await?,
                DiscoveryState::QueueData => {
                    self.on_queue_data(msg, channels.clone(), &mut local_channels)
                        .await?;
                }
                DiscoveryState::Error => self.on_error(msg).await?,
            }
        }
    }
}

pub async fn run_server(addr: SocketAddr) -> Result<(), Box<dyn std::error::Error>> {
    let pool = ThreadPool::new().unwrap();
    let channels = Arc::new(Mutex::new(BTreeSet::new()));

    let listener = TcpListener::bind(addr).await.unwrap();
    info!("Server is listening on {:?}", listener.local_addr());
    let mut incoming = listener.incoming();

    let web_channels = channels.clone();
    // Spawn off the web server
    pool.spawn(async {
        match web_service(web_channels).await {
            Ok(_) => (),
            Err(e) => error!("failed webservice error {}", e),
        }
    })?;
    while let Some(stream) = incoming.next().await {
        let stream = stream?.clone();
        let global_channels = channels.clone();
        pool.spawn(async move {
            let discovery_service = DiscoveryServer::new(stream.clone());
            let mut local_channels = BTreeSet::new();
            match discovery_service
                .run_loop(global_channels.clone(), &mut local_channels)
                .await
            {
                Ok(_) => (),
                Err(e) => {
                    error!(
                        "failed connection from {} error: {}",
                        stream.peer_addr().unwrap(),
                        e
                    );
                    loop {
                        if let Some(mut channels) = global_channels.try_lock() {
                            for ch in local_channels.iter() {
                                debug!("Removing channels {}", ch);
                                channels.remove(ch);
                            }
                            break;
                        }
                    }
                }
            }
        })?;
    }

    Ok(())
}

fn local_address() -> Url {
    use std::net::{IpAddr, Ipv4Addr};
    SocketAddr::new(IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)), 0);
    urlparse::urlparse("tcp://127.0.0.1:0")
}

fn process_msg(msg: &DiscoveryMessage, channels: Vec<Arc<Channel>>) -> DiscoveryMessage {
    match msg.state {
        DiscoveryState::Error => DiscoveryMessage {
            state: DiscoveryState::Connect,
            uri: local_address(),
            channels: channels,
        },
        DiscoveryState::Connect => DiscoveryMessage {
            state: DiscoveryState::ConnectResponse,
            uri: local_address(),
            channels: channels,
        },
        DiscoveryState::ConnectResponse => DiscoveryMessage {
            state: DiscoveryState::QueueData,
            uri: local_address(),
            channels: channels,
        },
        DiscoveryState::QueueData => DiscoveryMessage {
            state: DiscoveryState::QueueData,
            uri: local_address(),
            channels: channels,
        },
    }
}

pub async fn run_client(
    server: SocketAddr,
    channels: Vec<Arc<Channel>>,
) -> Result<(), Box<dyn std::error::Error>> {
    loop {
        let stream = TcpStream::connect(server).await?;
        let msg = read_msg(stream).await?;
        let next_msg = process_msg(&msg, channels.clone());
        if next_msg.state == DiscoveryState::QueueData {
            // tcpserver.init(next_msg.uri,channels); send mmessages to tcpservice using channels
        } else {
            debug!("Waiting for 10 seconds");
            sleep(std::time::Duration::from_secs(10)).await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures::executor::block_on;
    use std::str::FromStr;
    #[test]
    fn ds_server() {
        let _ = block_on(async { run_server(SocketAddr::from_str("0.0.0.0:9999").unwrap()).await });
    }
}
