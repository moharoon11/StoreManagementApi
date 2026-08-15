namespace StoreManagement.Api.Logging;

public interface IControllerFileLogger
{
    Task WriteAsync(string controllerName, object entry, CancellationToken cancellationToken = default);
}
