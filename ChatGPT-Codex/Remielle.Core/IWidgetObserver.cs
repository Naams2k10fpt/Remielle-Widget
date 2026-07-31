namespace Remielle.Core;

public interface IWidgetObserver : IAsyncDisposable
{
    event Action<WidgetEvent>? EventObserved;

    Task RunAsync(CancellationToken cancellationToken);
}
