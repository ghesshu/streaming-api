using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

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

            var publisherToken = CreateToken();
            var viewerToken = CreateToken();

            rooms.Add(
                roomId,
                new StreamingRoom(
                    roomId,
                    HashToken(publisherToken),
                    HashToken(viewerToken)));

            createdRoom = new CreatedRoom(roomId, publisherToken, viewerToken);
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
        string viewerToken,
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

            if (!TokenMatches(viewerToken, room.ViewerTokenHash) &&
                !TokenMatches(viewerToken, room.PublisherTokenHash))
            {
                return JoinRoomResult.InvalidToken;
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

    public bool AuthorizeMedia(string roomId, string action, string token)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room))
            {
                return false;
            }

            if (action == "publish")
            {
                return TokenMatches(token, room.PublisherTokenHash);
            }

            if (action is "read" or "playback")
            {
                return TokenMatches(token, room.ViewerTokenHash) ||
                    TokenMatches(token, room.PublisherTokenHash);
            }

            return false;
        }
    }

    public bool RemoveRoom(string roomId, string publisherToken)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room) ||
                !TokenMatches(publisherToken, room.PublisherTokenHash))
            {
                return false;
            }

            rooms.Remove(roomId);

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

    private static string CreateToken()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    private static bool TokenMatches(string token, byte[] expectedHash)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var actualHash = HashToken(token);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private sealed class StreamingRoom(
        string roomId,
        byte[] publisherTokenHash,
        byte[] viewerTokenHash)
    {
        public string RoomId { get; } = roomId;
        public byte[] PublisherTokenHash { get; } = publisherTokenHash;
        public byte[] ViewerTokenHash { get; } = viewerTokenHash;
        public HashSet<string> ViewerIds { get; } = new(StringComparer.Ordinal);
    }
}

public enum JoinRoomResult
{
    Joined,
    AlreadyInRoom,
    RoomNotFound,
    InvalidToken
}

public sealed record CreatedRoom(
    string RoomId,
    string PublisherToken,
    string ViewerToken);

public sealed record RoomStatus(
    string RoomId,
    int ViewerCount,
    bool IsCreated);

public sealed record ViewerDisconnection(
    string RoomId,
    int ViewerCount);
