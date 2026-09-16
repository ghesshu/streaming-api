using System.ComponentModel.DataAnnotations;

namespace App.Features.Streaming;

public sealed class MediaMtxOptions
{
    public const string SectionName = "MediaMtx";

    [Required]
    [Url]
    public string WebRtcBaseUrl { get; init; } = "http://localhost:8889";

    [Required]
    [Url]
    public string HlsBaseUrl { get; init; } = "http://localhost:8888";

}
