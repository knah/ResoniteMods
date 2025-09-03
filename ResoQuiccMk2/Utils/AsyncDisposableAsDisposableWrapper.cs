using System.Runtime.CompilerServices;

namespace ResoQuiccMk2.Utils;

internal sealed class AsyncDisposableAsDisposableWrapper(
    IAsyncDisposable asyncDisposable,
    [CallerArgumentExpression(nameof(asyncDisposable))] string? message = null)
    : IDisposable
{
    public void Dispose()
    {
        asyncDisposable.DisposeAsync().LogFailure(taskName: message);
    }
}