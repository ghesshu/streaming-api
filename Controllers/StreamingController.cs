using App.Features.Streaming;
using Microsoft.AspNetCore.Mvc;

namespace App.Controllers;

[ApiController]
[Route("api/streaming")]
public sealed class StreamingController(RoomRegistry roomRegistry) : ControllerBase
{
    // This endpoint describes the HTTP and real-time parts of the streaming API.
    [HttpGet]
    public IActionResult GetService()
    {
        return Ok(new
        {
            service = "streaming-api",
            status = "available",
            signalRHub = "/streamingHub",
            roomStatusEndpoint = "/api/streaming/rooms/{roomId}"
        });
    }

    // A Svelte client can call this before joining to check whether a room is live.
    [HttpGet("rooms/{roomId}")]
    public IActionResult GetRoom(string roomId)
    {
        var room = roomRegistry.GetRoomStatus(roomId);

        if (room is null)
        {
            return NotFound(new
            {
                message = "Room does not exist or is no longer live."
            });
        }

        return Ok(room);
    }
}
