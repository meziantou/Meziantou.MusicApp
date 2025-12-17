using Meziantou.MusicApp.Server.Middleware;
using Meziantou.MusicApp.Server.Models;
using Meziantou.MusicApp.Server.Models.Jellyfin;
using Meziantou.MusicApp.Server.Models.RestApi;
using Meziantou.MusicApp.Server.Models.Subsonic;
using Meziantou.MusicApp.Server.Services;
using Meziantou.AspNetCore.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.UseMeziantouConventions(options =>
{
    options.StaticAssets.Enabled = false;
});

builder.Services.AddHttpLogging(options =>
{
    options.CombineLogs = true;
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});

// Add services to the container.
// Configure common settings shared across all APIs
builder.Services.Configure<MusicServerSettings>(
    builder.Configuration.GetSection("MusicServer"));

builder.Services.Configure<SubsonicSettings>(
    builder.Configuration.GetSection("Subsonic"));

// Configure Jellyfin settings
builder.Services.Configure<JellyfinSettings>(
    builder.Configuration.GetSection("Jellyfin"));

// Configure REST API settings
builder.Services.Configure<RestApiSettings>(
    builder.Configuration.GetSection("RestApi"));

// Configure Last.fm settings
builder.Services.Configure<LastFmSettings>(
    builder.Configuration.GetSection("LastFm"));

builder.Services.AddControllers();

// Add response compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Enable CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowCredentials()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register the ReplayGain service
builder.Services.AddSingleton<ReplayGainService>();

// Register the music library service as a singleton hosted service
builder.Services.AddSingleton<MusicLibraryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MusicLibraryService>());

// Register the transcoding service
builder.Services.AddSingleton<TranscodingService>();

// Register the image resizing service
builder.Services.AddSingleton<ImageResizingService>();

// Register HttpClient for Last.fm
builder.Services.AddHttpClient("LastFm");

// Register the Last.fm service
builder.Services.AddSingleton<LastFmService>();

var app = builder.Build();

app.UseWhen(ctx => ctx.Request.Path.Value != "/health", app => app.UseHttpLogging());

// Enable response compression middleware
app.UseResponseCompression();

// Enable CORS middleware
app.UseCors();

app.MapGet("/", (MusicLibraryService library) => new
{
    Name = "Meziantou Music Server",
    Description = "A music server application using Subsonic backend.",
    Library = new
    {
        library.IsInitialScanCompleted,
        library.PlaylistCount,
        library.MusicFileCount,
        Percentage = library.ScanProgress,
        EstimatedCompletionTime = library.ScanEta,
    },
    Playlists = library.GetPlaylists().Select(p => new
    {
        p.Id,
        p.Name,
        TrackCount = p.SongCount,
    }).ToArray(),
});

// Add authentication middleware (REST API first, then Jellyfin, then Subsonic)
app.UseMiddleware<RestApiAuthMiddleware>();
app.UseMiddleware<JellyfinAuthMiddleware>();
app.UseMiddleware<SubsonicAuthMiddleware>();
app.MapControllers();
app.Run();
