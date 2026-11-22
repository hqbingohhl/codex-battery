using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexBattery {
    static class Program {
        [STAThread] public static int Main(string[] args) {
            bool test=args.Length>0 && (args[0]=="--self-test" || args[0]=="--ui-test");
            try {
                if(test) {
                    if(args[0]=="--self-test") CoreTest();
                    else WindowTest();
                    WriteReport(args,"PASS "+args[0]);
                    return 0;
                }
                UiText.Use(Preferences.Load().Language);
                bool first;
                using(var reveal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexBattery.Windows.Reveal"))
                using(var mutex=new Mutex(true,@"Local\CodexBattery.Windows",out first)) {
                    if(!first){
                        reveal.Set();
                        ExplainOlderInstance();
                        return 0;
                    }
                    try {
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        using(var form=new Dashboard()) {
                            var handle=form.Handle;
                            var listener=ThreadPool.RegisterWaitForSingleObject(reveal,delegate {
                                try {form.BeginInvoke((Action)form.Reveal);} catch(InvalidOperationException){}
                            },null,Timeout.Infinite,false);
                            try { Application.Run(form); } finally {listener.Unregister(null);}
                        }
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch(Exception e) {
                if(test) {WriteReport(args,"FAIL "+args[0]+"\n"+e);return 1;}
                MessageBox.Show(UiText.T(e.GetBaseException().Message),UiText.T("Codex Battery 启动失败"));
                return 1;
            }
        }
        // Windows may still be running the previous portable copy from another folder.
        static void ExplainOlderInstance() {
            using (var current = Process.GetCurrentProcess()) {
                var version = typeof(Program).Assembly.GetName().Version;
                foreach (var running in Process.GetProcessesByName(current.ProcessName)) {
                    using (running) {
                        try {
                            if (running.Id == current.Id || running.SessionId != current.SessionId) continue;
                            var info = running.MainModule.FileVersionInfo;
                            Version other;
                            if (info.ProductName == "Codex Battery" && Version.TryParse(info.FileVersion, out other) && other < version) {
                                MessageBox.Show(UiText.F("托盘中仍在运行 Codex Battery {0}。请在旧版设置中点「退出」，再双击新版 EXE。",other.ToString(2)), "Codex Battery " + version.ToString(2));
                                break;
                            }
                        } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                          catch (NotSupportedException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
                    }
                }
            }
        }
        static void WriteReport(string[] args,string text) {
            if(args.Length==2) File.WriteAllText(args[1],text+Environment.NewLine,new UTF8Encoding(false));
            Console.WriteLine(text);
        }
        static void CoreTest() {
            var json=new JavaScriptSerializer();
            var s=Snapshot.Parse(json.Deserialize<Dictionary<string,object>>(
                @"{""rateLimitsByLimitId"":{""codex"":{""primary"":{""usedPercent"":22.5,""windowDurationMins"":10080}}},""rateLimits"":{""primary"":{""usedPercent"":99}}}"));
            if(s.Select("secondary").Remaining!=77 || s.Select("minimum").Name!="每周额度") throw new InvalidDataException("Quota selection failed");
            IconArt.SelfTestBattery();
            TrayMenu.VerifyPlacement();
            PetSource.SelfTest();
            PetFallback.SelfTest();
            if(Environment.OSVersion.Platform==PlatformID.Win32NT) MacStyle.VerifyTypography();
            if(Environment.OSVersion.Platform==PlatformID.Win32NT) PetShortcut.SelfTest();
            using(var pose=new Bitmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","Pets","codex.png"))) {
                var icon=IconArt.ShortcutIcon(pose);
                using(var stream=new MemoryStream(icon)) using(var decoded=new Icon(stream,32,32))
                    if(decoded.Width!=32)throw new InvalidDataException("Shortcut icon size");
            }
        }
        static void WindowTest() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string temp=Path.Combine(Path.GetTempPath(),"CodexBatteryWindow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try {
                var pets=new PetSource(temp,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"),()=>new string[0]);
                var config = Path.Combine(temp, "config.toml");
                Action<string> selectPet = id => File.WriteAllText(config,"[desktop]\nselected-avatar-id = '" + id + "'\n");
                string mock=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","CheckUsageClient.exe");
                using(var form=new Dashboard(new Preferences{Window="secondary",SettingsFile=Path.Combine(temp,"prefs.json")},pets,
                          path=>new ProcessStartInfo(mock,"--mock weekly")))
                using(var wait=new System.Windows.Forms.Timer { Interval=100 }) {
                    Exception failure=null;
                    int phase=0;
                    var elapsed=Stopwatch.StartNew();
                    form.VerifyStartupPet();
                    wait.Tick+=delegate {
                        try {
                            if(elapsed.Elapsed>TimeSpan.FromSeconds(40))
                                throw new TimeoutException("Pet transition timed out at step " + phase);
                            if(phase==0 && form.NoPetConfigured) {
                                form.VerifyStartupPet();
                                selectPet("custom:missing-artwork");
                                phase=1;
                            } else if(phase==1 && (form.AwaitingWithoutPortrait || form.CurrentlyUsingFallback)) {
                                if (form.AwaitingWithoutPortrait) form.VerifyMissingArtwork();
                                else form.VerifyFallback();
                                selectPet("dewey");
                                phase=2;
                            } else if(phase==2 && form.HasFixtureData) {
                                form.VerifyWindow();
                                selectPet("none");
                                phase=3;
                            } else if(phase==3 && form.PetExplicitlyOff) {
                                wait.Stop();
                                form.Shutdown();
                            }
                        } catch(Exception e) {
                            failure=e;
                            wait.Stop();
                            form.Shutdown();
                        }
                    };
                    form.Shown+=delegate {wait.Start();};
                    Application.Run(form); if(failure!=null)throw failure;
                }
            } finally {Directory.Delete(temp,true);}
        }
    }
}
