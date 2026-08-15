using System.Text.Json;
using NLog;

namespace StoreManagement.Api.Logging;

public sealed class NLogControllerLogger : IControllerFileLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(string controllerName, object entry, CancellationToken cancellationToken = default)
    {
        var logEvent = new LogEventInfo(NLog.LogLevel.Info, controllerName, JsonSerializer.Serialize(entry, JsonOptions));
        logEvent.Properties["controller"] = controllerName;

        LogManager.GetLogger(controllerName).Log(logEvent);
        return Task.CompletedTask;
    }
}
