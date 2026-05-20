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
       └─ ResponseCachingHandler             ← short-circuits on cache HIT
            └─ [LoggingHandler "1-after-cache"]
                 └─ Polly.WaitAndRetryAsync
                      └─ [LoggingHandler "2-after-retry"]
                           └─ HttpClientHandler (socket)
```

Each `LoggingHandler` logs a unique tag along with the request URI, attempt
number, elapsed time, and response status. `ResponseCachingHandler` logs every
cache HIT/MISS with the cache key. Combined with Aspire's OpenTelemetry
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
| 1 | First call to `GetByGenevaLogAccount` | `cache MISS` → all three layers (`0-outer` → `1-after-cache` → `2-after-retry`) fire, plus a real HTTP call. ~20 ms. |
| 2 | Second call with **same** args | `cache HIT` → only `[0-outer]` fires, request short-circuits at `ResponseCachingHandler`. 0 ms. |
| 3 | Second call with **different** args | `cache MISS` (different cache key) → all three layers fire. |
| 4 | Valid AppInsights ARM ID | `cache MISS` → 200 OK from the server. |
| 5 | Invalid AppInsights ARM ID | Server returns 400. Polly's retry policy **does not** retry — 4xx are not transient errors. The 400 is **not** cached (only 2xx are). Refit throws `ApiException`. |

## ⚠️ Two latent bugs in the original `TraceControlPlane.Client`'s cache wiring

While building this playground I discovered the `Policy.CacheAsync` registration
in the real `TraceControlPlane.Client` has two problems that compound each
other. This playground replaces that wiring with `ResponseCachingHandler`,
which avoids both. The bugs are worth understanding:

### 1. Polly `CacheAsync` needs `Context.OperationKey`

`Microsoft.Extensions.Http.Polly`'s `PolicyHttpMessageHandler` creates a fresh
`Polly.Context` per request **but never populates `OperationKey`**. Polly's
cache policy uses `Context.OperationKey` as the cache key — when it is empty,
every call is treated as unique and **nothing is ever cached**. The real
`TraceControlPlane.Client` ships with this issue; its cache is silently a
no-op.

### 2. Caching `HttpResponseMessage` directly is unsafe

Even if you fix #1 (by inserting a handler that sets `OperationKey` before the
cache policy), `Polly.CacheAsync<HttpResponseMessage>` caches the **same
instance** of `HttpResponseMessage`. Refit reads & disposes its `Content` on
the first call. On the second call, the cache returns the now-disposed
instance and Refit throws:

```
Refit.ApiException: An error occured deserializing the response.
 ---> System.ObjectDisposedException: Cannot access a disposed object.
      Object name: 'HttpConnectionResponseContent'.
```

### The fix in this repo

`ResponseCachingHandler` sidesteps both problems by treating the cache as a
plain HTTP-layer concern and caching a value-typed snapshot
`(StatusCode, Headers, Body bytes, ReasonPhrase)` keyed by
`Method + URI`. On each cache hit it constructs a **fresh** `HttpResponseMessage`
from the snapshot, so Refit safely owns and disposes its own copy. It also
single-flights concurrent misses for the same key onto one upstream call.

Only successful (2xx) GET responses are cached — 4xx/5xx and non-GET methods
go straight through.
