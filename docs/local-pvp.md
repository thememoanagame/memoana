# Local PVP transport

Local PVP uses LiteNetLib for both LAN discovery and the peer-to-peer game
session. The app does not use a backend relay, TCP sockets, mDNS, or a
second discovery transport.

## Session flow

The host starts LiteNetLib on the configured Local PVP UDP port and answers
unconnected LAN discovery messages. A peer broadcasts discovery messages,
selects a room, and connects directly to the host using the room identifier as
the LiteNetLib connection key.

The transport exposes `ILocalPVPService`; UI and game code do not reference
LiteNetLib types. Messages use an explicit JSON envelope containing the
protocol version, room identifier, message identifier, and sequence number.
Game messages use reliable ordered delivery. Duplicate or out-of-order
messages are rejected before they reach the game service.

The host owns board creation and authoritative turn resolution. The peer sends
card selections, while the host sends turn results, scores, turn ownership,
and game completion. Both sides maintain their local game projection from
these transitions.

## Lifecycle and threading

Discovery and peer processing are cancellation-aware. `LeaveAsync` stops
discovery, disconnects the peer, stops `NetManager`, and releases the
associated cancellation resources. LiteNetLib callbacks perform transport
validation on the network worker; UI consumers dispatch only the render
notification to the UI thread.

The default UDP port is `45874` and is defined by `LocalPvpOptions`. Keep the
port available through the local firewall on Windows and retain the Android
multicast permission only when platform discovery requires it.
