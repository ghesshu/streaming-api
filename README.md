# Streaming API with MediaMTX

This project is the control API for a one-to-many live-streaming platform.

- ASP.NET Core creates rooms, issues access tokens, and authorizes media access.
- MediaMTX receives one stream from the broadcaster and distributes it to viewers.
- SignalR is used only for optional presence updates such as viewer counts.
- Svelte owns the recording and viewing interfaces.

The C# API does not relay video bytes and does not serve HTML.

## Run the complete stack

Start Docker Desktop, then run:

```bash
docker compose up --build
```

Local services:

| Service | Address |
| --- | --- |
| C# API | `http://localhost:3000` |
| MediaMTX WebRTC | `http://localhost:8889` |
| MediaMTX HLS | `http://localhost:8888` |
| MediaMTX WebRTC media | UDP port `8189` |
| MediaMTX RTMP ingest | `rtmp://localhost:1935` |
| MediaMTX RTSP | `rtsp://localhost:8554` |

Run only the API with `dotnet run`. Media publishing and playback require MediaMTX too.

## Create a room

```http
POST /api/streaming/rooms
Content-Type: application/json

{
  "roomId": "demo-room"
}
```

The room ID is optional. The API generates one when it is empty.

Example response:

```json
{
  "roomId": "demo-room",
  "publisherToken": "secret-publisher-token",
  "viewerToken": "shareable-viewer-token",
  "publishUrl": "http://localhost:8889/demo-room/whip",
  "watchUrl": "http://localhost:8889/demo-room/whep",
  "hlsUrl": "http://localhost:8888/demo-room/index.m3u8",
  "sharePath": "/watch/demo-room#token=shareable-viewer-token"
}
```

The raw tokens are returned only when the room is created. The registry keeps SHA-256
hashes instead of the raw values.

## Room endpoints

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `POST` | `/api/streaming/rooms` | Create a room and its tokens |
| `GET` | `/api/streaming/rooms/{roomId}` | Read room metadata and viewer count |
| `DELETE` | `/api/streaming/rooms/{roomId}` | Delete a room using `X-Publisher-Token` |
| `GET` | `/api/streaming` | Describe the service |
| `GET` | `/health` | API health check |

Close the WHIP publisher before deleting its room. Deleting the room invalidates future
MediaMTX requests but does not forcibly terminate a media session already in progress.

## Media authorization

MediaMTX sends every `publish`, `read`, and `playback` authorization request to:

```text
POST /api/media-auth
```

This is an internal callback, not a frontend endpoint.

- The publisher token can publish and read its room.
- The viewer token can only read its room.
- Tokens cannot access a different room.
- Unknown rooms and invalid tokens receive HTTP 401.

WHIP and WHEP clients send the token with:

```http
Authorization: Bearer token-value
```

## SignalR presence

Connect to `/streamingHub`, then register a viewer with:

```typescript
await connection.invoke('join-room', roomId, viewerToken);
```

Listen for:

- `joined-room`
- `viewer-count-changed`
- `room-ended`
- `streaming-error`

SignalR no longer forwards WebRTC offers, answers, or ICE candidates. MediaMTX handles
that work through WHIP and WHEP.

## Recording

Recording is configured but disabled in `mediamtx.yml`. To store streams as fragmented
MP4 segments, change:

```yaml
pathDefaults:
  record: true
```

Files will be written under `recordings/{roomId}` and deleted after seven days by the
current example configuration.

## Important production changes

- Replace `local-media-auth-key` in both `appsettings.json` and `mediamtx.yml`.
- Replace wildcard CORS origins in both services with the Svelte application's origin.
- Replace `127.0.0.1` in `webrtcAdditionalHosts` with the server's public IP or DNS name.
- Serve the API, Svelte app, and MediaMTX handshake endpoints over HTTPS.
- Configure TURN when clients cannot reach MediaMTX UDP port `8189` directly.
- Replace the in-memory `RoomRegistry` with a shared persistent store before running
  multiple API instances. No database migration is included or run by this project.

See [FRONTEND-VIEWER.md](FRONTEND-VIEWER.md) for the simplest viewing example,
[SVELTE-CLIENT.md](SVELTE-CLIENT.md) for complete browser integration, and
[Connection-Guide.md](Connection-Guide.md) for the short end-to-end explanation.
