using App.Features.Streaming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace App.Controllers;

[ApiController]
[Route("api/streaming")]
public sealed class StreamingController(
    RoomRegistry roomRegistry,
    IOptions<MediaMtxOptions> mediaMtxOptions,
    IHubContext<StreamingHub> streamingHub) : ControllerBase
{
    private readonly MediaMtxOptions mediaMtx = mediaMtxOptions.Value;

    [HttpGet]
    public IActionResult GetService()
    {
        return Ok(new
        {
            service = "streaming-api",
            status = "available",
            mediaServer = "MediaMTX",
            createRoomEndpoint = "/api/streaming/rooms",
            signalRHub = "/streamingHub"
        });
    }

    [HttpPost("rooms")]
    public IActionResult CreateRoom(CreateRoomRequest request)
    {
        var roomId = request.RoomId?.Trim();

        if (string.IsNullOrWhiteSpace(roomId))
        {
            var generatedId = Guid.NewGuid().ToString("N")[..12];
            roomId = $"live-{generatedId}";
        }

        if (!RoomIdValidator.IsValid(roomId))
        {
            return BadRequest(new
            {
                message = "Room IDs must contain 3 to 64 letters, numbers, or hyphens."
            });
        }

        if (!roomRegistry.TryCreateRoom(roomId, out var createdRoom) || createdRoom is null)
        {
            return Conflict(new
            {
                message = "That room already exists."
            });
        }

        var webRtcBaseUrl = mediaMtx.WebRtcBaseUrl.TrimEnd('/');
        var hlsBaseUrl = mediaMtx.HlsBaseUrl.TrimEnd('/');

        var response = new CreateRoomResponse(
            createdRoom.RoomId,
            $"{webRtcBaseUrl}/{createdRoom.RoomId}/whip",
            $"{webRtcBaseUrl}/{createdRoom.RoomId}/whep",
            $"{hlsBaseUrl}/{createdRoom.RoomId}/index.m3u8",
            $"/watch/{createdRoom.RoomId}");

        return CreatedAtAction(
            nameof(GetRoom),
            new { roomId = createdRoom.RoomId },
            response);
    }

    [HttpGet("rooms/{roomId}")]
    public IActionResult GetRoom(string roomId)
    {
        var room = roomRegistry.GetRoomStatus(roomId);

        if (room is null)
        {
            return NotFound(new
            {
                message = "Room does not exist."
            });
        }

        return Ok(room);
    }

    [HttpDelete("rooms/{roomId}")]
    public async Task<IActionResult> DeleteRoom(string roomId)
    {
        if (!roomRegistry.RemoveRoom(roomId))
        {
            return NotFound(new
            {
                message = "Room does not exist."
            });
        }

        await streamingHub.Clients.Group(roomId).SendAsync(
            "room-ended",
            new { roomId },
            HttpContext.RequestAborted);

        return NoContent();
    }
}
