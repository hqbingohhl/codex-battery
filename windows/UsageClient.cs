using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexBattery {
    public sealed class Quota {
        public double Used;
        public int? Minutes;
        public DateTime? Reset;
        public int Remaining { get { return (int)Math.Floor(100 - Used); } }
        public string Name { get {
            if (!Minutes.HasValue) return "未知周期";
            if (Minutes.Value == 10080) return "每周额度";
            return Minutes.Value % 60 == 0 ? Minutes.Value / 60 + " 小时额度" : Minutes.Value + " 分钟额度";
        } }
        internal static bool Number(object value, out double result) {
            result = 0;
            if (!(value is int || value is long || value is double || value is decimal || value is float)) return false;
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !Double.IsNaN(result) && !Double.IsInfinity(result);
        }
        public static Quota Parse(object value) {
            var fields = value as Dictionary<string, object>;
            double used, minutes, seconds;
            if (!Number(Snapshot.Field(fields, "usedPercent"), out used)) return null;
            var quota = new Quota { Used = Math.Min(100, Math.Max(0, used)) };
            if (Number(Snapshot.Field(fields, "windowDurationMins"), out minutes) &&
                minutes > 0 && minutes <= Int32.MaxValue && minutes == Math.Truncate(minutes))
                quota.Minutes = (int)minutes;
            if (Number(Snapshot.Field(fields, "resetsAt"), out seconds) && seconds > 0 && seconds <= 253402300799d)
                quota.Reset = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
            return quota;
        }
    }

    public sealed class Snapshot {
        public Quota Primary, Secondary;
        internal static object Field(Dictionary<string, object> dict, string key) {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value : null;
        }
        internal static bool IsCodex(Dictionary<string, object> fields) {
            object id = Field(fields, "limitId");
            return fields != null && (id == null || Object.Equals(id, "codex"));
        }
        public static Snapshot Parse(Dictionary<string, object> result) {
            Dictionary<string, object> fields;
            var buckets = Field(result, "rateLimitsByLimitId") as Dictionary<string, object>;
            if (buckets != null) {
                fields = Field(buckets, "codex") as Dictionary<string, object>;
            } else {
                fields = Field(result, "rateLimits") as Dictionary<string, object>;
            }
            if (!IsCodex(fields)) return new Snapshot();
            return new Snapshot { Primary = Quota.Parse(Field(fields, "primary")), Secondary = Quota.Parse(Field(fields, "secondary")) };
        }
        public Quota Select(string mode) {
            if (Primary == null || Secondary == null) return Primary ?? Secondary;
            if (mode == "minimum") return Primary.Remaining <= Secondary.Remaining ? Primary : Secondary;
            return mode == "secondary" ? Secondary : Primary;
        }
        public bool IsStale(DateTime lastUpdated, DateTime now, string mode) {
            var selected = Select(mode);
            return lastUpdated == DateTime.MinValue || now - lastUpdated >= TimeSpan.FromMinutes(3) ||
                   (selected != null && selected.Reset > lastUpdated && selected.Reset <= now);
        }
        internal bool ResetDue(DateTime lastUpdated, DateTime now) {
            return new[] { Primary, Secondary }.Any(q => q != null && q.Reset > lastUpdated && q.Reset <= now);
        }
        internal bool ChangedBy(Dictionary<string, object> notice) {
            foreach (string slot in new[] { "primary", "secondary" }) {
                if (!notice.ContainsKey(slot)) continue;
                var fields = notice[slot] as Dictionary<string, object>;
                if (fields == null) continue;
                var old = slot == "primary" ? Primary : Secondary;
                double n;
                if (old == null ||
                    (Quota.Number(Field(fields, "usedPercent"), out n) && old.Used != Math.Max(0, Math.Min(100, n))) ||
                    (Quota.Number(Field(fields, "windowDurationMins"), out n) && n != old.Minutes) ||
                    (Quota.Number(Field(fields, "resetsAt"), out n) &&
                     (!old.Reset.HasValue || Math.Abs((old.Reset.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds - n) > 0.01)))
                    return true;
            }
            return false;
        }
    }

    internal static class CodexExecutable {
        static string FileIn(string folder, string name) {
            if (String.IsNullOrWhiteSpace(folder)) return null;
            try {
                // Relative PATH entries and drive-relative paths depend on whichever directory launched the dashboard.
                string root = Path.GetPathRoot(folder);
                if (String.IsNullOrEmpty(root) || !root.EndsWith(Path.DirectorySeparatorChar.ToString())) return null;
                string file = Path.Combine(folder, name);
                return File.Exists(file) ? Path.GetFullPath(file) : null;
            } catch (ArgumentException) { return null; } catch (IOException) { return null; } catch (NotSupportedException) { return null; }
        }
        static string Npm(string folder) {
            var npm = Path.Combine(folder, "node_modules", "@openai");
            string[] triples = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") == "ARM64" ||
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") == "ARM64"
                ? new[] { "aarch64-pc-windows-msvc", "x86_64-pc-windows-msvc" }
                : new[] { "x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc" };
            foreach (string triple in triples) {
                var archPackage = triple.StartsWith("aarch64", StringComparison.Ordinal) ? "codex-win32-arm64" : "codex-win32-x64";
                foreach (string package in new[] {Path.Combine(npm, archPackage), Path.Combine(npm, "codex", "node_modules", "@openai", archPackage), Path.Combine(npm, "codex")}) {
                    var native = FileIn(package, Path.Combine("vendor", triple, "codex", "codex.exe"));
                    if (native != null) return native;
                }
            }
            return null;
        }
        static string Desktop(string app) {
            // Probe the CLI inside Electron's resources, never the desktop shell named Codex.exe.
            if (String.IsNullOrEmpty(app)) return null;
            foreach (string layout in new[] { @"resources\codex-cli\bin\codex.exe", @"resources\codex.exe", @"resources\codex-cli\codex.exe",
                                              @"app\resources\codex-cli\bin\codex.exe", @"app\resources\codex.exe", @"app\resources\codex-cli\codex.exe" }) {
                var file = FileIn(app, layout);
                if (file != null) return file;
            }
            return null;
        }
        static string Packaged() {
            const string repo = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            try {
                using (var packages = Registry.CurrentUser.OpenSubKey(repo)) {
                    if (packages == null) return null;
                    foreach (string name in packages.GetSubKeyNames().Where(n => n.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("OpenAI.ChatGPT_", StringComparison.OrdinalIgnoreCase))) {
                        using (var key = packages.OpenSubKey(name)) {
                            var candidate = key == null ? null : Desktop(key.GetValue("PackageRootFolder") as string);
                            if (candidate != null) return candidate;
                        }
                    }
                }
            } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
            return null;
        }
        static string Resolve(string path) {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase)) return Npm(Path.GetDirectoryName(Path.GetFullPath(path)));
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            string full = Path.GetFullPath(path);
            return Desktop(Path.GetDirectoryName(full)) ?? full;
        }
        internal static string Find(string chosen) {
            if (!String.IsNullOrWhiteSpace(chosen)) return Resolve(chosen);
            string appAlias = null;
            string aliasDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
            foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                var folder = part.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar);
                var cli = Resolve(FileIn(folder, "codex.exe")) ?? Resolve(FileIn(folder, "codex.cmd"));
                if (String.Equals(folder, aliasDirectory, StringComparison.OrdinalIgnoreCase)) {
                    appAlias = cli;
                    continue;
                }
                if (cli != null) return cli;
            }
            var npm = Npm(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            if (npm != null) return npm;
            var packaged = Packaged();
            if (packaged != null) return packaged;
            foreach (var name in new[] { "ChatGPT", "Codex" }) {
                foreach (var p in Process.GetProcessesByName(name)) {
                    try {
                        var file = Desktop(Path.GetDirectoryName(p.MainModule.FileName));
                        if (file != null) return file;
                    } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    finally { p.Dispose(); }
                }
                var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
                var installed = Desktop(Path.Combine(programs, name)) ?? Desktop(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name));
                if (installed != null) return installed;
            }
            return appAlias;
        }
    }

    // All state changes and callbacks run on the dashboard thread. Background pipe callbacks only enqueue messages.
    public sealed class UsageClient : IDisposable {
        readonly Action<Action> post;
        readonly Action<Snapshot> updated;
        readonly Action<string> failed;
        readonly Func<string, ProcessStartInfo> launch;
        readonly Func<long> clock;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4194304 };
        Process server;
        StreamWriter input;
        Snapshot snapshot;
        readonly Dictionary<string, object> pendingNotice = new Dictionary<string, object>();
        DateTime lastSuccess;
        long started, lastAttempt, nextNotice;
        int expected, seq = 1, generation, failures;
        bool ready, disposed, lightRead = true, noticeQueued;
        public bool IsRefreshing { get { return expected != 0; } }
        public UsageClient(Action<Action> post, Action<Snapshot> updated, Action<string> failed)
            : this(post, updated, failed, StartInfo, null) { }
        internal UsageClient(Action<Action> post, Action<Snapshot> updated, Action<string> failed, Func<string, ProcessStartInfo> launch, Func<long> clock) {
            this.post = post; this.updated = updated; this.failed = failed; this.launch = launch;
            var watch = Stopwatch.StartNew();
            this.clock = clock ?? delegate { return watch.ElapsedMilliseconds; };
        }
        static ProcessStartInfo StartInfo(string preferred) {
            var binary = CodexExecutable.Find(preferred);
            return binary == null ? null : new ProcessStartInfo(binary, "app-server");
        }
        public void Tick(string preferred, int seconds) {
            if (disposed) return;
            long now = clock();
            if (IsRefreshing) {
                if (now - started >= 25000) Fail("读取超时，请检查网络和 Codex 登录状态。");
                return;
            }
            long interval = Math.Min(300, Math.Max(1, seconds) * (1 << Math.Min(4, Math.Max(0, failures - 1)))) * 1000L;
            if ((noticeQueued && now >= nextNotice) || now - lastAttempt >= interval ||
                (ready && snapshot != null && snapshot.ResetDue(lastSuccess, DateTime.Now))) Refresh(preferred);
        }
        public void Refresh(string preferred) {
            if (disposed || IsRefreshing) return;
            lastAttempt = clock();
            noticeQueued = false;
            if (ready) { Request(); return; }
            int token = ++generation;
            try {
                var info = launch(preferred);
                if (info == null) { Fail("未找到 Codex CLI。请安装 Codex CLI，或选择其原生 codex.exe。"); return; }
                info.UseShellExecute = false; info.CreateNoWindow = true;
                info.RedirectStandardOutput = true; info.RedirectStandardError = true; info.RedirectStandardInput = true;
                info.StandardOutputEncoding = new UTF8Encoding(false); info.StandardErrorEncoding = new UTF8Encoding(false);
                info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var proc = new Process { StartInfo = info };
                proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    string line = e.Data;
                    post(delegate {
                        if (token != generation || disposed) return;
                        if (line == null) Fail("Codex 连接已关闭，将自动重试。");
                        else Receive(line);
                    });
                };
                proc.ErrorDataReceived += delegate { }; // Drain stderr without retaining account information.
                server = proc;
                proc.Start();
                input = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                Send(1, new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_battery_windows", title = "Codex Battery", version = "1.9.0" } } });
            } catch (Exception) { Fail("无法启动 Codex。请检查所选程序路径，或重新选择 codex.exe。"); }
        }
        bool Write(object rpc) {
            try { input.WriteLine(json.Serialize(rpc)); return true; }
            catch (IOException) { Fail("Codex 连接已中断，请刷新重试。"); return false; }
            catch (ObjectDisposedException) { Fail("Codex 连接已关闭，请刷新重试。"); return false; }
        }
        void Send(int id, object rpc) { expected = id; started = clock(); Write(rpc); }
        void Request() {
            int id = ++seq;
            if (lightRead) Send(id, new { id = id, method = "account/rateLimits/read", @params = new { excludeResetCreditDetails = true } });
            else Send(id, new { id = id, method = "account/rateLimits/read" });
        }
        void Receive(string line) {
            Dictionary<string,object> rpc;
            try { rpc = json.Deserialize<Dictionary<string,object>>(line); }
            catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
            if (rpc == null) return;
            string method = Snapshot.Field(rpc, "method") as string;
            if (method != null) {
                if (method == "account/rateLimits/updated") Notice(Snapshot.Field(rpc, "params") as Dictionary<string, object>);
                return;
            }
            double id;
            if (!IsRefreshing || !Quota.Number(Snapshot.Field(rpc, "id"), out id) || id != expected) return;
            var err = Snapshot.Field(rpc, "error") as Dictionary<string, object>;
            if (err != null) {
                if (ready && lightRead && Object.Equals(Snapshot.Field(err, "code"), -32602)) {
                    lightRead = false; Request(); return;
                }
                string hint = Convert.ToString(Snapshot.Field(err, "message")).ToLowerInvariant();
                Fail(hint.Contains("auth") || hint.Contains("login") || hint.Contains("401") ? "请先在 Codex 登录 ChatGPT 账号，再刷新额度。" : "Codex 暂时无法读取额度，请检查网络和账号后刷新。");
                return;
            }
            var result = Snapshot.Field(rpc, "result") as Dictionary<string, object>;
            if (result == null) { Fail("Codex 返回了无效的响应，请更新 CLI 后重试。"); return; }
            if (!ready) {
                ready = true;
                if (Write(new { method = "initialized", @params = new { } })) Request();
                return;
            }
            if (!(Snapshot.Field(result, "rateLimitsByLimitId") is Dictionary<string, object>) && !(Snapshot.Field(result, "rateLimits") is Dictionary<string, object>)) {
                Fail("Codex 没有返回额度字段，请更新 CLI 后刷新。"); return;
            }
            expected = 0;
            lastSuccess = DateTime.Now;
            failures = 0;
            snapshot = Snapshot.Parse(result);
            if (snapshot.ChangedBy(pendingNotice)) QueueRead();
            pendingNotice.Clear();
            updated(snapshot);
        }
        void QueueRead() { if (!noticeQueued) { noticeQueued = true; nextNotice = Math.Max(clock() + 300, lastAttempt + 5000); } }
        void Notice(Dictionary<string,object> args) {
            var notice = Snapshot.Field(args, "rateLimits") as Dictionary<string,object>;
            if (!Snapshot.IsCodex(notice)) return;
            if (!IsRefreshing) {
                if (snapshot == null || snapshot.ChangedBy(notice)) QueueRead();
                return;
            }
            // Retain only the newest supplied fields per slot while a read is in flight.
            foreach (string slot in new[] { "primary", "secondary" }) {
                var fields = Snapshot.Field(notice, slot) as Dictionary<string,object>;
                if (fields == null) continue;
                var merged = Snapshot.Field(pendingNotice, slot) as Dictionary<string,object> ?? new Dictionary<string,object>();
                foreach (string key in new[] { "usedPercent", "windowDurationMins", "resetsAt" }) if (fields.ContainsKey(key)) merged[key] = fields[key];
                pendingNotice[slot] = merged;
            }
        }
        void Fail(string text) {
            Stop();
            failures = Math.Min(failures + 1, 6);
            lastAttempt = clock();
            failed(text);
        }
        public void Stop() {
            ++generation; ready = false; lightRead = true; noticeQueued = false; pendingNotice.Clear(); seq = 1; expected = 0; snapshot = null;
            var proc = server;
            server = null;
            var writer = input;
            input = null;
            if (proc == null) return;
            ThreadPool.QueueUserWorkItem(delegate {
                try { if (writer != null) writer.Dispose(); } catch (IOException) { }
                try {
                    if (!proc.WaitForExit(1500)) { proc.Kill(); proc.WaitForExit(1500); }
                } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                finally { proc.Dispose(); }
            });
        }
        public void Dispose() { disposed = true; Stop(); }
    }
}
