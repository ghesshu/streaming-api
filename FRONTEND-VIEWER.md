# Simple token-free Svelte viewer

The viewing link is simply:

```text
http://localhost:5173/watch/demo-room
```

`demo-room` is the room ID. No token or authorization header is required.

## 1. Add the MediaMTX reader

Copy MediaMTX's official browser reader into the Svelte project:

```bash
mkdir -p src/lib/media
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/reader.js -o src/lib/media/reader.js
```

## 2. Create the viewer route

Create `src/routes/watch/[roomId]/+page.svelte`:

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
            await import('$lib/media/reader.js');

            const mediaWindow = window as MediaWindow;

            reader = new mediaWindow.MediaMTXWebRTCReader({
                url: `http://localhost:8889/${roomId}/whep`,
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

The stream reaches the page through `onTrack`. This line displays it:

```typescript
videoElement.srcObject = stream;
```

The broadcaster must already be publishing to the same room. In production, replace
`http://localhost:8889` with the public HTTPS MediaMTX address.
