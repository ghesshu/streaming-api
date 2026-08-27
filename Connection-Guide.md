Below is a simple explanation you can give to the Svelte developer.

The person recording is the **broadcaster**. The person watching is the **viewer**.

The C# API only handles rooms and WebRTC connection messages. The video travels directly between their browsers.

First install SignalR:

```bash
npm install @microsoft/signalr@10.0.8
```

## Person recording: `Broadcaster.svelte`

This component:

1. Connects to the C# SignalR Hub.
2. Requests camera and microphone permission.
3. Creates the room.
4. Sends video to every viewer who joins.

```svelte
<script lang="ts">
    import { onMount } from 'svelte';
    import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr';

    let roomId = 'demo-room';
    let status = 'Connecting';
    let videoElement: HTMLVideoElement;
    let connection: HubConnection;
    let localStream: MediaStream;

    const peerConnections = new Map<string, RTCPeerConnection>();
    const pendingCandidates = new Map<string, RTCIceCandidate[]>();

    onMount(() => {
        connection = new HubConnectionBuilder()
            .withUrl('http://localhost:3000/streamingHub')
            .withAutomaticReconnect()
            .build();

        connection.on('room-created', message => {
            status = `Room created: ${message.roomId}`;
        });

        connection.on('viewer-joined', async message => {
            status = 'A viewer joined';

            const peerConnection = createPeerConnection(message.viewerId);

            for (const track of localStream.getTracks()) {
                peerConnection.addTrack(track, localStream);
            }

            const offer = await peerConnection.createOffer();
            await peerConnection.setLocalDescription(offer);

            await connection.invoke('offer', {
                target: message.viewerId,
                sdp: offer
            });
        });

        connection.on('answer', async message => {
            const peerConnection = peerConnections.get(message.sender);

            if (!peerConnection) {
                return;
            }

            await peerConnection.setRemoteDescription(message.sdp);
            await addPendingCandidates(message.sender, peerConnection);
        });

        connection.on('ice-candidate', async message => {
            const peerConnection = peerConnections.get(message.sender);
            const candidate = new RTCIceCandidate(message.candidate);

            if (!peerConnection?.remoteDescription) {
                const candidates = pendingCandidates.get(message.sender) ?? [];

                candidates.push(candidate);
                pendingCandidates.set(message.sender, candidates);
                return;
            }

            await peerConnection.addIceCandidate(candidate);
        });

        connection.on('viewer-left', message => {
            peerConnections.get(message.viewerId)?.close();
            peerConnections.delete(message.viewerId);
            pendingCandidates.delete(message.viewerId);
        });

        connection.on('streaming-error', message => {
            status = message;
        });

        connection.start()
            .then(() => {
                status = 'Connected and ready';
            })
            .catch(error => {
                status = error.message;
            });

        return () => {
            localStream?.getTracks().forEach(track => track.stop());

            for (const peerConnection of peerConnections.values()) {
                peerConnection.close();
            }

            void connection.stop();
        };
    });

    function createPeerConnection(viewerId: string) {
        const peerConnection = new RTCPeerConnection({
            iceServers: [
                {
                    urls: 'stun:stun.l.google.com:19302'
                }
            ]
        });

        peerConnection.onicecandidate = async event => {
            if (!event.candidate) {
                return;
            }

            await connection.invoke('ice-candidate', {
                target: viewerId,
                candidate: event.candidate.toJSON()
            });
        };

        peerConnections.set(viewerId, peerConnection);

        return peerConnection;
    }

    async function addPendingCandidates(
        viewerId: string,
        peerConnection: RTCPeerConnection
    ) {
        const candidates = pendingCandidates.get(viewerId) ?? [];

        for (const candidate of candidates) {
            await peerConnection.addIceCandidate(candidate);
        }

        pendingCandidates.delete(viewerId);
    }

    async function startBroadcast() {
        try {
            localStream = await navigator.mediaDevices.getUserMedia({
                video: true,
                audio: true
            });

            videoElement.srcObject = localStream;

            await connection.invoke('create-room', roomId);
        } catch (error) {
            status = error instanceof Error
                ? error.message
                : 'Could not start broadcast';
        }
    }
</script>

<h1>Start a live stream</h1>

<label for="roomId">Room ID</label>
<input id="roomId" bind:value={roomId} />

<button type="button" onclick={startBroadcast}>
    Start broadcasting
</button>

<p>{status}</p>

<video
    bind:this={videoElement}
    autoplay
    playsinline
    muted
></video>
```

The important room creation line is:

```typescript
await connection.invoke('create-room', roomId);
```

If the room ID is `demo-room`, the broadcaster can share a link such as:

```text
https://your-svelte-app.com/watch/demo-room
```

## Person watching: `Viewer.svelte`

This component:

1. Connects to the C# SignalR Hub.
2. Automatically joins the room.
3. Receives the broadcaster’s WebRTC offer.
4. Displays the live stream.

```svelte
<script lang="ts">
    import { onMount } from 'svelte';
    import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr';

    export let roomId: string;

    let status = 'Connecting';
    let videoElement: HTMLVideoElement;
    let connection: HubConnection;
    let peerConnection: RTCPeerConnection | null = null;
    let broadcasterId: string | null = null;

    const pendingCandidates: RTCIceCandidate[] = [];

    onMount(() => {
        connection = new HubConnectionBuilder()
            .withUrl('http://localhost:3000/streamingHub')
            .withAutomaticReconnect()
            .build();

        connection.on('joined-room', message => {
            broadcasterId = message.broadcasterId;
            status = `Joined room ${message.roomId}`;
        });

        connection.on('offer', async message => {
            broadcasterId = message.sender;
            peerConnection = createPeerConnection(message.sender);

            await peerConnection.setRemoteDescription(message.sdp);

            for (const candidate of pendingCandidates) {
                await peerConnection.addIceCandidate(candidate);
            }

            pendingCandidates.length = 0;

            const answer = await peerConnection.createAnswer();
            await peerConnection.setLocalDescription(answer);

            await connection.invoke('answer', {
                target: message.sender,
                sdp: answer
            });
        });

        connection.on('ice-candidate', async message => {
            const candidate = new RTCIceCandidate(message.candidate);

            if (!peerConnection?.remoteDescription) {
                pendingCandidates.push(candidate);
                return;
            }

            await peerConnection.addIceCandidate(candidate);
        });

        connection.on('broadcaster-left', () => {
            status = 'The live stream has ended';
            videoElement.srcObject = null;

            peerConnection?.close();
            peerConnection = null;
        });

        connection.on('streaming-error', message => {
            status = message;
        });

        async function connectAndJoin() {
            try {
                await connection.start();

                status = 'Connected, joining room';

                await connection.invoke('join-room', roomId);
            } catch (error) {
                status = error instanceof Error
                    ? error.message
                    : 'Could not join the stream';
            }
        }

        void connectAndJoin();

        return () => {
            peerConnection?.close();
            void connection.stop();
        };
    });

    function createPeerConnection(senderId: string) {
        const newPeerConnection = new RTCPeerConnection({
            iceServers: [
                {
                    urls: 'stun:stun.l.google.com:19302'
                }
            ]
        });

        newPeerConnection.ontrack = event => {
            const remoteStream = event.streams[0];

            if (remoteStream) {
                videoElement.srcObject = remoteStream;
                status = 'Watching live stream';
            }
        };

        newPeerConnection.onicecandidate = async event => {
            if (!event.candidate) {
                return;
            }

            await connection.invoke('ice-candidate', {
                target: senderId,
                candidate: event.candidate.toJSON()
            });
        };

        return newPeerConnection;
    }
</script>

<h1>Watching room: {roomId}</h1>

<p>{status}</p>

<video
    bind:this={videoElement}
    autoplay
    playsinline
    controls
></video>
```

The important room joining line is:

```typescript
await connection.invoke('join-room', roomId);
```

## Shareable SvelteKit route

Put the viewer page at:

```text
src/routes/watch/[roomId]/+page.svelte
```

Then use:

```svelte
<script lang="ts">
    import { page } from '$app/state';
    import Viewer from '$lib/Viewer.svelte';

    const roomId = page.params.roomId;
</script>

<Viewer {roomId} />
```

Now this link:

```text
https://your-svelte-app.com/watch/demo-room
```

automatically joins:

```text
demo-room
```

The room must already have an active broadcaster. Also, this is live broadcasting—it does not save a recording to the C# server. Camera and microphone access requires HTTPS in production or localhost during development.