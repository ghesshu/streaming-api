namespace App.Features.Streaming;

public sealed record CreateRoomRequest(string? RoomId);

public sealed record CreateRoomResponse(
    string RoomId,
    string PublishUrl,
    string WatchUrl,
    string HlsUrl,
    string SharePath);

public sealed record MediaMtxAuthRequest(
    string? Action,
    string? Path,
    string? Protocol);
