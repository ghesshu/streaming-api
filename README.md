# Streaming API

An ASP.NET Core SignalR signaling server for one WebRTC broadcaster and multiple viewers.
The media goes directly between browsers; the server only coordinates rooms and forwards
offers, answers, and ICE candidates.

## Run locally

```bash
dotnet run
```

The API listens on `http://localhost:3000`. Check it with `GET /health`.

## HTTP controller

`StreamingController` provides the normal request-response API:

- `GET /api/streaming` describes the streaming service and SignalR endpoint.
- `GET /api/streaming/rooms/{roomId}` reports whether a known room is live and its
  current viewer count.

Live room creation, joining, and WebRTC signaling remain in `StreamingHub` because
those operations require a persistent, two-way SignalR connection.

For a Svelte or SvelteKit client, follow [SVELTE-CLIENT.md](SVELTE-CLIENT.md).

## SignalR contract

Connect to `/streamingHub`, then invoke:

- `create-room` with a room ID
- `join-room` with a room ID
- `offer` or `answer` with `{ target, sdp }`
- `ice-candidate` with `{ target, candidate }`

Listen for `room-created`, `joined-room`, `viewer-joined`, `viewer-left`, `offer`,
`answer`, `ice-candidate`, `broadcaster-left`, and `streaming-error`.

## Production scaling

The in-memory room registry is intentionally scoped to one server process. For multiple
API instances, move room presence into a distributed store and add a SignalR backplane.
Browser-to-browser WebRTC makes the broadcaster upload once per viewer, so use an SFU
such as LiveKit, Janus, or mediasoup when a stream needs a large audience. Configure a
TURN server as well; a public STUN server alone cannot connect every network topology.

Replace the wildcard entry in `Cors:AllowedOrigins` before production deployment.
