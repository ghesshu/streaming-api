using System.Security.Cryptography;
using System.Text;
using App.Features.Streaming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace App.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/media-auth")]
public sealed class MediaMtxAuthController(
    RoomRegistry roomRegistry,
    IOptions<MediaMtxOptions> mediaMtxOptions,
    ILogger<MediaMtxAuthController> logger) : ControllerBase
{
    private readonly MediaMtxOptions mediaMtx = mediaMtxOptions.Value;

    [HttpPost]
    public IActionResult AuthorizeRequest(
        [FromQuery] string key,
        MediaMtxAuthRequest request)
    {
        if (!KeysMatch(key, mediaMtx.AuthCallbackKey))
        {
            logger.LogWarning("Rejected a MediaMTX callback with an invalid callback key");
            return Unauthorized();
        }

        var roomId = request.Path?.Trim() ?? string.Empty;
        var action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        var token = !string.IsNullOrWhiteSpace(request.Token)
            ? request.Token
            : request.Password ?? string.Empty;

        if (roomRegistry.AuthorizeMedia(roomId, action, token))
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

    private static bool KeysMatch(string providedKey, string expectedKey)
    {
        if (string.IsNullOrEmpty(providedKey) || string.IsNullOrEmpty(expectedKey))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);

        return providedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
