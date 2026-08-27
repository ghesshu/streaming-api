using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;

namespace App.Features.Streaming;

public sealed partial class StreamingHub(
    RoomRegistry roomRegistry,
    ILogger<StreamingHub> logger) : Hub
{
    [HubMethodName("create-room")]
    public async Task CreateRoom(string roomId)
    {
        roomId = roomId?.Trim() ?? string.Empty;

        if (!RoomIdPattern().IsMatch(roomId))
        {
            await SendError("Room IDs must contain 3 to 64 letters, numbers, or hyphens.");
            return;
        }

        var result = roomRegistry.CreateRoom(roomId, Context.ConnectionId);

        if (result == CreateRoomResult.AlreadyInRoom)
        {
            await SendError("This connection is already in a room.");
            return;
        }

        if (result == CreateRoomResult.RoomAlreadyExists)
        {
            await SendError("That room already exists.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        logger.LogInformation(
            "Room {RoomId} created by {ConnectionId}",
            roomId,
            Context.ConnectionId);

        await Clients.Caller.SendAsync(
            "room-created",
            new { roomId },
            Context.ConnectionAborted);
    }

    [HubMethodName("join-room")]
    public async Task JoinRoom(string roomId)
    {
        roomId = roomId?.Trim() ?? string.Empty;
        var result = roomRegistry.JoinRoom(roomId, Context.ConnectionId, out var broadcasterId);

        if (result == JoinRoomResult.AlreadyInRoom)
        {
            await SendError("This connection is already in a room.");
            return;
        }

        if (result == JoinRoomResult.RoomNotFound || broadcasterId is null)
        {
            await SendError("Room does not exist or has no broadcaster.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        logger.LogInformation(
            "Viewer {ConnectionId} joined room {RoomId}",
            Context.ConnectionId,
            roomId);

        await Clients.Client(broadcasterId).SendAsync(
            "viewer-joined",
            new { viewerId = Context.ConnectionId },
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(
            "joined-room",
            new { roomId, broadcasterId },
            Context.ConnectionAborted);
    }

    [HubMethodName("offer")]
    public Task Offer(SessionDescriptionMessage message)
    {
        return RelayDescription("offer", message);
    }

    [HubMethodName("answer")]
    public Task Answer(SessionDescriptionMessage message)
    {
        return RelayDescription("answer", message);
    }

    [HubMethodName("ice-candidate")]
    public async Task IceCandidate(IceCandidateMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Target) ||
            !roomRegistry.CanRelay(Context.ConnectionId, message.Target))
        {
            await SendError("The signaling target is not in your room.");
            return;
        }

        await Clients.Client(message.Target).SendAsync(
            "ice-candidate",
            new
            {
                candidate = message.Candidate,
                sender = Context.ConnectionId
            },
            Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("User connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = roomRegistry.Disconnect(Context.ConnectionId);

        if (result is not null)
        {
            if (result.WasBroadcaster)
            {
                await Clients.Group(result.RoomId).SendAsync("broadcaster-left");
                logger.LogInformation(
                    "Room {RoomId} deleted because its broadcaster left",
                    result.RoomId);
            }
            else if (result.BroadcasterId is not null)
            {
                await Clients.Client(result.BroadcasterId).SendAsync(
                    "viewer-left",
                    new { viewerId = Context.ConnectionId });
            }
        }

        logger.LogInformation("User disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task RelayDescription(string eventName, SessionDescriptionMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Target) ||
            !roomRegistry.CanRelay(Context.ConnectionId, message.Target))
        {
            await SendError("The signaling target is not in your room.");
            return;
        }

        await Clients.Client(message.Target).SendAsync(
            eventName,
            new
            {
                sdp = message.Sdp,
                sender = Context.ConnectionId
            },
            Context.ConnectionAborted);
    }

    private Task SendError(string message)
    {
        return Clients.Caller.SendAsync(
            "streaming-error",
            message,
            Context.ConnectionAborted);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoomIdPattern();
}
