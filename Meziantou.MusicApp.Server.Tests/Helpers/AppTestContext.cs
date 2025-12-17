using Meziantou.MusicApp.Server.Services;
using Meziantou.Extensions.Logging.InMemory;
using Meziantou.Extensions.Logging.Xunit.v3;
using Meziantou.Framework;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.MusicApp.Server.Tests.Helpers;

internal sealed class AppTestContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _temporaryDirectory = TemporaryDirectory.Create();
    private readonly Stack<IAsyncDisposable> _disposables = new();
    private readonly CancellationTokenSource _cts;
    private WebApplicationFactory<Program> _factory = new();
    private bool _applicationStarted;

    public FullPath CachePath => _temporaryDirectory / "cache";
    public FullPath MusicCachePath => CachePath / "cache.json";
    public FullPath MusicPath => _temporaryDirectory / "music";
    public CancellationToken CancellationToken => _cts.Token;

    public MusicLibraryTestContext MusicLibrary { get; }

    public HttpClient Client
    {
        get
        {
            return field ??= _factory.CreateClient();
        }
    }

    public static AppTestContext Create()
    {
        return new AppTestContext();
    }

    private void UpdateFactory(Action<IWebHostBuilder> configuration)
    {
        if (_applicationStarted)
            throw new InvalidOperationException("The factory has already been started");

        _disposables.Push(_factory);
        _factory = _factory.WithWebHostBuilder(configuration);
    }

    private AppTestContext()
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        MusicLibrary = new(MusicPath);

        UpdateFactory(builder =>
        {
            builder.CaptureStartupErrors(captureStartupErrors: false);
            builder.SuppressStatusMessages(suppressStatusMessages: true);
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new XUnitLoggerProvider(new XUnitLoggerOptions() { TimestampFormat = "HH:mm:ss" }));
                logging.AddProvider(new InMemoryLoggerProvider(Logs));
            });

            builder.UseSetting("MusicServer:CachePath", CachePath);
            builder.UseSetting("MusicServer:MusicFolderPath", MusicPath);
        });
    }

    public InMemoryLogCollection Logs { get; } = new InMemoryLogCollection();

    public void Configure<T>(Action<T> configureOptions)
        where T : class
    {
        UpdateFactory(factory =>
        {
            factory.ConfigureServices(services =>
            {
                services.Configure(configureOptions);
            });
        });
    }

    public void ReplaceService<TService>(TService instance) where TService : class
    {
        UpdateFactory(builder =>
        {
            builder.ConfigureServices(builder =>
            {
                builder.RemoveAll<TService>();
                builder.AddSingleton(instance);
            });
        });
    }

    private void FinishInitialization()
    {
        _applicationStarted = true;
    }

    public T GetRequiredService<T>() where T : notnull
    {
        FinishInitialization();
        return _factory.Services.GetRequiredService<T>();
    }

    public IEnumerable<T> GetServices<T>() where T : notnull
    {
        FinishInitialization();
        return _factory.Services.GetServices<T>();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
        await _factory.DisposeAsync();
        while (_disposables.TryPop(out var disposable))
        {
            await disposable.DisposeAsync();
        }

        await _temporaryDirectory.DisposeAsync();
    }

    public void SetAuthToken(string authToken)
    {
        UpdateFactory(builder =>
        {
            builder.UseSetting("MusicServer:AuthToken", authToken);
        });
    }

    public async Task<MusicLibraryService> ScanCatalog()
    {
        var service = GetRequiredService<MusicLibraryService>();
        await service.InitialScanCompleted;
        return service;
    }
}