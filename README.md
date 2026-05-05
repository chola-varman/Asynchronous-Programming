# Asynchronous Programming in .NET Core

---



## Table of Contents

- [Core Concepts](#core-concepts)
- [How Async Execution Works](#how-async-execution-works)
- [Does `await` Block the Next Line?](#does-await-block-the-next-line)
- [Essential Patterns](#essential-patterns)
  - [async / await Basics](#asyncawait-basics)
  - [Parallel Async Work](#parallel-async-work)
  - [Task.WhenAny — Timeouts](#taskwhenany--timeouts)
- [CancellationToken — Always Propagate It](#cancellationtoken--always-propagate-it)
- [ConfigureAwait](#configureawait)
- [async void — Why It's Dangerous](#async-void--why-its-dangerous)
- [ValueTask\<T\> — Avoiding Heap Allocations](#valuetaskt--avoiding-heap-allocations)
- [Async Streams — IAsyncEnumerable\<T\>](#async-streams--iasyncenumerablet)
- [Common Mistakes to Avoid](#common-mistakes-to-avoid)
- [Quick Reference](#quick-reference)

---

## Getting started

```bash
git clone https://github.com/your-username/async-dotnet-demo.git
cd async-dotnet-demo
dotnet run
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download)


## Core Concepts

Async programming in .NET Core is built around the **Task-based Asynchronous Pattern (TAP)**, using `async` / `await` keywords.

| Concept | Description |
|---|---|
| `Task` | Represents an ongoing operation with no return value |
| `Task<T>` | Represents an ongoing operation that returns a value of type `T` |
| `async` | Marks a method as asynchronous |
| `await` | Suspends the method until the awaited operation completes — without blocking the thread |

```csharp
public async Task<string> FetchDataAsync(string url)
{
    using var client = new HttpClient();
    string result = await client.GetStringAsync(url);
    return result;
}
```

> **The state machine**: When you `await`, the compiler rewrites your method into a state machine. The method returns to the caller at the `await` point, and resumes when the awaited task completes.

---

## How Async Execution Works

```
Thread 1: [Running sync work] ----await----- [suspended] ----------- [resumed on callback]
                                     |                                       ^
                                     v                                       |
Thread pool:        [free to do other work while I/O is in progress]        |
                                                                             |
I/O device:                          [Network / disk / DB doing its thing] -+
```

**Key insight:**
- While waiting for I/O, the thread is **released back to the pool** — free to do other work.
- No thread is blocked. Scalability comes from this.
- For **CPU-bound** work, use `Task.Run()` to offload to a thread pool thread instead of blocking the caller.

---

## Does `await` Block the Next Line?

**Yes — within that method, the next line waits.**

```csharp
var a = await GetAAsync();  // pauses HERE until GetAAsync() finishes
var b = function();          // only runs AFTER 'a' has its value
```

The key distinction:

| What | Behaviour |
|---|---|
| The **method** | Suspended at the `await` line |
| The **thread** | NOT blocked — released back to the pool |
| The **next line** | Will not run until the awaited task completes |

Think of it like sending a text and waiting for a reply before writing your next message — **you** are paused, but you can still breathe.

---

## Essential Patterns

### async/await Basics

```csharp
// I/O-bound: await directly
public async Task<User> GetUserAsync(int id)
{
    return await _db.Users.FindAsync(id);
}

// CPU-bound: use Task.Run to avoid blocking the caller
public async Task<int> ComputeAsync(int[] data)
{
    return await Task.Run(() => data.Sum());
}
```

### Parallel Async Work

When you have multiple **independent** tasks, don't `await` them sequentially — run them concurrently:

```csharp
// Sequential — SLOW (3 seconds total)
var a = await GetAAsync();
var b = await GetBAsync();
var c = await GetCAsync();

// Concurrent — FAST (~1 second)
var taskA = GetAAsync();
var taskB = GetBAsync();
var taskC = GetCAsync();

await Task.WhenAll(taskA, taskB, taskC);

var (a, b, c) = (taskA.Result, taskB.Result, taskC.Result);
```

### Task.WhenAny — Timeouts

`Task.WhenAny` returns as soon as **the first task** in the list completes.

```csharp
var dataTask    = FetchDataAsync();
var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));

if (await Task.WhenAny(dataTask, timeoutTask) == timeoutTask)
    throw new TimeoutException();

return await dataTask;
```

**Breaking down `await Task.WhenAny(...) == timeoutTask`:**

| Part | What it does |
|---|---|
| `Task.WhenAny(dataTask, timeoutTask)` | Starts both tasks racing; returns a `Task<Task>` that completes when either finishes |
| `await` it | Gives you back the **winning Task object** (not its value) |
| `== timeoutTask` | Checks if the timeout won the race |

> **Note:** After the check, you still need to `await dataTask` to get the actual result — `WhenAny` only tells you *who won*.

**Race outcome:**

```
Scenario A — timeout fires first:
  dataTask:    [=======================...still running...]
  timeoutTask: [=========] ← WhenAny returns this → throw TimeoutException

Scenario B — data arrives first:
  dataTask:    [=======] ← WhenAny returns this → return await dataTask (success)
  timeoutTask: [==================...still counting...]
```

---

## CancellationToken — Always Propagate It

Without propagating the token, **cancellation is ignored mid-chain**. The DB query keeps running, the HTTP call keeps going, resources are wasted — even after the user cancelled.

**The call chain:**

```
Controller → ServiceA → ServiceB → HttpClient → DB query
```

**Pass the token all the way down:**

```csharp
// Controller
public async Task<IActionResult> Get(CancellationToken ct)
{
    var result = await _service.GetDataAsync(ct);  // passes token
    return Ok(result);
}

// Service
public async Task<Data> GetDataAsync(CancellationToken ct)
{
    var raw = await _repo.FetchAsync(ct);           // passes token
    return Transform(raw);
}

// Repository
public async Task<Raw> FetchAsync(CancellationToken ct)
{
    return await _db.ExecuteAsync(query, ct);       // token reaches DB driver
}
```

**What happens when the token is propagated:**
- User cancels (closes browser tab, request times out, etc.)
- DB query stops immediately
- HTTP call stops
- Everything cleans up — no wasted resources

> Especially important for expensive operations like large DB queries or external API calls.

**Creating and using a CancellationToken:**

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var result = await FetchAsync("https://api.example.com", cts.Token);
```

---

## ConfigureAwait

By default, `await` captures the current **synchronization context** (e.g. UI thread, ASP.NET request context) and resumes on it.

In **library code**, you usually don't need this — use `ConfigureAwait(false)` to avoid deadlocks and improve performance:

```csharp
// In library / service code — don't need the context
var data = await _repo.GetAsync(id).ConfigureAwait(false);

// In UI code or ASP.NET controllers — omit it (you need the context)
var data = await _repo.GetAsync(id);
```

> In **ASP.NET Core** there is no `SynchronizationContext`, so `ConfigureAwait(false)` is a no-op there — but it is still good practice in shared libraries.

---

## async void — Why It's Dangerous

### The problem: exceptions disappear

```csharp
// async Task — SAFE: caller can catch the exception
public async Task DoWorkAsync()
{
    throw new Exception("something broke");
}

try { await DoWorkAsync(); }
catch (Exception e) { /* caught! */ }


// async void — DANGEROUS: exception goes nowhere
public async void DoWork()
{
    throw new Exception("something broke");
}

DoWork();  // fire and forget — exception crashes the process or silently disappears
```

### The problem: you can't await it

```csharp
DoWork();               // returns immediately (void)
DoSomethingAfter();     // runs before DoWork() is actually done!
```

### The only valid use — event handlers

```csharp
// OK here: the event system is already fire-and-forget
button.Click += async (s, e) =>
{
    await LoadDataAsync();
};
```

| | `async Task` | `async void` |
|---|---|---|
| Can be awaited | Yes | No |
| Exception observable | Yes (stored in Task) | No (crashes / disappears) |
| Know when it finishes | Yes | No |
| Valid use case | Everything | Event handlers only |

---

## ValueTask\<T\> — Avoiding Heap Allocations

### The problem with `Task<T>`

Every call to an `async Task<T>` method allocates a `Task<T>` **object on the heap** — even if the result was already available immediately. In high-throughput APIs, this adds up.

```csharp
// Even on cache hit, a Task<User> object is still allocated on the heap
public async Task<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
        return user;  // result is ready, but Task allocation still happens

    return await _db.FindAsync(id);
}
```

### The solution: `ValueTask<T>`

`ValueTask<T>` is a **struct** (value type), not a class. When the result is synchronously available, it lives on the stack — no heap allocation.

```csharp
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
        return ValueTask.FromResult(user);  // no heap allocation — just a struct

    return new ValueTask<User>(FetchFromDbAsync(id));  // wraps a real Task only when needed
}
```

**Analogy:** `Task<T>` always puts the gift in a box, even if you're handing it over directly. `ValueTask<T>` skips the box when the gift is already in your hand.

### Rules for using `ValueTask<T>` correctly

- Do **not** `await` it more than once
- Do **not** store it in a field
- Do **not** use it in general code — only in hot paths where you've measured a real performance problem
- Default to `Task<T>` everywhere else (simpler, safer)

---

## Async Streams — IAsyncEnumerable\<T\>

For **streaming data** (reading a file line by line, server-sent events, paginated APIs):

```csharp
// Producer
public async IAsyncEnumerable<LogEntry> ReadLogsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var line in File.ReadLinesAsync("app.log", ct))
    {
        yield return ParseLogEntry(line);
    }
}

// Consumer
await foreach (var entry in ReadLogsAsync(ct))
{
    Console.WriteLine(entry.Message);
}
```

---

## Common Mistakes to Avoid

### 1. `async void` outside event handlers

```csharp
// BAD
public async void SaveData() { ... }

// GOOD
public async Task SaveDataAsync() { ... }
```

### 2. Blocking with `.Result` or `.Wait()`

```csharp
// BAD — can deadlock, blocks the thread
var result = GetDataAsync().Result;
var result = GetDataAsync().GetAwaiter().GetResult();

// GOOD
var result = await GetDataAsync();
```

### 3. `async` without `await`

```csharp
// BAD — runs synchronously, pointless async, compiler warning
public async Task<int> GetCountAsync()
{
    return 42;
}

// GOOD — return a completed task directly
public Task<int> GetCountAsync()
{
    return Task.FromResult(42);
}
```

### 4. Sequential awaits when tasks are independent

```csharp
// BAD — unnecessarily slow
var a = await GetAAsync();
var b = await GetBAsync();

// GOOD — run in parallel
await Task.WhenAll(GetAAsync(), GetBAsync());
```

---

## Quick Reference

### Return types

| Type | Use when |
|---|---|
| `Task` | Async method, no return value |
| `Task<T>` | Async method, returns a value |
| `ValueTask<T>` | Hot path, result often synchronously available |
| `async void` | Event handlers **only** |
| `IAsyncEnumerable<T>` | Streaming / yielding multiple results |

### Key APIs

| API | Purpose |
|---|---|
| `Task.WhenAll(...)` | Await all tasks; all must succeed |
| `Task.WhenAny(...)` | Await first task to finish |
| `Task.Run(...)` | Offload CPU-bound work to thread pool |
| `Task.FromResult(v)` | Wrap a sync value as a completed task |
| `Task.Delay(ms)` | Async wait / timeout |
| `CancellationTokenSource` | Create and control a cancellation token |

### Rules of thumb

| Scenario | Recommendation |
|---|---|
| I/O-bound work | `async` / `await` directly |
| CPU-bound work | `Task.Run()` |
| Library / shared code | `ConfigureAwait(false)` |
| Blocking calls (`.Result`, `.Wait()`) | Avoid — can deadlock |
| `async void` | Avoid — use `async Task` instead |
| Independent tasks | Run with `Task.WhenAll` — don't await sequentially |
| High-frequency sync results | Consider `ValueTask<T>` |

---

