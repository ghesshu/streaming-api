# How a broadcaster and viewer connect

There are three applications:

```text
Svelte browser → C# API → room creation and authorization
Svelte browser → MediaMTX → live audio and video
Svelte browser → SignalR → optional viewer-count updates
```

## Broadcaster

1. The Svelte app captures the camera and microphone.
2. It calls `POST http://localhost:3000/api/streaming/rooms`.
3. C# returns a WHIP publish URL, publisher token, viewer token, and share path.
4. Svelte publishes the camera stream to the WHIP URL using the publisher token.
5. MediaMTX asks the C# callback whether that token can publish to that room.
6. C# approves it, and MediaMTX starts receiving the stream.

The broadcaster uploads one media stream to MediaMTX regardless of viewer count.

## Viewer

1. The viewer opens a link such as:

   ```text
   https://stream.example.com/watch/demo-room#token=viewer-token
   ```

2. Svelte reads the room ID from the route and the token from the URL fragment.
3. Svelte opens the room's WHEP URL with the viewer token.
4. MediaMTX asks C# whether the token can read that room.
5. C# approves it, and MediaMTX sends the stream to the viewer.

The URL fragment is not sent automatically to web servers. Svelte reads it and sends the
token to MediaMTX in the `Authorization: Bearer` header.

## What each C# file does

- `StreamingController.cs` creates, reads, and deletes rooms.
- `MediaMtxAuthController.cs` authorizes MediaMTX publish/read requests.
- `RoomRegistry.cs` stores room token hashes and viewer presence in memory.
- `StreamingHub.cs` reports optional viewer-count changes through SignalR.
- `MediaMtxOptions.cs` contains the public MediaMTX URLs and callback secret.

The old peer-to-peer `offer`, `answer`, and `ice-candidate` methods are intentionally gone.
MediaMTX now owns WebRTC signaling and media distribution.
