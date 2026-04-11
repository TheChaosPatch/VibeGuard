---
schema_version: 1
archetype: errors/error-handling
language: csharp
principles_file: _principles.md
libraries:
  preferred: built-in exceptions
  acceptable:
    - OneOf
    - FluentResults
  avoid:
    - name: Custom exception hierarchies with dozens of types
      reason: Hard to maintain; usually signals insufficient domain modeling.
minimum_versions:
  dotnet: "10.0"
---

# Error Handling — C#

## Library choice
Use the built-in exception system for unexpected failures. For expected domain failures that routinely drive flow control (validation rejection, "not found", "already exists"), a result type like `OneOf<T, TError>` or `FluentResults` keeps the happy path exception-free and makes intent explicit.

## Reference implementation
```csharp
public sealed record DomainError(string Code, string Message);

public sealed class OrderService(IOrderRepository orders, ILogger<OrderService> logger)
{
    public async Task<OneOf<Order, DomainError>> SubmitAsync(SubmitOrderRequest request)
    {
        try
        {
            var existing = await orders.GetAsync(request.OrderId);
            if (existing is not null)
            {
                return new DomainError("order.exists", $"Order {request.OrderId} already submitted.");
            }

            var order = Order.Create(request);
            await orders.SaveAsync(order);
            return order;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict saving order {OrderId}", request.OrderId);
            return new DomainError("order.conflict", "Order was modified concurrently; please retry.");
        }
    }
}
```

## Language-specific gotchas
- Prefer `async Task<T>` over `async void` — exceptions in `async void` tear down the process.
- `catch (Exception)` without re-throwing should be reserved for the outermost boundary (an `ExceptionHandlerMiddleware` in ASP.NET, for example). Elsewhere, catch specific types.
- Use `logger.LogError(ex, "...")` with a message template, not `logger.LogError(ex.ToString())`. The template lets structured logging extract fields.
- `throw;` preserves the stack trace. `throw ex;` resets it and makes bugs unfindable.
- `.Result` and `.Wait()` on tasks cause deadlocks in sync-over-async contexts. Always `await`.

## Tests to write
- Happy path returns the success branch of `OneOf<Order, DomainError>`.
- Duplicate order returns `order.exists` with the expected code.
- A simulated concurrency exception returns `order.conflict` and logs at Warning.
- The service never swallows an unrecognized exception — assert it propagates.
