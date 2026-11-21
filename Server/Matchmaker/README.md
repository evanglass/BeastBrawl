# Feral Fighters matchmaker

The matchmaker accepts the same WebSocket connection used by the game client. It assigns each connection to the oldest room with space and starts a separate Unity dedicated-server process for each room. Four players fill a room; subsequent players go to another room. A room accepts replacements during the lobby, closes to new players when arena loading starts, and its process is stopped when its last connection leaves.

The matchmaker and game servers listen on loopback only by default. A TLS WebSocket reverse proxy exposes `beastnet.evanglass.me:18773` and forwards requests to the matchmaker at `127.0.0.1:18774`. The matchmaker forwards WebSocket messages to room processes on local ports starting at 20000. This also supports Web clients; no client-side room address or additional public room ports are required.

## Custom games

Keep the same TLS proxy and forward both standard HTTP requests and WebSocket upgrades:

- `GET /custom/lobbies` lists only configured custom rooms with available human slots.
- WebSocket `/custom/host` creates a dedicated custom room. The first human owns setup and the room stays hidden until Unity accepts their configuration.
- WebSocket `/custom/join/{id}` connects to the selected room. A stale, started, full, or closed room rejects the connection.
- WebSocket `/` continues using the existing four-player quick match queue.

Unity publishes occupancy to the matchmaker over the room process's stdout. Clients cannot publish listings. Human slots are validated again by the dedicated server before the lobby can start. Custom rooms can run server-owned bots without starting an additional worker process. In an all-bot custom roster the host spectates, and the room is not listed because it has no human slots.

Deploy matching Unity client and Linux dedicated-server builds along with this matchmaker update. Configure the NGO setup's `onlineAddress` and `onlinePort` to point to the TLS proxy. Desktop clients can override them with `-custom-endpoint HOST PORT`; this changes the browser's endpoint and does not automatically join a game. Public discovery is unavailable on the older backend.

## Build and run

1. Build a **Linux dedicated server** for the same project and version as the clients. Include `NGO_Setup`, the main menu, and arena scenes in the build. The Unity server must include the `FERAL_MATCHMAKER_ROOM_CLOSED` log message in `MatchManager` so that started matches stop accepting replacements.
2. Publish this project with a .NET 8 SDK:

   ```sh
   dotnet publish Server/Matchmaker/Matchmaker.csproj -c Release -r linux-x64 --self-contained true -o ./matchmaker-publish
   ```

3. On the Linux machine, set `FERAL_GAME_SERVER` to the absolute path of the Unity Linux dedicated-server executable and start the published matchmaker:

   ```sh
   export FERAL_GAME_SERVER=/absolute/path/to/FeralFightersServer.x86_64
   ./matchmaker-publish/Matchmaker
   ```

   Keep the executable and its accompanying Unity data files together. The matchmaker launches it with `-batchmode -nographics -logFile - -port <room-port>`.

4. Configure a TLS reverse proxy with a trusted certificate for `beastnet.evanglass.me`, listening on TCP port **18773** and forwarding WebSocket upgrades to `http://127.0.0.1:18774`. Point DNS to the machine and allow incoming TCP traffic on 18773. Keep the matchmaker and room ports private.
5. Check `http://127.0.0.1:18774/health` locally, then test actual desktop and Web clients through the public TLS endpoint. The current arena scenes also need their spawn points before a game can start.

For example, a Caddy configuration can contain:

```caddyfile
https://beastnet.evanglass.me:18773 {
    reverse_proxy 127.0.0.1:18774
}
```

Certificate issuance and renewal must also work for the server's existing DNS and firewall configuration. Run the matchmaker under a process manager that terminates its child processes when it stops; for systemd, keep all child processes in the service's control group.

## Configuration

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `FERAL_GAME_SERVER` | Required | Absolute path to the Linux Unity server executable. |
| `FERAL_MATCHMAKER_URL` | `http://127.0.0.1:18774` | Local HTTP/WebSocket listener. |
| `FERAL_FIRST_ROOM_PORT` | `20000` | First local port available for Unity room processes. |
| `FERAL_MAX_ROOMS` | `16` | Maximum simultaneous Unity processes. Set this to fit the machine's resources. |
| `FERAL_START_TIMEOUT_SECONDS` | `30` | Time to wait for a room process to accept WebSocket connections. |

The room-port range is `FERAL_FIRST_ROOM_PORT` through `FERAL_FIRST_ROOM_PORT + FERAL_MAX_ROOMS - 1`. A new connection receives HTTP 503 when all room slots are occupied or no room port is available. Set `Logging__LogLevel__Default=Debug` to include the Unity processes' stdout in matchmaker logs.

### Game balance

Copy [gameglobals.json](gameglobals.json) beside the Unity server executable and edit its values to adjust gameplay without rebuilding. Alternatively, set `FERAL_GAME_CONFIG` to the absolute path of a JSON file before starting the matchmaker. Each new room process reads the file once on startup; existing rooms retain their current settings. If the file is absent, the server uses the values saved in the Unity scene. Omitted fields also retain their scene values. Invalid or unreadable files are reported in the server log.

These settings affect the dedicated server's authoritative game simulation. Desktop LAN hosts and clients continue using their scene values.

This is a single-machine, first-available matchmaker. It does not provide accounts, skill ratings, regional selection, or a persistent queue when the machine is full. Players must run matching client and server builds.
