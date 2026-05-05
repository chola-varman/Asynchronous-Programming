using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncDemo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            while (true)
            {
                PrintMenu();
                var key = Console.ReadKey(true).Key;

                switch (key)
                {
                    case ConsoleKey.D1: await Demo_AwaitBlocksNextLine(); break;
                    case ConsoleKey.D2: await Demo_ParallelVsSequential(); break;
                    case ConsoleKey.D3: await Demo_WhenAnyTimeout(); break;
                    case ConsoleKey.D4: await Demo_CancellationToken(); break;
                    case ConsoleKey.D5: await Demo_AsyncVoid(); break;
                    case ConsoleKey.D6: await Demo_ValueTask(); break;
                    case ConsoleKey.D7: await Demo_AsyncStreams(); break;
                    case ConsoleKey.D8: await Demo_CommonMistakes(); break;
                    case ConsoleKey.Q:
                        Console.WriteLine("\n  Goodbye!\n");
                        return;
                    default:
                        Console.WriteLine("\n  Invalid choice. Press any key to try again.");
                        Console.ReadKey(true);
                        break;
                }

                Console.WriteLine("\n  Press any key to return to menu...");
                Console.ReadKey(true);
            }
        }

        // ─────────────────────────────────────────────────────────────
        static void PrintMenu()
        {
            Console.Clear();
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║        Async .NET Core — Interactive Demo        ║");
            Console.WriteLine("╠══════════════════════════════════════════════════╣");
            Console.WriteLine("║  1  Does await block the next line?              ║");
            Console.WriteLine("║  2  Sequential vs parallel async                 ║");
            Console.WriteLine("║  3  Task.WhenAny — timeout pattern               ║");
            Console.WriteLine("║  4  CancellationToken propagation                ║");
            Console.WriteLine("║  5  async void — why it's dangerous              ║");
            Console.WriteLine("║  6  ValueTask<T> — avoiding heap allocations     ║");
            Console.WriteLine("║  7  Async streams — IAsyncEnumerable<T>         ║");
            Console.WriteLine("║  8  Common mistakes                              ║");
            Console.WriteLine("║  Q  Quit                                         ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.Write("\n  Choose a demo: ");
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 1 — Does await block the next line?
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_AwaitBlocksNextLine()
        {
            PrintHeader("Does await block the next line?");

            Console.WriteLine("  The method is suspended at await — but the THREAD is not blocked.");
            Console.WriteLine("  The next line only runs once the awaited task completes.\n");

            Console.WriteLine("  [Step 1] Starting...");
            Console.WriteLine("  [Step 2] Calling: var a = await GetDataAsync()");

            string a = await SimulateIoAsync("Data from server", delayMs: 2000);

            Console.WriteLine($"  [Step 3] await returned. a = \"{a}\"");
            Console.WriteLine("  [Step 4] This line runs ONLY after Step 3 — not before.\n");

            Console.WriteLine("  ✓ The thread was free during the 2 second wait.");
            Console.WriteLine("    In a real app it would have handled other requests.");
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 2 — Sequential vs Parallel
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_ParallelVsSequential()
        {
            PrintHeader("Sequential vs Parallel async");

            // Sequential
            Console.WriteLine("  ── Sequential (awaiting one at a time) ──\n");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Console.WriteLine("  Awaiting task A...");
            var a = await SimulateIoAsync("A", 1000);
            Console.WriteLine("  Awaiting task B...");
            var b = await SimulateIoAsync("B", 1000);
            Console.WriteLine("  Awaiting task C...");
            var c = await SimulateIoAsync("C", 1000);

            sw.Stop();
            Console.WriteLine($"\n  Results: {a}, {b}, {c}");
            Console.WriteLine($"  Time taken: {sw.ElapsedMilliseconds}ms  (≈ 3 seconds — each waited in turn)\n");

            // Parallel
            Console.WriteLine("  ── Parallel (Task.WhenAll) ──\n");
            sw.Restart();

            Console.WriteLine("  Starting all three tasks simultaneously...");
            var taskA = SimulateIoAsync("A", 1000);
            var taskB = SimulateIoAsync("B", 1000);
            var taskC = SimulateIoAsync("C", 1000);

            await Task.WhenAll(taskA, taskB, taskC);

            sw.Stop();
            Console.WriteLine($"\n  Results: {taskA.Result}, {taskB.Result}, {taskC.Result}");
            Console.WriteLine($"  Time taken: {sw.ElapsedMilliseconds}ms  (≈ 1 second — all ran at the same time)");
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 3 — Task.WhenAny timeout
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_WhenAnyTimeout()
        {
            PrintHeader("Task.WhenAny — Timeout Pattern");

            Console.WriteLine("  Scenario A: data arrives BEFORE the timeout\n");
            await RunWithTimeout(dataDelayMs: 1000, timeoutMs: 3000);

            Console.WriteLine("\n  Scenario B: timeout fires BEFORE data arrives\n");
            await RunWithTimeout(dataDelayMs: 4000, timeoutMs: 2000);
        }

        static async Task RunWithTimeout(int dataDelayMs, int timeoutMs)
        {
            Console.WriteLine($"    Data delay: {dataDelayMs}ms  |  Timeout: {timeoutMs}ms");

            var dataTask = SimulateIoAsync("Server response", dataDelayMs);
            var timeoutTask = Task.Delay(timeoutMs);

            var winner = await Task.WhenAny(dataTask, timeoutTask);

            if (winner == timeoutTask)
            {
                Console.WriteLine("    ✗ Timeout fired first → TimeoutException would be thrown");
                Console.WriteLine($"    if (await Task.WhenAny(dataTask, timeoutTask) == timeoutTask)");
                Console.WriteLine($"        throw new TimeoutException();");
            }
            else
            {
                string result = await dataTask;  // safe to await again — already completed
                Console.WriteLine($"    ✓ Data arrived first → result: \"{result}\"");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 4 — CancellationToken propagation
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_CancellationToken()
        {
            PrintHeader("CancellationToken — Propagation");

            Console.WriteLine("  Scenario A: token NOT cancelled — completes normally\n");
            using (var cts = new CancellationTokenSource())
            {
                try
                {
                    var result = await Controller_GetData(cts.Token);
                    Console.WriteLine($"    ✓ Completed successfully: \"{result}\"");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("    ✗ Cancelled");
                }
            }

            Console.WriteLine("\n  Scenario B: token cancelled after 500ms (simulates user closing browser tab)\n");
            using (var cts = new CancellationTokenSource())
            {
                // Cancel after 500ms — the fake DB query takes 2000ms
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Console.WriteLine("    [User cancelled the request]");
                    cts.Cancel();
                });

                try
                {
                    var result = await Controller_GetData(cts.Token);
                    Console.WriteLine($"    ✓ Completed: \"{result}\"");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("    ✗ OperationCanceledException caught — DB query stopped early.");
                    Console.WriteLine("      No wasted resources. No orphaned queries.");
                }
            }
        }

        // Simulates: Controller → Service → Repository → DB
        static async Task<string> Controller_GetData(CancellationToken ct)
        {
            Console.WriteLine("    [Controller] Calling service...");
            return await Service_GetData(ct);
        }

        static async Task<string> Service_GetData(CancellationToken ct)
        {
            Console.WriteLine("    [Service]    Calling repository...");
            return await Repository_FetchFromDb(ct);
        }

        static async Task<string> Repository_FetchFromDb(CancellationToken ct)
        {
            Console.WriteLine("    [Repository] Running DB query (takes 2s)...");
            // ct.ThrowIfCancellationRequested() is what most DB drivers / HttpClient do internally
            await Task.Delay(2000, ct);  // respects cancellation
            return "User data from DB";
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 5 — async void
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_AsyncVoid()
        {
            PrintHeader("async void — Why It's Dangerous");

            Console.WriteLine("  ── async Task (SAFE) ──\n");
            Console.WriteLine("  Calling async Task method that throws...");
            try
            {
                await SafeAsyncTask();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✓ Exception caught: \"{ex.Message}\"");
            }

            Console.WriteLine("\n  ── async void (DANGEROUS) ──\n");
            Console.WriteLine("  Calling async void method that throws...");
            Console.WriteLine("  You cannot await it. You cannot catch its exceptions here.");

            // We use a flag to show the exception was unobservable by the caller
            bool callerKnewItFailed = false;
            try
            {
                DangerousAsyncVoid();  // can't await — returns immediately
                await Task.Delay(500); // wait a bit so the void method runs
                // Caller has no way to know it failed
            }
            catch
            {
                callerKnewItFailed = true;
            }

            Console.WriteLine(callerKnewItFailed
                ? "  Caller caught the exception (this won't print)"
                : "  ✗ Caller had NO IDEA it failed. Exception was unobservable.");

            Console.WriteLine("\n  ── Can't await async void ──\n");
            Console.WriteLine("  DangerousAsyncVoid();   // returns immediately");
            Console.WriteLine("  DoSomethingAfter();     // runs BEFORE void method finishes!\n");
            Console.WriteLine("  ── Valid use: event handler ──\n");
            Console.WriteLine("  button.Click += async (s, e) => {");
            Console.WriteLine("      await LoadDataAsync();  // OK inside event handler");
            Console.WriteLine("  };");
        }

        static async Task SafeAsyncTask()
        {
            await Task.Delay(100);
            throw new InvalidOperationException("Something broke inside SafeAsyncTask");
        }

        static async void DangerousAsyncVoid()
        {
            await Task.Delay(100);
            // In a real app this would crash the process or silently disappear
            // We swallow it here just so the demo doesn't actually crash
            try { throw new InvalidOperationException("Something broke inside DangerousAsyncVoid"); }
            catch { /* swallowed — caller never knows */ }
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 6 — ValueTask<T>
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_ValueTask()
        {
            PrintHeader("ValueTask<T> — Avoiding Heap Allocations");

            Console.WriteLine("  Scenario: GetUser called 5 times.");
            Console.WriteLine("  First call → cache miss → hits DB (slow).");
            Console.WriteLine("  Subsequent calls → cache hit → returns immediately (no allocation).\n");

            var repo = new UserRepository();

            for (int i = 1; i <= 5; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var user = await repo.GetUserAsync(userId: 1);
                sw.Stop();

                string source = i == 1 ? "DB (slow path — Task allocated)" : "Cache (fast path — ValueTask, no heap allocation)";
                Console.WriteLine($"  Call {i}: {user.Name,-12}  |  {sw.ElapsedMilliseconds,4}ms  |  {source}");
            }

            Console.WriteLine("\n  ✓ Calls 2-5 returned synchronously via ValueTask — no Task object allocated.");
            Console.WriteLine("    In a hot path (thousands of req/s) this removes GC pressure significantly.");
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 7 — Async Streams
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_AsyncStreams()
        {
            PrintHeader("Async Streams — IAsyncEnumerable<T>");

            Console.WriteLine("  Streaming 5 log entries one by one (each arrives 400ms apart).");
            Console.WriteLine("  The consumer processes each entry AS IT ARRIVES — no waiting for all.\n");

            using var cts = new CancellationTokenSource();

            int count = 0;
            await foreach (var entry in ReadLogsAsync(cts.Token))
            {
                Console.WriteLine($"  [{entry.Timestamp:HH:mm:ss.fff}]  {entry.Level,-7}  {entry.Message}");
                count++;
            }

            Console.WriteLine($"\n  ✓ Processed {count} log entries as a stream.");
            Console.WriteLine("    No List<T> was built in memory. Each item was yielded and consumed immediately.");
        }

        static async IAsyncEnumerable<LogEntry> ReadLogsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var entries = new[]
            {
                new LogEntry("INFO",  "Application started"),
                new LogEntry("INFO",  "Listening on port 5000"),
                new LogEntry("WARN",  "High memory usage detected"),
                new LogEntry("ERROR", "Failed to connect to cache"),
                new LogEntry("INFO",  "Reconnected to cache successfully"),
            };

            foreach (var entry in entries)
            {
                await Task.Delay(400, ct);  // simulate arriving over time
                yield return entry;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // DEMO 8 — Common Mistakes
        // ─────────────────────────────────────────────────────────────
        static async Task Demo_CommonMistakes()
        {
            PrintHeader("Common Mistakes");

            // Mistake 1: async without await
            Console.WriteLine("  ── Mistake 1: async without await ──\n");
            Console.WriteLine("  BAD:  public async Task<int> GetCount() { return 42; }");
            Console.WriteLine("        Runs synchronously, compiler warning, wasteful.\n");
            Console.WriteLine("  GOOD: public Task<int> GetCount() { return Task.FromResult(42); }");
            Console.WriteLine($"\n  Demo: Task.FromResult(99) → {await GoodSyncReturn()}\n");

            // Mistake 2: .Result / .Wait()
            Console.WriteLine("  ── Mistake 2: Blocking with .Result or .Wait() ──\n");
            Console.WriteLine("  BAD:  var x = GetDataAsync().Result;   // can deadlock!");
            Console.WriteLine("        var x = GetDataAsync().Wait();    // blocks the thread");
            Console.WriteLine("  GOOD: var x = await GetDataAsync();    // non-blocking\n");

            // Mistake 3: Sequential when parallel is possible
            Console.WriteLine("  ── Mistake 3: Awaiting independent tasks sequentially ──\n");
            Console.WriteLine("  BAD:  var a = await GetAAsync();   // 1s");
            Console.WriteLine("        var b = await GetBAsync();   // 1s  → total: 2s");
            Console.WriteLine("  GOOD: await Task.WhenAll(GetAAsync(), GetBAsync()); → total: 1s\n");

            // Mistake 4: Wrapping already-async in Task.Run
            Console.WriteLine("  ── Mistake 4: Wrapping I/O-bound work in Task.Run ──\n");
            Console.WriteLine("  BAD:  await Task.Run(() => client.GetStringAsync(url));");
            Console.WriteLine("        Task.Run is for CPU-bound work only.");
            Console.WriteLine("  GOOD: await client.GetStringAsync(url);");
        }

        static Task<int> GoodSyncReturn() => Task.FromResult(99);

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────
        static async Task<string> SimulateIoAsync(string result, int delayMs)
        {
            await Task.Delay(delayMs);
            return result;
        }

        static void PrintHeader(string title)
        {
            Console.Clear();
            Console.WriteLine($"╔══════════════════════════════════════════════════╗");
            Console.WriteLine($"║  {title.PadRight(48)}║");
            Console.WriteLine($"╚══════════════════════════════════════════════════╝\n");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Supporting types
    // ─────────────────────────────────────────────────────────────
    record User(int Id, string Name);

    record LogEntry(string Level, string Message)
    {
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    class UserRepository
    {
        private readonly Dictionary<int, User> _cache = new();

        public ValueTask<User> GetUserAsync(int userId)
        {
            // Cache hit — return synchronously, no Task allocation
            if (_cache.TryGetValue(userId, out var cached))
                return ValueTask.FromResult(cached);

            // Cache miss — must go to DB (async)
            return new ValueTask<User>(FetchFromDbAsync(userId));
        }

        private async Task<User> FetchFromDbAsync(int userId)
        {
            await Task.Delay(300);  // simulate DB latency
            var user = new User(userId, $"User_{userId}");
            _cache[userId] = user;  // populate cache
            return user;
        }
    }
}