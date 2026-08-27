namespace App.Features.Streaming;

public sealed class RoomRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, StreamingRoom> rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Participant> participants = new(StringComparer.Ordinal);

    public CreateRoomResult CreateRoom(string roomId, string connectionId)
    {
        lock (gate)
        {
            if (participants.ContainsKey(connectionId))
            {
                return CreateRoomResult.AlreadyInRoom;
            }

            if (rooms.ContainsKey(roomId))
            {
                return CreateRoomResult.RoomAlreadyExists;
            }

            rooms.Add(roomId, new StreamingRoom(roomId, connectionId));
            participants.Add(connectionId, new Participant(roomId, true));
            return CreateRoomResult.Created;
        }
    }

    public JoinRoomResult JoinRoom(string roomId, string connectionId, out string? broadcasterId)
    {
        lock (gate)
        {
            broadcasterId = null;

            if (participants.ContainsKey(connectionId))
            {
                return JoinRoomResult.AlreadyInRoom;
            }

            if (!rooms.TryGetValue(roomId, out var room))
            {
                return JoinRoomResult.RoomNotFound;
            }

            room.ViewerIds.Add(connectionId);
            participants.Add(connectionId, new Participant(roomId, false));
            broadcasterId = room.BroadcasterId;
            return JoinRoomResult.Joined;
        }
    }

    public bool CanRelay(string senderId, string targetId)
    {
        lock (gate)
        {
            if (!participants.TryGetValue(senderId, out var sender))
            {
                return false;
            }

            if (!participants.TryGetValue(targetId, out var target))
            {
                return false;
            }

            return sender.RoomId == target.RoomId && sender.IsBroadcaster != target.IsBroadcaster;
        }
    }

    public RoomStatus? GetRoomStatus(string roomId)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            return new RoomStatus(room.RoomId, room.ViewerIds.Count, true);
        }
    }

    public DisconnectionResult? Disconnect(string connectionId)
    {
        lock (gate)
        {
            if (!participants.Remove(connectionId, out var participant))
            {
                return null;
            }

            if (!rooms.TryGetValue(participant.RoomId, out var room))
            {
                return null;
            }

            if (participant.IsBroadcaster)
            {
                rooms.Remove(participant.RoomId);

                foreach (var viewerId in room.ViewerIds)
                {
                    participants.Remove(viewerId);
                }

                return new DisconnectionResult(participant.RoomId, true, null);
            }

            room.ViewerIds.Remove(connectionId);
            return new DisconnectionResult(participant.RoomId, false, room.BroadcasterId);
        }
    }

    private sealed record Participant(string RoomId, bool IsBroadcaster);

    private sealed class StreamingRoom(string roomId, string broadcasterId)
    {
        public string RoomId { get; } = roomId;
        public string BroadcasterId { get; } = broadcasterId;
        public HashSet<string> ViewerIds { get; } = new(StringComparer.Ordinal);
    }
}

public enum CreateRoomResult
{
    Created,
    AlreadyInRoom,
    RoomAlreadyExists
}

public enum JoinRoomResult
{
    Joined,
    AlreadyInRoom,
    RoomNotFound
}

public sealed record DisconnectionResult(
    string RoomId,
    bool WasBroadcaster,
    string? BroadcasterId);

public sealed record RoomStatus(
    string RoomId,
    int ViewerCount,
    bool IsLive);
