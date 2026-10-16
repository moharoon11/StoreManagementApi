namespace StoreManagement.Api.Services;

public interface IEmailQueue
{
    bool Queue(Func<IEmailService, CancellationToken, Task> send);
}
