using System.Collections.Concurrent;
using System.Text.Json;

namespace StoreManagement.Api.Logging;

public sealed class ControllerFileLogger : IControllerFileLogger
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _logDirectory;
    private readonly ILogger<ControllerFileLogger> _logger;

    public ControllerFileLogger(IWebHostEnvironment environment, ILogger<ControllerFileLogger> logger)
    {
        _logDirectory = Path.Combine(environment.ContentRootPath, "Logs", "Controllers");
        _logger = logger;
    }

    public async Task WriteAsync(string controllerName, object entry, CancellationToken cancellationToken = default)
    {
        var safeControllerName = string.Concat(controllerName.Where(char.IsLetterOrDigit));
        var filePath = Path.Combine(_logDirectory, $"{safeControllerName}.log");
        var fileLock = FileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        try
        {
            Directory.CreateDirectory(_logDirectory);
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

            await fileLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken);
            }
            finally
            {
                fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            // Logging must never prevent an API request from completing.
            _logger.LogError(ex, "Unable to write controller log for {ControllerName}", controllerName);
        }
    }
}
