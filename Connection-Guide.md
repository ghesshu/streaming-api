# How the token-free stream works

There are three applications:

```text
Svelte browser → C# API → creates and tracks rooms
Svelte browser → MediaMTX → sends or receives live media
Svelte browser → SignalR → optional viewer-count updates
```

## Broadcaster

1. Svelte calls `POST /api/streaming/rooms` with a room ID.
2. C# creates the room and returns its WHIP publish URL.
3. Svelte captures the camera and microphone.
4. Svelte publishes that stream to the WHIP URL.
5. MediaMTX asks C# whether the room exists.
6. C# returns HTTP 204, so MediaMTX accepts the stream.

The broadcaster uploads one stream to MediaMTX regardless of viewer count.

## Viewer

1. The viewer opens a link such as:

   ```text
   https://stream.example.com/watch/demo-room
   ```

2. Svelte reads `demo-room` from the route.
3. Svelte opens `https://media.example.com/demo-room/whep`.
4. MediaMTX asks C# whether `demo-room` exists.
5. C# returns HTTP 204, and MediaMTX sends the stream to the viewer.

## Important security behavior

There are no publisher or viewer credentials. Anyone who knows or guesses `demo-room`
can publish, watch, register presence, or call the room deletion endpoint. Use this mode
only when that open-access behavior is intentional.

## What each C# file does

- `StreamingController.cs` creates, reads, and deletes rooms.
- `MediaMtxAuthController.cs` confirms that requested rooms exist.
- `RoomRegistry.cs` stores rooms and viewer presence in memory.
- `StreamingHub.cs` reports optional viewer-count changes.
- `MediaMtxOptions.cs` contains the public MediaMTX addresses.
