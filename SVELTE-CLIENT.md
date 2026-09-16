# Token-free Svelte streaming client

The Svelte app uses:

- C# at `http://localhost:3000` to create and track rooms.
- MediaMTX at `http://localhost:8889` to publish and watch streams.
- SignalR optionally for viewer-count updates.

No publisher or viewer credentials are required.

## Add the MediaMTX browser helpers

```bash
mkdir -p src/lib/media
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/publisher.js -o src/lib/media/publisher.js
curl -L https://raw.githubusercontent.com/bluenviron/mediamtx/v1.20.1/internal/servers/webrtc/reader.js -o src/lib/media/reader.js
```

SignalR is optional:

```bash
npm install @microsoft/signalr@10.0.8
```

## Broadcaster example

```svelte
<script lang="ts">
    import { onMount } from 'svelte';

    type Room = {
        roomId: string;
        publishUrl: string;
        watchUrl: string;
        hlsUrl: string;
        sharePath: string;
    };

    type MediaPublisher = {
        close: () => void;
    };

    type MediaWindow = Window & {
        MediaMTXWebRTCPublisher: new (options: {
            url: string;
            stream: MediaStream;
            videoCodec: string;
            videoBitrate: number;
            audioCodec: string;
            audioBitrate: number;
            audioVoice: boolean;
            onConnected: () => void;
            onError: (message: string) => void;
        }) => MediaPublisher;
    };

    let roomId = 'demo-room';
    let status = 'Ready';
    let shareUrl = '';
    let videoElement: HTMLVideoElement;
    let publisher: MediaPublisher | null = null;
    let localStream: MediaStream | null = null;
    let room: Room | null = null;
    let publisherReady: Promise<unknown> | null = null;

    onMount(() => {
        publisherReady = import('$lib/media/publisher.js');

        return () => {
            publisher?.close();
            localStream?.getTracks().forEach(track => track.stop());
        };
    });

    async function startBroadcast() {
        await publisherReady;

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
            status = await response.text();
            return;
        }

        room = await response.json();
        shareUrl = `${window.location.origin}${room.sharePath}`;

        const mediaWindow = window as MediaWindow;

        publisher = new mediaWindow.MediaMTXWebRTCPublisher({
            url: room.publishUrl,
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
    }

    async function stopBroadcast() {
        publisher?.close();
        publisher = null;

        localStream?.getTracks().forEach(track => track.stop());
        localStream = null;

        if (room) {
            await fetch(
                `http://localhost:3000/api/streaming/rooms/${room.roomId}`,
                { method: 'DELETE' }
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
{#if shareUrl}<p>Share: {shareUrl}</p>{/if}
<video bind:this={videoElement} autoplay playsinline muted></video>
```

## Viewer example

See [FRONTEND-VIEWER.md](FRONTEND-VIEWER.md) for the complete viewer route. It opens:

```text
http://localhost:8889/{roomId}/whep
```

without an authorization header.

## Optional viewer count

```typescript
import { HubConnectionBuilder } from '@microsoft/signalr';

const connection = new HubConnectionBuilder()
    .withUrl('http://localhost:3000/streamingHub')
    .withAutomaticReconnect()
    .build();

connection.on('viewer-count-changed', room => {
    console.log(room.viewerCount);
});

await connection.start();
await connection.invoke('join-room', roomId);
```

## Security warning

In this configuration, anyone who knows or guesses a room ID can publish, watch, join
presence, or delete the room. Add authentication before using it for private streams.
