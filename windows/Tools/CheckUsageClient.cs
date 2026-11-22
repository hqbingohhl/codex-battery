using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using CodexBattery;

// Compile this file with UsageClient.cs, /target:exe and /r:System.Web.Extensions.dll.
// Without arguments it runs in isolation. Child processes provide the simulated app-server protocol.
class CheckUsageClient {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static Dictionary<string,object> Parse(string text) { return Json.Deserialize<Dictionary<string,object>>(text); }
    static void Check(bool ok, string test) { if (!ok) throw new Exception("Failed: " + test); }
    static void Reply(int id, object result) { Console.WriteLine(Json.Serialize(new { id = id, result = result })); }
    static int Mock(string mode) {
        string line;
        int reads = 0;
        while ((line = Console.ReadLine()) != null) {
            var rpc = Parse(line);
            var method = rpc["method"] as string;
            int id = rpc.ContainsKey("id") ? Convert.ToInt32(rpc["id"]) : -1;
            if (method == "initialize") { Reply(id, new { userAgent = "fixture" }); continue; }
            if (method == "initialized") continue;
            if (method != "account/rateLimits/read" || id <= 1) return 5;
            if (mode == "disconnect") return 0;
            if (mode == "timeout") continue;
            if (mode == "weekly") {
                var epoch = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);
                long reset = (long)(DateTime.UtcNow.AddDays(4).AddHours(1)-epoch).TotalSeconds;
                Reply(id, new { rateLimits = new { primary = new { usedPercent = 23, windowDurationMins = 10080, resetsAt = reset } } });
                continue;
            }
            if (mode == "auth") {
                Console.WriteLine(Json.Serialize(new { id = id, error = new { code = -32000, message = "401 test-private-detail" } })); continue;
            }
            if (rpc.ContainsKey("params")) {
                Console.WriteLine(Json.Serialize(new { id = id, error = new { code = -32602, message = "invalid params" } })); continue;
            }
            // Nonmatching errors and server-originated requests sharing a response ID must not end this read.
            Console.WriteLine(Json.Serialize(new { id = 9999, error = new { code = -1 } }));
            Console.WriteLine(Json.Serialize(new { id = id, method = "unrelated" }));
            reads++;
            Reply(id, new { rateLimitsByLimitId = new {
                codex = new { primary = new { usedPercent = reads == 1 ? 10 : 25, windowDurationMins = 300 }, secondary = new { usedPercent = 40, windowDurationMins = 10080 } },
                other = new { primary = new { usedPercent = 99 } }
            } });
            // First change is sparse. The second state's unchanged/model-specific pushes must be ignored.
            Console.WriteLine(Json.Serialize(new { method = "account/rateLimits/updated", @params = new { rateLimits = new { limitId = "other", primary = new { usedPercent = 92 } } } }));
            Console.WriteLine(Json.Serialize(new { method = "account/rateLimits/updated", @params = new { rateLimits = new { limitId = "codex", primary = new { usedPercent = 25 } } } }));
        }
        return 0;
    }
    sealed class Harness : IDisposable {
        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        public long Now;
        public UsageClient Client;
        public int Starts, Updates, Errors;
        public string Mode = "success", Error;
        public Snapshot Last;
        public Harness() {
            Client = new UsageClient(a => queue.Enqueue(a), s => { Last = s; Updates++; }, e => { Error = e; Errors++; },
                ignored => {
                    Starts++;
                    var location = Assembly.GetExecutingAssembly().Location;
                    return Environment.OSVersion.Platform == PlatformID.Win32NT
                        ? new ProcessStartInfo(location, "--mock " + Mode)
                        : new ProcessStartInfo(Environment.GetEnvironmentVariable("CB_TEST_MONO") ?? "mono", "\"" + location + "\" --mock " + Mode);
                }, () => Now);
        }
        public void Pump() { Action a; while (queue.TryDequeue(out a)) a(); }
        public void Until(Func<bool> done) {
            var timeout = Stopwatch.StartNew();
            while (!done() && timeout.Elapsed.TotalSeconds < 15) { Pump(); Thread.Sleep(10); }
            Check(done(), "expected protocol event; last error: " + Error);
        }
        public void Advance(long ms) { Now += ms; Client.Tick(null, 60); }
        public void Dispose() { Client.Dispose(); }
    }

    static void Test() {
        var weekly = Snapshot.Parse(Parse("{\"rateLimitsByLimitId\":null,\"rateLimits\":{\"primary\":{\"usedPercent\":22.5,\"windowDurationMins\":10080}}}"));
        Check(weekly.Select("secondary").Remaining == 77 && weekly.Primary.Name == "每周额度", "single weekly slot fallback");
        Check(Snapshot.Parse(Parse("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":50}}}")).Primary == null, "explicit empty bucket map");
        Check(Snapshot.Parse(Parse("{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":0}}}")).Primary == null, "legacy isolated bucket");
        Check(Quota.Parse(Parse("{\"usedPercent\":true}")) == null, "boolean rejected");
        var q = Quota.Parse(Parse("{\"usedPercent\":150,\"resetsAt\":1e20,\"windowDurationMins\":0}"));
        Check(q.Remaining == 0 && q.Reset == null && !q.Minutes.HasValue, "numerical bounds");
        var now = DateTime.Now;
        weekly.Primary.Reset = now.AddSeconds(-1);
        Check(weekly.IsStale(now.AddMinutes(-1), now, "secondary") && !weekly.IsStale(now, now, "primary"), "reset staleness");

        string fixture = Path.Combine(Path.GetTempPath(), "codex battery test " + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(fixture);
            string shim = Path.Combine(fixture, "codex.cmd");
            File.WriteAllText(shim, "This shim must not be executed");
            Check(CodexExecutable.Find(shim) == null, "unresolved command shim");
            string vendor = Path.Combine(fixture, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex");
            Directory.CreateDirectory(vendor);
            string exe = Path.Combine(vendor, "codex.exe"); File.WriteAllBytes(exe, new byte[0]);
            Check(CodexExecutable.Find(shim) == exe, "resolve installed npm shim to native binary");
        } finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }

        using (var h = new Harness()) {
            h.Client.Refresh(null);
            h.Client.Refresh(null);
            h.Until(() => h.Updates == 1);
            Check(h.Starts == 1 && h.Last.Primary.Remaining == 90 && h.Last.Secondary.Remaining == 60, "initial full snapshot, one subprocess");
            h.Until(() => { h.Advance(500); return h.Updates == 2; });
            Check(h.Last.Primary.Remaining == 75 && h.Last.Secondary.Remaining == 60 && h.Last.Select("minimum") == h.Last.Secondary, "authoritative update after sparse push");
            for (int i = 0; i < 30; i++) { h.Pump(); h.Advance(350); Thread.Sleep(10); }
            Check(h.Updates == 2 && h.Errors == 0, "push coalescing and model isolation");
            h.Client.Stop();
            h.Mode = "auth"; h.Client.Refresh(null);
            h.Until(() => h.Errors == 1);
            Check(h.Error.Contains("登录") && !h.Error.Contains("private"), "login guidance, no upstream sensitive message");
            h.Mode = "timeout"; h.Client.Refresh(null);
            h.Advance(26000);
            Check(h.Errors == 2 && h.Error.Contains("超时") && !h.Client.IsRefreshing, "bounded timeout");
            int attempts = h.Starts;
            h.Advance(61000);
            Check(h.Starts == attempts, "backoff after consecutive failures");
            h.Mode = "disconnect"; h.Client.Refresh(null);
            h.Until(() => h.Errors == 3);
            h.Mode = "success"; h.Client.Refresh(null);
            h.Until(() => h.Updates == 3);
            Check(h.Last.Primary.Remaining == 90, "manual reconnect");
        }
        Console.WriteLine("Usage checks passed: numeric bounds, weekly fallback, reset, handshake, legacy params, sparse push, bucket isolation, auth, timeout, backoff and reconnect.");
    }
    static void Live(string binary) {
        var queue = new ConcurrentQueue<Action>();
        Snapshot result = null;
        string error = null;
        using (var client = new UsageClient(a => queue.Enqueue(a), s => result = s, e => error = e,
                                            ignored => new ProcessStartInfo(binary, "app-server"), null)) {
            client.Refresh(null);
            var timer = Stopwatch.StartNew();
            while (error == null && result == null && timer.Elapsed.TotalSeconds < 30) {
                Action a;
                while (queue.TryDequeue(out a)) a();
                Thread.Sleep(10);
            }
            if (error != null || result == null) throw new Exception(error ?? "Timed out");
            var slots = new List<string>();
            if (result.Primary != null) slots.Add(result.Primary.Name);
            if (result.Secondary != null) slots.Add(result.Secondary.Name);
            Check(slots.Count > 0, "at least one published account quota");
            Console.WriteLine("Live app-server read passed (" + String.Join(", ", slots) + ").");
        }
    }
    public static int Main(string[] args) {
        if (args.Length == 2 && args[0] == "--mock") return Mock(args[1]);
        try { if (args.Length == 2 && args[0] == "--live") Live(args[1]); else Test(); return 0; } catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
}
