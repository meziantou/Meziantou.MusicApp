using System.Security.Cryptography;

namespace Meziantou.MusicApp.Server.Models;

internal static class ItemId
{
    public static string CreateSongId(string relativePath, DateTime lastWriteTime)
    {
        return CreateId("song", relativePath, lastWriteTime);
    }

    public static string CreateLyricsId(string path)
    {
        return CreateId("lyrics", path);
    }

    public static string CreateCoverId(string path)
    {
        return CreateId("cover", path);
    }

    public static string CreateArtistId(string artistName)
    {
        return CreateId("artist", artistName);
    }

    public static string CreateAlbumId(string albumKey)
    {
        return CreateId("album", albumKey);
    }

    public static string CreatePlaylistId(string relativePath)
    {
        return CreateId("playlist", relativePath);
    }

    public static string CreateDirectoryId(string path)
    {
        return CreateId("dir", path);
    }

    private static string CreateId(string context, string id, DateTime? lastWriteTime = null)
    {
        var input = lastWriteTime.HasValue
            ? $"{context}:{id}:{lastWriteTime.Value:O}"
            : $"{context}:{id}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
