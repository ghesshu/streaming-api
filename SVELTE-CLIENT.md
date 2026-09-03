# Svelte client for C# and MediaMTX

The Svelte app talks to two servers:

- C# at `http://localhost:3000` for room creation and authorization.
- MediaMTX at `http://localhost:8889` for WebRTC publishing and viewing.

## Client dependencies

MediaMTX provides standalone browser helpers instead of an npm package. Copy the helper
files that match the pinned MediaMTX `1.20.1` container into the Svelte project:

```bash
mkdir -p src/lib/media
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/publisher.js -o src/lib/media/publisher.js
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/reader.js -o src/lib/media/reader.js
```

SignalR is optional and is only needed for viewer-count updates:

```bash
npm install @microsoft/signalr@10.0.8
```

No SignalR package is required to publish or watch video.

## TypeScript declarations

Add these declarations to the Svelte project's `src/app.d.ts`:

```typescript
type MediaPublisher = {
    close: () => void;
};

type MediaReader = {
    close: () => void;
};

declare global {
    interface Window {
        MediaMTXWebRTCPublisher: new (options: {
            url: string;
            token: string;
            stream: MediaStream;
            videoCodec: string;
            videoBitrate: number;
            audioCodec: string;
            audioBitrate: number;
            audioVoice: boolean;
            onConnected?: () => void;
            onError?: (message: string) => void;
        }) => MediaPublisher;

        MediaMTXWebRTCReader: new (options: {
            url: string;
            token: string;
            onTrack: (event: RTCTrackEvent) => void;
            onError?: (message: string) => void;
        }) => MediaReader;
    }
}

export {};
```

## Broadcaster example

Create `Broadcaster.svelte`:

```svelte
<script lang="ts">
    import { onMount } from 'svelte';

    type Room = {
        roomId: string;
        publisherToken: string;
        viewerToken: string;
        publishUrl: string;
        watchUrl: string;
        sharePath: string;
    };

    let roomId = 'demo-room';
    let status = 'Ready';
    let shareUrl = '';
    let videoElement: HTMLVideoElement;
    let room: Room | null = null;
    let localStream: MediaStream | null = null;
    let publisher: MediaPublisher | null = null;

    onMount(() => {
        void import('$lib/media/publisher.js');

        return () => {
            publisher?.close();
            localStream?.getTracks().forEach(track => track.stop());
        };
    });

    async function startBroadcast() {
        try {
            status = 'Requesting camera';

            localStream = await navigator.mediaDevices.getUserMedia({
                video: true,
                audio: true
            });

            videoElement.srcObject = localStream;

            const response = await fetch(
                'http://localhost:3000/api/streaming/rooms',
                {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ roomId })
                }
            );

            if (!response.ok) {
                throw new Error(await response.text());
            }

            room = await response.json();
            shareUrl = `${window.location.origin}${room.sharePath}`;

            publisher = new window.MediaMTXWebRTCPublisher({
                url: room.publishUrl,
                token: room.publisherToken,
                stream: localStream,
                videoCodec: 'vp8/90000',
                videoBitrate: 2500,
                audioCodec: 'opus/48000',
                audioBitrate: 32,
                audioVoice: true,
                onConnected: () => {
                    status = 'Live';
                },
                onError: message => {
                    status = message;
                }
            });
        } catch (error) {
            status = error instanceof Error ? error.message : 'Could not start';
        }
    }

    async function stopBroadcast() {
        publisher?.close();
        publisher = null;

        localStream?.getTracks().forEach(track => track.stop());
        localStream = null;

        if (room) {
            await fetch(
                `http://localhost:3000/api/streaming/rooms/${room.roomId}`,
                {
                    method: 'DELETE',
                    headers: {
                        'X-Publisher-Token': room.publisherToken
                    }
                }
            );
        }

        room = null;
        status = 'Stopped';
    }
</script>

<input bind:value={roomId} aria-label="Room ID" />
<button onclick={startBroadcast}>Go live</button>
<button onclick={stopBroadcast}>Stop</button>

<p>{status}</p>

{#if shareUrl}
    <p>Share this link: {shareUrl}</p>
{/if}

<video bind:this={videoElement} autoplay playsinline muted></video>
```

The browser sends one WHIP stream to MediaMTX. It does not create a separate peer
connection for every viewer.

## Viewer example

Create `Viewer.svelte`:

```svelte
<script lang="ts">
    import { onMount } from 'svelte';

    export let roomId: string;
    export let viewerToken: string;

    let status = 'Connecting';
    let videoElement: HTMLVideoElement;
    let reader: MediaReader | null = null;

    onMount(() => {
        async function watch() {
            await import('$lib/media/reader.js');

            reader = new window.MediaMTXWebRTCReader({
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

        void watch();

        return () => {
            reader?.close();
        };
    });
</script>

<p>{status}</p>
<video bind:this={videoElement} autoplay playsinline controls></video>
```

## Shareable SvelteKit route

Create `src/routes/watch/[roomId]/+page.svelte`:

```svelte
<script lang="ts">
    import { onMount } from 'svelte';
    import { page } from '$app/state';
    import Viewer from '$lib/Viewer.svelte';

    const roomId = page.params.roomId;
    let viewerToken = '';

    onMount(() => {
        const values = new URLSearchParams(window.location.hash.slice(1));
        viewerToken = values.get('token') ?? '';
    });
</script>

{#if viewerToken}
    <Viewer {roomId} {viewerToken} />
{:else}
    <p>This viewing link is missing its token.</p>
{/if}
```

The API returns a share path like:

```text
/watch/demo-room#token=viewer-token
```

## Optional viewer count

Install `@microsoft/signalr`, connect to `http://localhost:3000/streamingHub`, and then:

```typescript
connection.on('viewer-count-changed', room => {
    console.log(room.viewerCount);
});

connection.on('room-ended', () => {
    console.log('The broadcaster ended this room.');
});

await connection.start();
await connection.invoke('join-room', roomId, viewerToken);
```

SignalR is not involved in the audio/video connection.

## Production checklist

- Replace every localhost URL with the deployed API and MediaMTX addresses.
- Use HTTPS for camera access and WHIP/WHEP negotiation.
- Put the public MediaMTX hostname in `webrtcAdditionalHosts`.
- Configure a TURN server if UDP port `8189` cannot be reached.
- Never expose the publisher token in the viewer link.
- Keep the viewer token in the URL fragment or authenticated application state.

The helper classes come from MediaMTX's official
[publisher.js](https://github.com/bluenviron/mediamtx/blob/v1.20.1/internal/servers/webrtc/publisher.js)
and [reader.js](https://github.com/bluenviron/mediamtx/blob/v1.20.1/internal/servers/webrtc/reader.js).
