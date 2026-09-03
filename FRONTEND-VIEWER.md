# Simple Svelte viewer

This page receives an existing MediaMTX stream and displays it in a Svelte `<video>`
element. The viewer does not connect to the broadcaster directly.

The expected viewing link looks like this:

```text
http://localhost:5173/watch/demo-room#token=viewer-token
```

- `demo-room` identifies the MediaMTX stream.
- `viewer-token` gives read-only access to that room.
- MediaMTX sends the live audio and video to the browser through WebRTC/WHEP.

## 1. Add the MediaMTX reader

No npm package is required for video playback. Copy the official reader into the Svelte
project:

```bash
mkdir -p src/lib/media
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/reader.js -o src/lib/media/reader.js
```

## 2. Create the viewer route

Create this file:

```text
src/routes/watch/[roomId]/+page.svelte
```

Add:

```svelte
<script lang="ts">
    import { onMount } from 'svelte';
    import { page } from '$app/state';

    type MediaReader = {
        close: () => void;
    };

    type MediaWindow = Window & {
        MediaMTXWebRTCReader: new (options: {
            url: string;
            token: string;
            onTrack: (event: RTCTrackEvent) => void;
            onError: (message: string) => void;
        }) => MediaReader;
    };

    const roomId = page.params.roomId;

    let status = 'Connecting';
    let videoElement: HTMLVideoElement;
    let reader: MediaReader | null = null;

    onMount(() => {
        async function startViewing() {
            const values = new URLSearchParams(window.location.hash.slice(1));
            const viewerToken = values.get('token');

            if (!viewerToken) {
                status = 'The viewing link is missing its token';
                return;
            }

            await import('$lib/media/reader.js');

            const mediaWindow = window as MediaWindow;

            reader = new mediaWindow.MediaMTXWebRTCReader({
                url: `http://localhost:8889/${roomId}/whep`,
                token: viewerToken,
                onTrack: event => {
                    const stream = event.streams[0];

                    if (stream) {
                        videoElement.srcObject = stream;
                        status = 'Watching live';
                    }
                },
                onError: message => {
                    status = message;
                }
            });
        }

        void startViewing();

        return () => {
            reader?.close();
        };
    });
</script>

<h1>Live stream</h1>
<p>{status}</p>

<video
    bind:this={videoElement}
    autoplay
    playsinline
    controls
    muted
></video>
```

## 3. What happens

When the page opens:

1. Svelte gets `roomId` from `/watch/[roomId]`.
2. Svelte gets `viewerToken` from the URL fragment after `#token=`.
3. The reader sends a WHEP request to:

   ```text
   http://localhost:8889/demo-room/whep
   ```

4. It sends the viewer token as an `Authorization: Bearer` header.
5. MediaMTX asks the C# API whether the token can read `demo-room`.
6. C# approves the request.
7. The reader receives a browser `MediaStream` through `onTrack`.
8. Svelte assigns that stream to `videoElement.srcObject`.

This is the line that displays the stream:

```typescript
videoElement.srcObject = stream;
```

The video starts muted because browsers commonly block unmuted autoplay. The viewer can
unmute it using the video controls.

## Production

Change this local address:

```text
http://localhost:8889
```

to the public HTTPS MediaMTX address, for example:

```text
https://media.example.com
```

The C# API and MediaMTX must both be running, and the broadcaster must already be
publishing to the same room before the viewer opens the link.
