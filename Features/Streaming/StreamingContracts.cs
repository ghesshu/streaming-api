namespace App.Features.Streaming;

public sealed record CreateRoomRequest(string? RoomId);

public sealed record CreateRoomResponse(
    string RoomId,
    string PublisherToken,
    string ViewerToken,
    string PublishUrl,
    string WatchUrl,
    string HlsUrl,
    string SharePath);

public sealed record MediaMtxAuthRequest(
    string? User,
    string? Password,
    string? Token,
    string? Ip,
    string? Action,
    string? Path,
    string? Protocol,
    string? Id,
    string? Query,
    string? UserAgent);
