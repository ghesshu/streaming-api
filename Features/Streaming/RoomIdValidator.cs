using System.Text.RegularExpressions;

namespace App.Features.Streaming;

public static partial class RoomIdValidator
{
    public static bool IsValid(string roomId)
    {
        return RoomIdPattern().IsMatch(roomId);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoomIdPattern();
}
