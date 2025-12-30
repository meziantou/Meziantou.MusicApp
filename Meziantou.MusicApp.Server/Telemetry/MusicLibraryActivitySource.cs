using System.Diagnostics;

namespace Meziantou.MusicApp.Server.Telemetry;

internal static class MusicLibraryActivitySource
{
    public const string ActivitySourceName = "Meziantou.MusicApp.Server.MusicLibrary";
    public static readonly ActivitySource Instance = new(ActivitySourceName, "1.0.0");
}
