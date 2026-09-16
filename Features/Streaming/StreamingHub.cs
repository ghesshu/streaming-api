using Microsoft.AspNetCore.SignalR;

namespace App.Features.Streaming;

public sealed class StreamingHub(
    RoomRegistry roomRegistry,
    ILogger<StreamingHub> logger) : Hub
{
    [HubMethodName("join-room")]
    public async Task JoinRoom(string roomId)
    {
        roomId = roomId?.Trim() ?? string.Empty;

        var result = roomRegistry.JoinRoom(
            roomId,
            Context.ConnectionId,
            out var roomStatus);

        if (result == JoinRoomResult.AlreadyInRoom)
        {
            await SendError("This connection is already watching a room.");
            return;
        }

        if (result == JoinRoomResult.RoomNotFound)
        {
            await SendError("Room does not exist.");
            return;
        }

        if (roomStatus is null)
        {
            await SendError("Room could not be joined.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        logger.LogInformation(
            "Viewer {ConnectionId} joined room {RoomId}",
            Context.ConnectionId,
            roomId);

        await Clients.Caller.SendAsync(
            "joined-room",
            roomStatus,
            Context.ConnectionAborted);

        await Clients.Group(roomId).SendAsync(
            "viewer-count-changed",
            roomStatus,
            Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Presence connection opened: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = roomRegistry.DisconnectViewer(Context.ConnectionId);

        if (result is not null)
        {
            await Clients.Group(result.RoomId).SendAsync(
                "viewer-count-changed",
                new
                {
                    roomId = result.RoomId,
                    viewerCount = result.ViewerCount,
                    isCreated = true
                });
        }

        logger.LogInformation("Presence connection closed: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private Task SendError(string message)
    {
        return Clients.Caller.SendAsync(
            "streaming-error",
            message,
            Context.ConnectionAborted);
    }
}
