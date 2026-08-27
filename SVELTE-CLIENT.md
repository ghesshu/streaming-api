# Connect a Svelte app to the Streaming API

This guide connects a Svelte or SvelteKit browser app to the ASP.NET Core streaming API.
SignalR transports the room and WebRTC signaling messages. WebRTC transports the actual
audio and video directly between the broadcaster and viewers.

## 1. Install the client package

From the Svelte project directory, install Microsoft's SignalR JavaScript client:

```bash
npm install @microsoft/signalr@10.0.8
```

Or use the equivalent command for the project's package manager:

```bash
pnpm add @microsoft/signalr@10.0.8
```

```bash
yarn add @microsoft/signalr@10.0.8
```

No WebRTC package is required. Modern browsers provide `RTCPeerConnection`,
`RTCSessionDescription`, `RTCIceCandidate`, and `navigator.mediaDevices`.

Version `10.0.8` matches the .NET 10 server in this repository. Keep the SignalR client
and server on the same major version when upgrading.

## 2. Start the C# API

In the API repository, run:

```bash
dotnet run
```

The local endpoints are:

- Health check: `http://localhost:3000/health`
- Streaming controller: `http://localhost:3000/api/streaming`
- Room status: `http://localhost:3000/api/streaming/rooms/{roomId}`
- SignalR hub: `http://localhost:3000/streamingHub`

The controller endpoints are normal HTTP requests. For example, a Svelte app can check
whether a room is live before joining it:

```typescript
async function getRoomStatus(apiUrl: string, roomId: string) {
    const response = await fetch(
        `${apiUrl}/api/streaming/rooms/${encodeURIComponent(roomId)}`,
        { credentials: 'include' }
    );

    if (response.status === 404) {
        return null;
    }

    if (!response.ok) {
        throw new Error('Could not check the room.');
    }

    return response.json() as Promise<{
        roomId: string;
        viewerCount: number;
        isLive: boolean;
    }>;
}
```

## 3. Create the SignalR client

Create `src/lib/streamingClient.ts` in the Svelte project:

```typescript
import {
    HubConnectionBuilder,
    HubConnectionState,
    LogLevel,
    type HubConnection
} from '@microsoft/signalr';

type StreamingClientOptions = {
    apiUrl: string;
    onLocalStream?: (stream: MediaStream) => void;
    onRemoteStream?: (stream: MediaStream) => void;
    onStatus?: (message: string) => void;
};

type DescriptionMessage = {
    sender: string;
    sdp: RTCSessionDescriptionInit;
};

type CandidateMessage = {
    sender: string;
    candidate: RTCIceCandidateInit;
};

type ViewerMessage = {
    viewerId: string;
};

export class StreamingClient {
    private readonly connection: HubConnection;
    private readonly peerConnections = new Map<string, RTCPeerConnection>();
    private readonly pendingCandidates = new Map<string, RTCIceCandidate[]>();
    private readonly options: StreamingClientOptions;
    private readonly iceServers: RTCIceServer[] = [
        { urls: 'stun:stun.l.google.com:19302' }
    ];

    private localStream: MediaStream | null = null;

    constructor(options: StreamingClientOptions) {
        this.options = options;
        this.connection = new HubConnectionBuilder()
            .withUrl(`${options.apiUrl}/streamingHub`, {
                withCredentials: true
            })
            .withAutomaticReconnect([0, 2000, 10000, 30000])
            .configureLogging(LogLevel.Information)
            .build();

        this.registerEvents();
    }

    get isConnected(): boolean {
        return this.connection.state === HubConnectionState.Connected;
    }

    async connect(): Promise<void> {
        if (this.connection.state !== HubConnectionState.Disconnected) {
            return;
        }

        await this.connection.start();
        this.options.onStatus?.('Connected to streaming API');
    }

    async startBroadcast(roomId: string): Promise<void> {
        this.ensureConnected();

        this.localStream = await navigator.mediaDevices.getUserMedia({
            video: true,
            audio: true
        });

        this.options.onLocalStream?.(this.localStream);
        await this.connection.invoke('create-room', roomId);
    }

    async joinRoom(roomId: string): Promise<void> {
        this.ensureConnected();
        await this.connection.invoke('join-room', roomId);
    }

    async dispose(): Promise<void> {
        this.localStream?.getTracks().forEach(track => track.stop());
        this.localStream = null;

        for (const peerId of this.peerConnections.keys()) {
            this.closePeer(peerId);
        }

        if (this.connection.state !== HubConnectionState.Disconnected) {
            await this.connection.stop();
        }
    }

    private registerEvents(): void {
        this.connection.on('room-created', (message: { roomId: string }) => {
            this.options.onStatus?.(`Broadcasting room ${message.roomId}`);
        });

        this.connection.on(
            'joined-room',
            (message: { roomId: string; broadcasterId: string }) => {
                this.options.onStatus?.(`Joined room ${message.roomId}`);
            }
        );

        this.connection.on('viewer-joined', async (message: ViewerMessage) => {
            if (!this.localStream) {
                return;
            }

            const peerConnection = this.createPeerConnection(message.viewerId);

            for (const track of this.localStream.getTracks()) {
                peerConnection.addTrack(track, this.localStream);
            }

            const offer = await peerConnection.createOffer();
            await peerConnection.setLocalDescription(offer);

            await this.connection.invoke('offer', {
                target: message.viewerId,
                sdp: offer
            });
        });

        this.connection.on('offer', async (message: DescriptionMessage) => {
            const peerConnection = this.createPeerConnection(message.sender);
            await peerConnection.setRemoteDescription(message.sdp);
            await this.addPendingCandidates(message.sender, peerConnection);

            const answer = await peerConnection.createAnswer();
            await peerConnection.setLocalDescription(answer);

            await this.connection.invoke('answer', {
                target: message.sender,
                sdp: answer
            });
        });

        this.connection.on('answer', async (message: DescriptionMessage) => {
            const peerConnection = this.peerConnections.get(message.sender);

            if (!peerConnection) {
                return;
            }

            await peerConnection.setRemoteDescription(message.sdp);
            await this.addPendingCandidates(message.sender, peerConnection);
        });

        this.connection.on('ice-candidate', async (message: CandidateMessage) => {
            const candidate = new RTCIceCandidate(message.candidate);
            const peerConnection = this.peerConnections.get(message.sender);

            if (!peerConnection || !peerConnection.remoteDescription) {
                const candidates = this.pendingCandidates.get(message.sender) ?? [];
                candidates.push(candidate);
                this.pendingCandidates.set(message.sender, candidates);
                return;
            }

            await peerConnection.addIceCandidate(candidate);
        });

        this.connection.on('viewer-left', (message: ViewerMessage) => {
            this.closePeer(message.viewerId);
        });

        this.connection.on('broadcaster-left', () => {
            for (const peerId of this.peerConnections.keys()) {
                this.closePeer(peerId);
            }

            this.options.onStatus?.('The broadcaster ended the stream');
        });

        this.connection.on('streaming-error', (message: string) => {
            this.options.onStatus?.(message);
        });

        this.connection.onreconnecting(() => {
            this.options.onStatus?.('Reconnecting to streaming API');
        });

        this.connection.onreconnected(() => {
            this.options.onStatus?.('Reconnected; create or join the room again');
        });

        this.connection.onclose(() => {
            this.options.onStatus?.('Disconnected from streaming API');
        });
    }

    private createPeerConnection(peerId: string): RTCPeerConnection {
        const existingConnection = this.peerConnections.get(peerId);

        if (existingConnection) {
            return existingConnection;
        }

        const peerConnection = new RTCPeerConnection({
            iceServers: this.iceServers
        });

        peerConnection.onicecandidate = async event => {
            if (!event.candidate) {
                return;
            }

            await this.connection.invoke('ice-candidate', {
                target: peerId,
                candidate: event.candidate.toJSON()
            });
        };

        peerConnection.ontrack = event => {
            const remoteStream = event.streams[0];

            if (remoteStream) {
                this.options.onRemoteStream?.(remoteStream);
            }
        };

        peerConnection.onconnectionstatechange = () => {
            if (['failed', 'closed'].includes(peerConnection.connectionState)) {
                this.closePeer(peerId);
            }
        };

        this.peerConnections.set(peerId, peerConnection);
        return peerConnection;
    }

    private async addPendingCandidates(
        peerId: string,
        peerConnection: RTCPeerConnection
    ): Promise<void> {
        const candidates = this.pendingCandidates.get(peerId) ?? [];

        for (const candidate of candidates) {
            await peerConnection.addIceCandidate(candidate);
        }

        this.pendingCandidates.delete(peerId);
    }

    private closePeer(peerId: string): void {
        this.peerConnections.get(peerId)?.close();
        this.peerConnections.delete(peerId);
        this.pendingCandidates.delete(peerId);
    }

    private ensureConnected(): void {
        if (!this.isConnected) {
            throw new Error('Connect to the streaming API first.');
        }
    }
}
```

## 4. Use it from a Svelte component

Create a component such as `src/lib/StreamingRoom.svelte`:

```svelte
<script lang="ts">
    import { onMount } from 'svelte';
    import { StreamingClient } from './streamingClient';

    let roomId = 'demo-room';
    let status = 'Connecting';
    let videoElement: HTMLVideoElement;
    let client: StreamingClient;

    onMount(() => {
        client = new StreamingClient({
            apiUrl: 'http://localhost:3000',
            onLocalStream: stream => {
                videoElement.srcObject = stream;
                videoElement.muted = true;
            },
            onRemoteStream: stream => {
                videoElement.srcObject = stream;
                videoElement.muted = false;
            },
            onStatus: message => {
                status = message;
            }
        });

        client.connect().catch(error => {
            status = error instanceof Error ? error.message : 'Connection failed';
        });

        return () => {
            void client.dispose();
        };
    });

    async function startBroadcast() {
        try {
            await client.startBroadcast(roomId);
        } catch (error) {
            status = error instanceof Error ? error.message : 'Could not broadcast';
        }
    }

    async function joinRoom() {
        try {
            await client.joinRoom(roomId);
        } catch (error) {
            status = error instanceof Error ? error.message : 'Could not join room';
        }
    }
</script>

<label for="roomId">Room ID</label>
<input id="roomId" bind:value={roomId} />

<button type="button" onclick={startBroadcast}>Start broadcast</button>
<button type="button" onclick={joinRoom}>Join room</button>

<p>{status}</p>
<video bind:this={videoElement} autoplay playsinline controls></video>
```

For SvelteKit, keep client construction inside `onMount` as shown. WebRTC and browser
media APIs are unavailable during server-side rendering.

## SignalR event contract

The Svelte client invokes these server methods:

| Method | Payload |
| --- | --- |
| `create-room` | A room ID string |
| `join-room` | A room ID string |
| `offer` | `{ target, sdp }` |
| `answer` | `{ target, sdp }` |
| `ice-candidate` | `{ target, candidate }` |

The Svelte client listens for these server events:

| Event | Payload |
| --- | --- |
| `room-created` | `{ roomId }` |
| `joined-room` | `{ roomId, broadcasterId }` |
| `viewer-joined` | `{ viewerId }` |
| `viewer-left` | `{ viewerId }` |
| `offer` | `{ sender, sdp }` |
| `answer` | `{ sender, sdp }` |
| `ice-candidate` | `{ sender, candidate }` |
| `broadcaster-left` | No payload |
| `streaming-error` | Error message string |

## CORS configuration

The API currently accepts every origin for development. Before production, replace the
wildcard in the API's `appsettings.json` with the real Svelte application origins:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "https://stream.example.com"
    ]
  }
}
```

Restart the C# API after changing this configuration.

## Production requirements

- Serve both applications over HTTPS. Browsers only allow camera and microphone access
  from secure origins, with localhost as the development exception.
- Replace the example public STUN server with your own STUN and TURN configuration.
  TURN is required for users whose networks cannot establish a direct peer connection.
- Protect `create-room` and `join-room` with authentication before allowing public use.
- The current peer-to-peer design creates one broadcaster upload per viewer. Use an SFU
  when broadcasts need a large audience.
- Room membership is tied to a SignalR connection ID. After a reconnect, the client must
  create or join its room again.

See Microsoft's [SignalR JavaScript client documentation](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0)
for additional connection, logging, transport, and reconnection options.
