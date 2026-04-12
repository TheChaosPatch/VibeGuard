---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Threading.Channels
  acceptable:
    - System.Threading.SemaphoreSlim
    - Microsoft.AspNetCore.RateLimiting
  avoid:
    - name: Task.Run without bounded concurrency
      reason: Unbounded Task.Run calls share the default ThreadPool; a burst of CPU-bound work starves I/O completion and ASP.NET request handling.
minimum_versions:
  dotnet: "10.0"
---

# Resource Exhaustion Prevention — C#

## Library choice
`System.Threading.Channels` provides bounded producer-consumer channels that apply backpressure when full. `SemaphoreSlim` gates concurrent access to a limited resource. `Microsoft.AspNetCore.RateLimiting` middleware applies request-level rate limiting before work reaches the thread pool. For HTTP client concurrency, `IHttpClientFactory` manages `HttpClient` instances and their connection pools.

## Reference implementation
```csharp
using System.Threading.Channels;
using System.Threading;

// Bounded channel — producer blocks when full, providing backpressure.
public static class WorkQueue
{
    private static readonly Channel<WorkItem> _channel = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(capacity: 200)
        {
            FullMode    = BoundedChannelFullMode.Wait,   // backpressure to producers
            SingleReader = false,
            SingleWriter = false,
        });

    public static ChannelWriter<WorkItem> Writer => _channel.Writer;
    public static ChannelReader<WorkItem> Reader => _channel.Reader;
}

// Semaphore gates concurrent database connections to 20 max.
public sealed class ThrottledDbService(IDbConnection dbFactory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(20, 20);

    public async Task<int> QueryAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(5), ct))
            throw new InvalidOperationException("Database concurrency limit reached.");
        try
        {
            // Work is done inside the semaphore — max 20 concurrent.
            return await dbFactory.ExecuteScalarAsync<int>("SELECT 1", ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public record WorkItem(string Payload);
```

## Language-specific gotchas
- `Channel.CreateUnbounded<T>()` has no backpressure — at high throughput it consumes unbounded heap memory. Always prefer `CreateBounded` in production.
- `BoundedChannelFullMode.DropOldest` or `DropNewest` silently discard items when the channel is full. Use `Wait` so producers experience backpressure rather than losing work silently.
- `SemaphoreSlim.WaitAsync()` without a timeout suspends indefinitely. Always pass a `TimeSpan` or `CancellationToken` (or both).
- ASP.NET Core's `IHttpClientFactory` recycles `HttpMessageHandler` instances on a 2-minute interval by default. Avoid creating `new HttpClient()` per request — it exhausts socket handles.
- `Task.Run(() => CpuBoundWork())` uses the shared `ThreadPool`. For sustained CPU workloads, set `ThreadPool.SetMaxThreads` or use a dedicated `TaskScheduler` to avoid starving the I/O completion port threads.

## Tests to write
- Fill the bounded channel to capacity; assert the next `WriteAsync` waits (does not complete immediately) until a reader consumes one item.
- Concurrently call `QueryAsync` 21 times; assert the 21st call throws `InvalidOperationException` within the 5-second gate timeout.
- `Channel.CreateBounded` with `DropNewest` mode: write 201 items; assert the channel contains exactly 200.
- `IHttpClientFactory` integration: assert `HttpClient` instances are not created per request by verifying the handler lifetime via the DI container.
