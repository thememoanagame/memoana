# PVP over LAN

MemoAna's PVP mode uses LiteNetLib 2.1.4 over UDP. `IPVPService` is the
application boundary; Razor components do not access LiteNetLib types.

## Architecture

```text
Game.razor -> IPVPService -> PVPService -> LiteNetLib -> LAN
```

The host owns the session and is authoritative for the board, turn, card
validation, matches, mismatches, scores, and game completion. A client sends
card-selection intent only. A matching pair keeps the current player's turn;
a mismatch changes it to the other player.

The host sends the theme name and a compact board description containing
positions, pair identifiers, and image indexes. Each peer loads the selected
theme locally, so card images are not sent for every move. Subsequent messages
contain only card positions and authoritative state changes.

## Flow

1. Select difficulty, PVP, and a theme.
2. Choose **Criar sala** on one device or **Procurar salas** on the other.
3. The host waits for one client and starts the game.
4. The client waits for the host's initial state before displaying the board.
5. Use **INICIO**, **REPETIR**, or navigation away from the page to stop the
   session and release the network manager.

Discovery uses LiteNetLib broadcast on UDP port `35679`; game traffic defaults
to UDP port `35678`. The ports can be supplied through `PVPGameSettings` for
tests or custom deployments.

## Platform requirements

Android declares `INTERNET` and `ACCESS_NETWORK_STATE`. Both devices must be on
the same LAN. Windows firewall rules must allow the configured UDP ports, and
Android may require the network permission prompt to be accepted.

## Runtime and validation notes

`PVPService` owns a polling loop for every running `NetManager`; without
`PollEvents`, LiteNetLib callbacks do not process discovery, connection or game
messages. The host advertises the first operational non-loopback IPv4 address
available on the machine and falls back to the request endpoint only when no
such interface exists. A mismatch remains visible for 500 ms on both peers
before either peer may start its next turn.

The Windows-targeted game library and the desktop host/client integration
harness have been validated with a real LiteNetLib pair, including initial
state synchronization and mismatch resolution. The MAUI Windows DeviceRunners
host executes the complete unit-test suite, including the LiteNetLib tests;
the current run completed with 24 passed tests and no failures or skips.

Physical Windows-to-Android and Android-to-Windows LAN validation still
requires an ADB-authorized Android device and separately controlled app
automation endpoints. No physical validation is claimed until both directions
have been exercised. MAUI DevFlow is not required for the current test-runner
validation.
