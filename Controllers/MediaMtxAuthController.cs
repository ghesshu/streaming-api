using App.Features.Streaming;
using Microsoft.AspNetCore.Mvc;

namespace App.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/media-auth")]
public sealed class MediaMtxAuthController(
    RoomRegistry roomRegistry,
    ILogger<MediaMtxAuthController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult AuthorizeRequest(MediaMtxAuthRequest request)
    {
        var roomId = request.Path?.Trim() ?? string.Empty;
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;

        if (roomRegistry.AuthorizeMedia(roomId, action))
        {
            return NoContent();
        }

        logger.LogInformation(
            "Denied MediaMTX {Action} request for room {RoomId} over {Protocol}",
            action,
            roomId,
            request.Protocol);

        return Unauthorized();
    }
}
