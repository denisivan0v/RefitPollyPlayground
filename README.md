# Refit + Polly Playground

A small .NET 8 Aspire project that mirrors the request pipeline used in
`PIE-Correlation-Pipeline-NET/src/TraceControlPlane.Client`, so you can see
**exactly** what Refit and Polly do at each step of an HTTP call.

## Layout

| Project | Role |
| --- | --- |
| `src/Playground.AppHost` | Aspire AppHost — orchestrates server + client console |
| `src/Playground.ServiceDefaults` | Shared OpenTelemetry / health / service-discovery defaults |
| `src/Playground.Contracts` | DTOs + Refit interface shared by server & client |
| `src/Playground.Server` | ASP.NET Core Web API serving static in-memory tracing configs |
| `src/Playground.Client` | Refit + Polly client library (mirrors `TraceControlPlane.Client`) |
| `src/Playground.ConsoleClient` | Console app that exercises the client and prints results |

## Pipeline visibility

The client wires up the following `HttpClient` handler chain (outermost → innermost):

```
HttpClient
  └─ [LoggingHandler "0-outer"]
       └─ (optional) PollyCacheKeyHandler           ← sets Context.OperationKey
            └─ Polly.CacheAsync<HttpResponseMessage>
                 └─ [LoggingHandler "1-after-cache"]
                      └─ Polly.WaitAndRetryAsync
                           └─ [LoggingHandler "2-after-retry"]
                                └─ HttpClientHandler (socket)
```

Each `LoggingHandler` logs a unique tag along with the request URI, attempt
number, elapsed time, and response status. Combined with Aspire's OpenTelemetry
dashboard, you see the entire pipeline for every Refit method invocation.

## Run

### Standalone (no Aspire dashboard needed)

```bash
# terminal 1
cd src/Playground.Server
ASPNETCORE_URLS=http://localhost:5050 dotnet run

# terminal 2
cd src/Playground.ConsoleClient
dotnet run
```

### With Aspire AppHost

```bash
cd src/Playground.AppHost
dotnet run
```

Open the Aspire dashboard URL printed in the console (typically
http://localhost:15110) to see traces, logs, and metrics for all components.

> **Note:** the AppHost uses the path-based `AddProject(name, projectPath)` overload
> so you do **not** need to install the deprecated `dotnet workload install aspire`.
> The Aspire 8.2.2 NuGet packages alone are enough.

## What the scenarios demonstrate

`Playground.ConsoleClient/Program.cs` runs five back-to-back scenarios. Watch
the pipeline-handler log lines to see how each one flows:

| # | Scenario | What you should see |
| --- | --- | --- |
| 1 | First call to `GetByGenevaLogAccount` | All three layers (`0-outer` → `1-after-cache` → `2-after-retry`) fire, plus a real HTTP call. ~20 ms. |
| 2 | Second call with **same** args | With cache effectively disabled (default), all three layers fire again — see footnote below. |
| 3 | Second call with **different** args | All three layers fire — different cache key (if caching worked). |
| 4 | Valid AppInsights ARM ID | 200 OK from the server. |
| 5 | Invalid AppInsights ARM ID | Server returns 400. Polly's retry policy **does not** retry — 4xx are not transient errors. Refit throws `ApiException`. |

## ⚠️ Lessons hidden in the original `TraceControlPlane.Client`

While building this playground I discovered the cache wiring in the real
`TraceControlPlane.Client` is **a latent no-op**, and "fixing" it the obvious
way exposes a second pitfall. The flag `EnableCacheKeyFix` and
`PollyCacheKeyHandler` exist to demonstrate both:

### 1. Polly `CacheAsync` needs `Context.OperationKey`

`Microsoft.Extensions.Http.Polly`'s `PolicyHttpMessageHandler` creates a fresh
`Polly.Context` per request **but never populates `OperationKey`**. Polly's
cache policy uses `Context.OperationKey` as the cache key — when it is empty,
every call is treated as unique and **nothing is ever cached**.

→ With `EnableCacheKeyFix = false` (default, matching the real repo), scenario
2 in the demo still issues a real HTTP request, even though there is a "cache".

→ Fix: add a tiny `DelegatingHandler` that sits **before** the cache policy
and sets `Context.OperationKey` to the request method + URI. That is what
`PollyCacheKeyHandler` does.

### 2. Caching `HttpResponseMessage` directly is unsafe

Set `EnableCacheKeyFix = true` and rerun — scenario 2 now **does** short-circuit
at the cache layer (you'll see only `[0-outer]` fire and zero ms latency).
But you also get:

```
Refit.ApiException: An error occured deserializing the response.
 ---> System.ObjectDisposedException: Cannot access a disposed object.
      Object name: 'HttpConnectionResponseContent'.
```

Why? `Polly.CacheAsync<HttpResponseMessage>` caches the **same instance** of
`HttpResponseMessage`. Refit reads & disposes its `Content` on the first call.
On the cache hit, Refit gets the already-disposed instance and crashes.

→ Real fix: cache **deserialized DTOs**, not `HttpResponseMessage`. Either:
  - put the cache at the Refit-interface level (decorate `ITracingConfigurationsApi`), or
  - cache a `(StatusCode, Headers, Body bytes)` snapshot and reconstruct a fresh
    `HttpResponseMessage` on each cache hit.

The current code in `TraceControlPlane.Client` ships with the first problem
unresolved — caching is silently disabled. If the no-op cache is intentional,
removing the `Policy.CacheAsync` registration would make that explicit and
remove ~10 lines of misleading code.
