using System.Text.Json;

namespace App.Features.Streaming;

public sealed record SessionDescriptionMessage(string Target, JsonElement Sdp);

public sealed record IceCandidateMessage(string Target, JsonElement Candidate);
