namespace App.Features.Streaming;

public sealed class RoomRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, StreamingRoom> rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> viewerRooms = new(StringComparer.Ordinal);

    public bool TryCreateRoom(string roomId, out CreatedRoom? createdRoom)
    {
        lock (gate)
        {
            if (rooms.ContainsKey(roomId))
            {
                createdRoom = null;
                return false;
            }

            rooms.Add(roomId, new StreamingRoom(roomId));
            createdRoom = new CreatedRoom(roomId);
            return true;
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

            return CreateStatus(room);
        }
    }

    public JoinRoomResult JoinRoom(
        string roomId,
        string connectionId,
        out RoomStatus? roomStatus)
    {
        lock (gate)
        {
            roomStatus = null;

            if (viewerRooms.ContainsKey(connectionId))
            {
                return JoinRoomResult.AlreadyInRoom;
            }

            if (!rooms.TryGetValue(roomId, out var room))
            {
                return JoinRoomResult.RoomNotFound;
            }

            room.ViewerIds.Add(connectionId);
            viewerRooms.Add(connectionId, roomId);
            roomStatus = CreateStatus(room);
            return JoinRoomResult.Joined;
        }
    }

    public ViewerDisconnection? DisconnectViewer(string connectionId)
    {
        lock (gate)
        {
            if (!viewerRooms.Remove(connectionId, out var roomId))
            {
                return null;
            }

            if (!rooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            room.ViewerIds.Remove(connectionId);
            return new ViewerDisconnection(roomId, room.ViewerIds.Count);
        }
    }

    public bool AuthorizeMedia(string roomId, string action)
    {
        lock (gate)
        {
            if (!rooms.ContainsKey(roomId))
            {
                return false;
            }

            return action is "publish" or "read" or "playback";
        }
    }

    public bool RemoveRoom(string roomId)
    {
        lock (gate)
        {
            if (!rooms.Remove(roomId, out var room))
            {
                return false;
            }

            foreach (var viewerId in room.ViewerIds)
            {
                viewerRooms.Remove(viewerId);
            }

            return true;
        }
    }

    private static RoomStatus CreateStatus(StreamingRoom room)
    {
        return new RoomStatus(room.RoomId, room.ViewerIds.Count, true);
    }

    private sealed class StreamingRoom(string roomId)
    {
        public string RoomId { get; } = roomId;
        public HashSet<string> ViewerIds { get; } = new(StringComparer.Ordinal);
    }
}

public enum JoinRoomResult
{
    Joined,
    AlreadyInRoom,
    RoomNotFound
}

public sealed record CreatedRoom(string RoomId);

public sealed record RoomStatus(
    string RoomId,
    int ViewerCount,
    bool IsCreated);

public sealed record ViewerDisconnection(
    string RoomId,
    int ViewerCount);
