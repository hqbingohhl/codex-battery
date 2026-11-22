using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexBattery {
    internal enum PetState { Reading, Unconfigured, Disabled, Selected, AwaitingArtwork, Unreadable }

    public sealed class PetSelection {
        public readonly string Name, Status;
        // The caller owns the detached bitmap. A null portrait also clears the previous pet.
        public readonly Bitmap Portrait;
        internal readonly PetState State;
        public bool AwaitingArtwork { get { return State == PetState.AwaitingArtwork; } }
        internal PetSelection(string name, string status, Bitmap portrait, PetState state) {
            Name = name; Status = status; Portrait = portrait; State = state;
        }
    }

    // Call Read on one background worker. No network, credentials or chat history are required.
    public sealed class PetSource {
        const int MaxSheet = 32 * 1024 * 1024;
        static readonly string[] Stock = { "codex", "dewey", "fireball", "hoots", "rocky", "seedy", "stacky", "bsod", "null-signal" };
        readonly string desktop, assets, savedPortrait;
        readonly Func<IEnumerable<string>> cacheFolders;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxSheet };
        Choice chosen;
        string selectionStamp, attempted, published, previousCloudUrl, previousRevision, previousBody;
        DateTime retryAfter;
        sealed class Choice {
            public bool Disabled;
            public string Id = "", Name = "", Owner = "", Account = "", User = "", Url = "", Revision = "", Status = "等待本机 ChatGPT 的 Pet 设置";
        }
        public PetSource() : this(DefaultDesktop(), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), WindowsCaches,
                                  Path.Combine(Preferences.Folder,"selected-pet-v1.cache")) {}
        internal PetSource(string desktop, string assets, Func<IEnumerable<string>> caches, string savedPortrait = null) {
            this.desktop = desktop; this.assets = assets; cacheFolders = caches;
            this.savedPortrait = savedPortrait;
        }
        static string DefaultDesktop() {
            string custom = Environment.GetEnvironmentVariable("CODEX_HOME");
            return !String.IsNullOrEmpty(custom) && Path.IsPathRooted(custom) ? custom :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }
        static IEnumerable<string> WindowsCaches() {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return WindowsCaches(roaming,local);
        }
        static string[] ChildDirectories(string path, string pattern) {
            try { return Directory.GetDirectories(path,pattern); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
        static IEnumerable<string> WindowsCaches(string roaming,string local) {
            var roots = new List<string> { roaming, local };
            var profiles = new List<string>();
            string packages = Path.Combine(local, "Packages");
            if (Directory.Exists(packages)) {
                foreach (string pattern in new[] { "OpenAI.Codex_*", "OpenAI.ChatGPT-Desktop_*", "OpenAI.ChatGPT_*" }) {
                    foreach (string dir in ChildDirectories(packages, pattern)) {
                        roots.Add(Path.Combine(dir, "LocalCache", "Roaming"));
                        roots.Add(Path.Combine(dir, "LocalCache", "Local"));
                        profiles.Add(Path.Combine(dir,"LocalState"));
                    }
                }
            }
            foreach (string root in roots)
                foreach (string app in new[] { "Codex", "ChatGPT" })
                    profiles.Add(Path.Combine(root,app));
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string profile in profiles.Where(Directory.Exists)) {
                var partitions = new List<string> { profile, Path.Combine(profile,"Default"), Path.Combine(profile,"User Data","Default") };
                partitions.AddRange(ChildDirectories(Path.Combine(profile,"User Data"),"Profile *"));
                partitions.AddRange(ChildDirectories(Path.Combine(profile,"Partitions"),"*"));
                foreach (string partition in partitions) {
                    string cache = Path.Combine(partition,"Cache");
                    foreach (string dir in new[] {Path.Combine(cache,"Cache_Data"),cache})
                        if (emitted.Add(dir) && Directory.Exists(dir)) yield return dir;
                }
            }
        }

        static string Stamp(string file) {
            var info = new FileInfo(file);
            return info.Exists ? info.LastWriteTimeUtc.Ticks + "/" + info.Length : "-";
        }
        static FileStream Open(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        static byte[] ReadBytes(string file, int limit) {
            using (var s = Open(file)) {
                long n = s.Length;
                if (n <= 0 || n > limit) throw new InvalidDataException("文件尺寸异常");
                using (var b = new BinaryReader(s)) {
                    byte[] data = b.ReadBytes((int)n);
                    if (data.Length != n) throw new EndOfStreamException();
                    return data;
                }
            }
        }
        Dictionary<string,object> ObjectFile(string file, int limit) {
            var data = json.DeserializeObject(Encoding.UTF8.GetString(ReadBytes(file, limit)).TrimStart('\uFEFF')) as Dictionary<string, object>;
            if (data == null) throw new InvalidDataException("设置格式异常");
            return data;
        }
        static object Field(Dictionary<string,object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        static Dictionary<string,object> Obj(Dictionary<string,object> d, string key) { return Field(d,key) as Dictionary<string,object>; }
        static string Str(Dictionary<string,object> d, string key) { return Field(d,key) as string; }

        Choice FromState(string state, string config) {
            var d = ObjectFile(state, MaxSheet);
            string owner = Str(d, "electron-renderer-content-owner");
            var atoms = String.IsNullOrEmpty(owner) ? null : Obj(d, "electron-owner-atoms:" + owner);
            if (atoms == null)
                return d.Keys.Any(x => x.StartsWith("electron-owner-atoms:",StringComparison.Ordinal)) ? new Choice() : Legacy(config);
            var selections = Obj(atoms, "last-observed-cloud-pet-selection-v1");
            if (selections == null || selections.Count == 0) return Legacy(config);
            var art = Obj(atoms, "cloud-pet-artwork-cache");
            string account = Str(art,"accountId"), user = Str(art,"userId"), id = null;
            foreach (var pair in selections) {
                IList parts;
                try { parts = json.DeserializeObject(pair.Key) as IList; }
                catch (ArgumentException) { continue; }
                catch (InvalidOperationException) { continue; }
                if (parts == null || parts.Count != 2 || String.IsNullOrEmpty(parts[0] as string) || String.IsNullOrEmpty(parts[1] as string)) continue;
                bool sameAccount = Equals(parts[0],account) && Equals(parts[1],user);
                if (sameAccount || (account == null && user == null && selections.Count == 1)) { id = pair.Value as string; break; }
            }
            if (String.IsNullOrEmpty(id)) return new Choice();
            var choice = Named(id);
            choice.Owner = owner;
            if (!id.StartsWith("pet_",StringComparison.Ordinal) || account == null || user == null) return choice;
            choice.Account = account; choice.User = user;
            var rows = Field(art, "pets") as IList ?? new object[0];
            var hit = rows.OfType<Dictionary<string,object>>().FirstOrDefault(row => Str(row,"id") == id);
            choice.Revision = Str(hit, "spritesheetFingerprint") ?? "";
            choice.Url = ValidAsset(Str(hit,"spritesheetUrl")) ?? "";
            if (!String.IsNullOrEmpty(Str(hit,"displayName"))) choice.Name = Str(hit,"displayName");
            return choice;
        }
        static string ValidAsset(string value) {
            Uri u;
            if (!Uri.TryCreate(value, UriKind.Absolute, out u) || u.Scheme != "https" || !u.IsDefaultPort ||
                u.UserInfo != "" || u.Fragment != "" ||
                !(u.Host.Equals("oaiusercontent.com",StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".oaiusercontent.com", StringComparison.OrdinalIgnoreCase))) return null;
            return u.GetLeftPart(UriPartial.Path);
        }
        static Choice Named(string id) {
            if (id == "default" || id == "none" || id == "") return new Choice { Disabled = true, Status = "ChatGPT 已关闭 Pet" };
            return new Choice { Id = id, Name = Stock.Contains(id) ? id : "自定义 Pet", Status = "等待 ChatGPT 加载所选 Pet 的图像" };
        }
        static Choice Legacy(string config) {
            if (!File.Exists(config)) return new Choice();
            bool section = false;
            using (var reader = new StringReader(Encoding.UTF8.GetString(ReadBytes(config, 1024 * 1024)))) {
                string row;
                while ((row = reader.ReadLine()) != null) {
                    row = row.Trim().TrimStart('\uFEFF');
                    if (row.StartsWith("[")) section = row.Split('#')[0].Trim() == "[desktop]";
                    int eq = row.IndexOf('=');
                    if (!section || eq < 0 || row.Substring(0,eq).Trim() != "selected-avatar-id") continue;
                    string v = row.Substring(eq + 1).TrimStart();
                    if (v.Length < 2 || (v[0] != '\'' && v[0] != '"')) break;
                    int end = v.IndexOf(v[0],1);
                    if (end > 0) return Named(v.Substring(1,end-1));
                }
            }
            return new Choice();
        }

        static bool CanRetry(Exception e) {
            return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException ||
                   e is InvalidOperationException || e is NotSupportedException || e is ExternalException ||
                   e is OutOfMemoryException || e is System.Security.SecurityException;
        }
        byte[] CodexArtwork() {
            using (var resource=typeof(PetSource).Assembly.GetManifestResourceStream("CodexBattery.Pets.codex.png")) {
                if(resource!=null) using(var reader=new BinaryReader(resource)) return reader.ReadBytes((int)resource.Length);
            }
            return ReadBytes(Path.Combine(assets,"Pets","codex.png"),MaxSheet);
        }
        internal Bitmap ReadStartupPortrait() {
            try { return Portrait(CodexArtwork(),false); }
            catch (Exception e) { if (!CanRetry(e)) throw; return null; }
        }
        public PetSelection Read(bool force = false) {
            try { return Check(force); }
            catch (Exception e) {
                if (!CanRetry(e)) throw;
                selectionStamp = null;
                var state = chosen != null && chosen.Id != "" ? PetState.AwaitingArtwork : PetState.Unreadable;
                string key = "io-error:" + state;
                if (published == key) return null;
                published = key; attempted = null;
                return new PetSelection("", "暂时无法读取本机 Pet，下次检查时重试", null, state);
            }
        }
        PetSelection Check(bool force) {
            string state = Path.Combine(desktop,".codex-global-state.json"), config = Path.Combine(desktop,"config.toml");
            string stamp = Stamp(state) + "/" + Stamp(config);
            if (selectionStamp != stamp || force) {
                chosen = null;
                chosen = File.Exists(state) ? FromState(state,config) : Legacy(config);
                selectionStamp = stamp;
            }
            Choice c = chosen;
            string file = null, name = c.Name;
            bool sheet = false;
            if (Stock.Contains(c.Id)) file = Path.Combine(assets,"Pets",c.Id + ".png");
            else if (c.Id.StartsWith("custom:",StringComparison.Ordinal)) {
                string slug = c.Id.Substring(7);
                if (SafeName(slug)) {
                    string dir = Path.Combine(desktop,"pets",slug), manifest = Path.Combine(dir,"pet.json");
                    if (Directory.Exists(dir) && File.Exists(manifest) && !Link(Path.Combine(desktop,"pets")) && !Link(dir) && !Link(manifest)) {
                        var fields = ObjectFile(manifest,65536);
                        string leaf = Str(fields,"spritesheetPath");
                        if (SafeName(leaf)) {
                            file = Path.Combine(dir,leaf); sheet = true;
                            name = Str(fields,"displayName") ?? slug;
                        }
                    }
                }
            }
            string key = json.Serialize(new[] { c.Owner,c.Account,c.User,c.Id,name,c.Status,c.Revision,c.Url,file,file == null ? null : Stamp(file) });
            if (!force && attempted == key && (published == key || DateTime.UtcNow < retryAfter)) return null;
            attempted = key;
            byte[] bytes = null;
            if (c.Id == "codex") bytes = CodexArtwork();
            else if (file != null && File.Exists(file) && !Link(file))
                bytes = ReadBytes(file, MaxSheet);
            byte[] cacheKey = BackupKey(c);
            Bitmap image = null;
            if (bytes == null && cacheKey != null) image = SavedImage(cacheKey);
            if (image == null && bytes == null && c.Url != "") { bytes = CloudSheet(c); sheet = true; }
            string status = c.Status;
            if (bytes != null) {
                try { image = Portrait(bytes,sheet); }
                catch (NotSupportedException) { status = "Pet 使用 WebP，此 Windows 尚无可用图像解码器"; }
                if (image != null && cacheKey != null && sheet) RememberVerifiedImage(cacheKey,c.Revision,bytes,image);
            }
            if (image != null) status = "已同步 · " + name;
            bool awaiting = image == null && c.Id != "";
            string result = awaiting ? key + "\n" + status : key;
            retryAfter = DateTime.UtcNow.AddSeconds(30);
            if (published == result) { if (image != null) image.Dispose(); return null; }
            published = result;
            PetState outcome = c.Disabled ? PetState.Disabled : c.Id == "" ? PetState.Unconfigured :
                               awaiting ? PetState.AwaitingArtwork : PetState.Selected;
            return new PetSelection(name,status,image,outcome);
        }
        // The app retains only the last verified cloud portrait. Its key contains no readable identifiers.
        byte[] BackupKey(Choice c) {
            if (savedPortrait == null || c.Owner == "" || c.Account == "" || c.User == "" ||
                !c.Id.StartsWith("pet_",StringComparison.Ordinal) || !c.Revision.StartsWith("[",StringComparison.Ordinal)) return null;
            byte[] data = Encoding.UTF8.GetBytes(json.Serialize(new[] { "selected-pet-v1",c.Owner,c.Account,c.User,c.Id,c.Revision }));
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }
        Bitmap SavedImage(byte[] key) {
            try {
                if (!File.Exists(savedPortrait) || Link(savedPortrait)) return null;
                byte[] file = ReadBytes(savedPortrait,2*1024*1024);
                if (file.Length <= key.Length || !key.SequenceEqual(file.Take(key.Length))) return null;
                var png = new byte[file.Length-key.Length];
                Buffer.BlockCopy(file,key.Length,png,0,png.Length);
                return Portrait(png,false);
            } catch (IOException) { } catch (InvalidDataException) { } catch (UnauthorizedAccessException) { }
              catch (ArgumentException) { } catch (ExternalException) { } catch (OutOfMemoryException) { }
            return null;
        }
        void RememberVerifiedImage(byte[] key,string revision,byte[] data,Bitmap image) {
            try {
                // ChatGPT fingerprints unpremultiplied RGBA canvas pixels, not encoded file bytes.
                // A decoder that rounds translucent colors differently simply skips this backup.
                if (Fingerprint(data) != revision) return;
                using (var output = new MemoryStream()) using (var png = new MemoryStream()) {
                    image.Save(png,ImageFormat.Png);
                    if (key.Length + png.Length > 2*1024*1024) return;
                    output.Write(key,0,key.Length); png.WriteTo(output);
                    Storage.WriteAtomic(savedPortrait,output.ToArray());
                }
            } catch (IOException) { } catch (InvalidDataException) { } catch (UnauthorizedAccessException) { }
              catch (ArgumentException) { } catch (ExternalException) { } catch (NotSupportedException) { } catch (OutOfMemoryException) { }
        }
        string Fingerprint(byte[] data) {
            int w,h; bool webp;
            if (!Dimensions(data,out w,out h,out webp) || SheetRows(w,h) == 0) return null;
            using (var stream = new MemoryStream(data,false))
            using (var decoded = webp ? WebpCell(data,w,h) : new Bitmap(stream))
            using (var sha = SHA256.Create()) {
                var pixels = decoded.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try {
                    byte[] row = new byte[4*w];
                    for (int y=0;y<h;y++) {
                        Marshal.Copy(IntPtr.Add(pixels.Scan0,y*pixels.Stride),row,0,row.Length);
                        for (int x=0;x<row.Length;x+=4) {
                            byte blue=row[x]; row[x]=row[x+2]; row[x+2]=blue;
                        }
                        sha.TransformBlock(row,0,row.Length,row,0);
                    }
                    sha.TransformFinalBlock(new byte[0],0,0);
                    return json.Serialize(new object[] {w,h,sha.Hash.Select(b => (int)b).ToArray()});
                } finally { decoded.UnlockBits(pixels); }
            }
        }
        static bool SafeName(string name) {
            if (String.IsNullOrEmpty(name) || name.Length > 180 || name == "." || name == ".." ||
                name.EndsWith(".") || name.EndsWith(" ") || name.IndexOfAny("/\\:\0".ToCharArray()) >= 0 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            string baseName = name.Split('.')[0].ToUpperInvariant();
            return baseName != "CON" && baseName != "AUX" && baseName != "NUL" && baseName != "PRN" &&
                   !(baseName.Length == 4 && "123456789".IndexOf(baseName[3]) >= 0 &&
                     (baseName.StartsWith("LPT") || baseName.StartsWith("COM")));
        }
        static bool Link(string file) { return (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0; }
        static string Digest(byte[] data) {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        }
        byte[] CloudSheet(Choice c) {
            string ownerUrl = json.Serialize(new[] {c.Owner,c.Account,c.User,c.Id,c.Url});
            Func<byte[],bool> accepts = body => {
                if (body == null) return false;
                int width,height; bool webp;
                if (!Dimensions(body,out width,out height,out webp) || SheetRows(width,height) == 0) return false;
                string digest = Digest(body);
                if (ownerUrl == previousCloudUrl && c.Revision != previousRevision && digest == previousBody) return false;
                previousCloudUrl = ownerUrl; previousRevision = c.Revision; previousBody = digest;
                return true;
            };
            foreach (string folder in cacheFolders()) {
                try {
                    string index = Path.Combine(folder,"index");
                    if (File.Exists(index) && File.Exists(Path.Combine(folder,"data_1"))) {
                        byte[] block = BlockSheet(folder,index,c.Url,accepts);
                        if (block != null) return block;
                    }
                    foreach (string file in Directory.EnumerateFiles(folder,"*_0")) {
                        try {
                            byte[] body = CacheEntry(file,c.Url);
                            if (accepts(body)) return body;
                        } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                } catch (IOException) { } catch (InvalidDataException) { } catch (UnauthorizedAccessException) { }
            }
            return null;
        }
        static bool MatchesCacheKey(string key,string url) {
            int pos = key.IndexOf(url,StringComparison.Ordinal), end = pos + url.Length;
            return pos >= 0 && (pos == 0 || key[pos-1] == ' ' || System.Text.RegularExpressions.Regex.IsMatch(key.Substring(0,pos),@"^[0-9]+/[0-9]+/$")) &&
                   (end == key.Length || key[end] == '?');
        }
        // Chromium's Windows blockfile cache. Traverse only indexed entries, and open a
        // response body only after matching its complete request URL. Stream zero is HTTP metadata.
        static byte[] BlockSheet(string folder,string index,string url,Func<byte[],bool> accepts) {
            byte[] table = ReadBytes(index,8*1024*1024);
            if (table.Length < 368 || BitConverter.ToUInt32(table,0) != 0xc103cac3) return null;
            int version = BitConverter.ToInt32(table,4), buckets = BitConverter.ToInt32(table,28);
            if (version != 0x20000 && version != 0x20001 && version != 0x30000) return null;
            if (buckets == 0) buckets = 65536;
            if (buckets < 0 || 368L + 4L*buckets > table.Length) return null;
            var seen = new HashSet<uint>();
            var opened = new Dictionary<string,BinaryReader>();
            try {
                for (int bucket=0;bucket<buckets;bucket++) {
                    uint address = BitConverter.ToUInt32(table,368+4*bucket);
                    while ((address >> 28) == 10 && seen.Count < 131072 && seen.Add(address)) {
                        int capacity = 256 * (1+(int)((address >> 24) & 3));
                        byte[] entry = BlockBytes(folder,address,capacity,opened);
                        if (entry == null) break;
                        address = BitConverter.ToUInt32(entry,4);
                        int len = BitConverter.ToInt32(entry,32);
                        if (BitConverter.ToInt32(entry,20) != 0 || len < url.Length || len > 65535) continue;
                        uint longKey = BitConverter.ToUInt32(entry,36);
                        string key;
                        if (longKey == 0) {
                            if (len >= entry.Length-96) continue;
                            key = Encoding.UTF8.GetString(entry,96,len);
                        } else {
                            byte[] data = BlockBytes(folder,longKey,len,opened);
                            if (data == null) continue;
                            key = Encoding.UTF8.GetString(data);
                        }
                        if (!MatchesCacheKey(key,url)) continue;
                        int size = BitConverter.ToInt32(entry,44);
                        if (size <= 0 || size > MaxSheet) continue;
                        byte[] body = BlockBytes(folder,BitConverter.ToUInt32(entry,60),size,opened);
                        if (accepts(body)) return body;
                    }
                }
                return null;
            } finally { foreach (var reader in opened.Values) reader.Dispose(); }
        }
        static byte[] BlockBytes(string folder,uint address,int count,Dictionary<string,BinaryReader> opened) {
            if ((address & 0x80000000) == 0) return null;
            int type = (int)((address >> 28) & 7);
            if (type == 0) {
                string external = Path.Combine(folder,"f_" + (address & 0x0fffffff).ToString("x6"));
                if (!File.Exists(external)) return null;
                using (var r = new BinaryReader(Open(external))) {
                    if (r.BaseStream.Length < count) return null;
                    byte[] data = r.ReadBytes(count);
                    return data.Length == count ? data : null;
                }
            }
            if (type < 2 || type > 4 || (address & 0x0c000000) != 0) return null;
            int size = 256 << (2*(type-2)), number = (int)((address >> 16) & 255);
            if (count > size * (1 + ((address >> 24) & 3))) return null;
            string path = Path.Combine(folder,"data_"+number);
            BinaryReader reader;
            if (!opened.TryGetValue(path,out reader)) {
                if (!File.Exists(path)) return null;
                reader = new BinaryReader(Open(path));
                opened.Add(path,reader);
            }
            reader.BaseStream.Position = 0;
            if (reader.BaseStream.Length < 8192 || reader.ReadUInt32() != 0xc104cac3 || reader.ReadUInt32() != 0x20000 ||
                (reader.ReadUInt32() & 65535) != number || reader.ReadInt32() != size) return null;
            long start = 8192 + (address & 65535) * (long)size;
            if (start+count > reader.BaseStream.Length) return null;
            reader.BaseStream.Position = start;
            byte[] result = reader.ReadBytes(count);
            return result.Length == count ? result : null;
        }
        // Chromium simple cache v5, stream 1. Skip unrelated responses and all HTTP metadata.
        static byte[] CacheEntry(string path, string url) {
            using (var s = Open(path)) using (var r = new BinaryReader(s)) {
                if (s.Length < 72 || s.Length > MaxSheet + 2097152 || r.ReadUInt64() != 0xfcfb6d1ba7725c30UL || r.ReadUInt32() != 5) return null;
                uint length = r.ReadUInt32();
                if (length == 0 || length > 65535 || 24 + length >= s.Length) return null;
                s.Position = 24;
                string key = Encoding.UTF8.GetString(r.ReadBytes((int)length));
                if (!MatchesCacheKey(key,url)) return null;
                const ulong eof = 0xf4fa6f45970d41d8UL;
                s.Position = s.Length - 24;
                if (r.ReadUInt64() != eof) return null;
                int digest = (r.ReadUInt32() & 2) == 0 ? 0 : 32;
                r.ReadUInt32();
                long marker = s.Length - 48 - digest - r.ReadUInt32(), start = 24L + length, n = marker - start;
                if (n <= 0 || n > MaxSheet) return null;
                s.Position = marker;
                if (r.ReadUInt64() != eof) return null;
                s.Position = start;
                byte[] body = r.ReadBytes((int)n);
                return body.Length == n ? body : null;
            }
        }
        static int Big(byte[] b,int i) { return (b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3]; }
        static int Little24(byte[] b,int i) { return b[i]|(b[i+1]<<8)|(b[i+2]<<16); }
        static bool Dimensions(byte[] bytes, out int w, out int h, out bool webp) {
            w = h = 0; webp = false;
            if (bytes.Length < 24) return false;
            if (BitConverter.ToUInt64(bytes,0) == 0x0a1a0a0d474e5089UL && Encoding.ASCII.GetString(bytes,12,4) == "IHDR") {
                w = Big(bytes,16); h = Big(bytes,20);
            } else if (Encoding.ASCII.GetString(bytes,0,4) == "RIFF" && Encoding.ASCII.GetString(bytes,8,4) == "WEBP") {
                webp = true;
                string type = Encoding.ASCII.GetString(bytes,12,4);
                if (type == "VP8X" && bytes.Length >= 30) { w = 1 + Little24(bytes,24); h = 1 + Little24(bytes,27); }
                if (type == "VP8L" && bytes.Length >= 25 && bytes[20] == 0x2f) {
                    uint bits = BitConverter.ToUInt32(bytes,21); w = 1 + (int)(bits & 16383); h = 1 + (int)((bits >> 14) & 16383);
                }
                if (type == "VP8 " && bytes.Length >= 30 && Little24(bytes,23) == 0x2a019d) {
                    w = BitConverter.ToUInt16(bytes,26) & 16383; h = BitConverter.ToUInt16(bytes,28) & 16383;
                }
            }
            return w > 0 && h > 0 && w <= 4096 && h <= 4096;
        }
        static int SheetRows(int w,int h) {
            int rows = w*208*9 == h*192*8 ? 9 : w*208*11 == h*192*8 ? 11 : 0;
            return rows != 0 && w % 8 == 0 && h % rows == 0 ? rows : 0;
        }
        static Bitmap Portrait(byte[] data,bool sheet) {
            int w,h; bool webp;
            if (!Dimensions(data,out w,out h,out webp)) return null;
            int rows = sheet ? SheetRows(w,h) : 1;
            if (rows == 0) return null;
            int cw = sheet ? w/8 : w, ch = sheet ? h/rows : h;
            Bitmap cell;
            if (webp) cell = WebpCell(data,cw,ch);
            else using (var s = new MemoryStream(data,false)) using (var bmp = new Bitmap(s)) {
                if (bmp.Width != w || bmp.Height != h) return null;
                cell = bmp.Clone(new Rectangle(0,0,cw,ch),PixelFormat.Format32bppArgb);
            }
            if (!sheet) return cell;
            using (cell) {
                int left = cw, top = ch, right = -1, bottom = -1;
                for (int y=0; y<ch; y++) for (int x=0; x<cw; x++) if (cell.GetPixel(x,y).A != 0) {
                    left = Math.Min(left,x); right = Math.Max(right,x); top = Math.Min(top,y); bottom = y;
                }
                return right < left ? null : cell.Clone(Rectangle.FromLTRB(left,top,right+1,bottom+1),PixelFormat.Format32bppArgb);
            }
        }
        // WPF delegates WebP to the installed Windows Imaging Component codecs. Late binding
        // keeps the PNG-only path usable without WPF and avoids shipping an unmanaged decoder.
        static Bitmap WebpCell(byte[] bytes,int w,int h) {
            try {
                var core = Assembly.Load("PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                var windows = Assembly.Load("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                const string imaging = "System.Windows.Media.Imaging.";
                var decoder = core.GetType(imaging+"BitmapDecoder",true);
                var flags = core.GetType(imaging+"BitmapCreateOptions",true);
                var cache = core.GetType(imaging+"BitmapCacheOption",true);
                using (var s = new MemoryStream(bytes,false)) {
                    var decoded = decoder.GetMethod("Create",new[] {typeof(Stream), flags, cache}).Invoke(null,
                        new object[] {s,Enum.Parse(flags,"PreservePixelFormat"),Enum.Parse(cache,"OnLoad")});
                    var frame = ((IList)decoder.GetProperty("Frames").GetValue(decoded,null))[0];
                    var format = core.GetType("System.Windows.Media.PixelFormats", true).GetProperty("Bgra32").GetValue(null,null);
                    var converted = core.GetType(imaging+"FormatConvertedBitmap",true);
                    var bgra = Activator.CreateInstance(converted, new object[] {frame,format,null,0.0});
                    var rect = windows.GetType("System.Windows.Int32Rect",true);
                    var crop = Activator.CreateInstance(rect,new object[] {0,0,w,h});
                    byte[] pixels = new byte[4*w*h];
                    converted.GetMethod("CopyPixels", new[] {rect,typeof(Array),typeof(int),typeof(int)}).Invoke(
                        bgra,new object[] {crop,pixels,w*4,0});
                    var bmp = new Bitmap(w,h,PixelFormat.Format32bppArgb);
                    try {
                        var bits = bmp.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,bmp.PixelFormat);
                        try {
                            for (int y=0;y<h;y++) Marshal.Copy(pixels,y*w*4,IntPtr.Add(bits.Scan0,y*bits.Stride),w*4);
                        } finally { bmp.UnlockBits(bits); }
                        return bmp;
                    } catch { bmp.Dispose(); throw; }
                }
            } catch (Exception e) {
                if (e is OutOfMemoryException) throw;
                throw new NotSupportedException("WebP codec unavailable",e);
            }
        }
        // Runs against private fixtures; never changes the user's real ChatGPT selection.
        public static void SelfTest() {
            string folder = Path.Combine(Path.GetTempPath(),"CodexBatteryPet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                string cacheFolder = Path.Combine(folder,"Cache_Data");
                Directory.CreateDirectory(cacheFolder);
                var src = new PetSource(folder, Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets"), () => new[] {cacheFolder});
                Action<bool> require = ok => { if (!ok) throw new InvalidDataException("Pet fixture check failed"); };
                string bare = Path.Combine(folder,"standalone");
                Directory.CreateDirectory(bare);
                var bareSource = new PetSource(bare,Path.Combine(bare,"absent-assets"),()=>new string[0]);
                File.WriteAllText(Path.Combine(bare,"config.toml"),"[desktop]\nselected-avatar-id='codex'\n");
                using(var first=bareSource.ReadStartupPortrait()) {
                    var synced=bareSource.Read();
                    using(var selected=synced.Portrait)
                        require(first!=null && selected!=null && first.Size==selected.Size && synced.State==PetState.Selected);
                }
                var noState = src.Read();
                require(noState != null && noState.Portrait == null && noState.State == PetState.Unconfigured);
                string state = Path.Combine(folder,".codex-global-state.json");
                var cache = new Dictionary<string,object> { {"accountId","a"}, {"userId","u"} };
                var choices = new Dictionary<string,object> { {"[\"a\",\"u\"]","dewey"} };
                var owner = new Dictionary<string,object> { {"last-observed-cloud-pet-selection-v1",choices}, {"cloud-pet-artwork-cache",cache} };
                var jsonState = new Dictionary<string,object> { {"electron-renderer-content-owner","owner"}, {"electron-owner-atoms:owner",owner} };
                Action persist = () => File.WriteAllText(state,src.json.Serialize(jsonState));
                persist(); var builtIn = src.Read(true);
                require(builtIn != null && builtIn.Name == "dewey" && builtIn.Portrait != null && !builtIn.AwaitingArtwork);
                builtIn.Portrait.Dispose();
                choices["[\"a\",\"u\"]"] = "pet_fixture";
                var record = new Dictionary<string,object> { {"id","pet_fixture"}, {"spritesheetFingerprint","fixture-version"} };
                cache["pets"] = new[] {record}; persist();
                var missing = src.Read(true);
                require(missing != null && missing.AwaitingArtwork && missing.Portrait==null);
                owner.Remove("cloud-pet-artwork-cache"); persist();
                var noArtwork = src.Read(true);
                require(noArtwork != null && noArtwork.AwaitingArtwork && noArtwork.Portrait==null);
                choices["[\"second\",\"account\"]"] = "dewey"; persist();
                var ambiguous = src.Read(true); require(!ambiguous.AwaitingArtwork && ambiguous.Portrait == null);
                choices.Remove("[\"second\",\"account\"]");
                owner["cloud-pet-artwork-cache"] = cache;
                persist();
                var restored = src.Read(true);
                require(restored != null && restored.AwaitingArtwork && restored.Portrait == null);
                jsonState["electron-renderer-content-owner"] = "other"; persist();
                var other = src.Read(true); require(other.Portrait == null && !other.AwaitingArtwork);
                jsonState["electron-renderer-content-owner"] = "owner"; choices["[\"a\",\"u\"]"] = "default"; persist();
                var off = src.Read(true); require(off != null && off.Portrait == null && off.State == PetState.Disabled);
                File.Delete(state); string petdir = Path.Combine(folder,"pets","local");
                Directory.CreateDirectory(petdir);
                File.WriteAllText(Path.Combine(folder,"config.toml"),"[desktop]\nselected-avatar-id = 'custom:local'\n");
                File.WriteAllText(Path.Combine(petdir,"pet.json"),"{\"displayName\":\"测试\",\"spritesheetPath\":\"test.png\"}");
                using (var sheet = new Bitmap(192,234)) {
                    sheet.SetPixel(4,9,Color.Red); sheet.SetPixel(7,12,Color.Green);
                    sheet.Save(Path.Combine(petdir,"test.png"), ImageFormat.Png);
                }
                var local = src.Read(true);
                require(local != null && local.Portrait != null && local.Portrait.Width == 4 && local.Portrait.Height == 4);
                local.Portrait.Dispose();

                // A valid cache entry must match the selected unsigned asset URL as a complete key.
                const string asset = "https://example.oaiusercontent.com/files/fixture/raw";
                byte[] png = File.ReadAllBytes(Path.Combine(petdir,"test.png"));
                Action<string> writeEntry = url => {
                    byte[] k = Encoding.UTF8.GetBytes("1/0/_dk_app://- app://- " + url + "?fixture=1");
                    using (var b = new BinaryWriter(File.Create(Path.Combine(cacheFolder,"fixture_0")))) {
                        b.Write(0xfcfb6d1ba7725c30UL); b.Write(5); b.Write(k.Length); b.Write(0L); b.Write(k); b.Write(png);
                        b.Write(0xf4fa6f45970d41d8UL); b.Write(0); b.Write(0); b.Write(png.Length); b.Write(0);
                        b.Write(0xf4fa6f45970d41d8UL); b.Write(0); b.Write(0); b.Write(0); b.Write(0);
                    }
                };
                choices["[\"a\",\"u\"]"] = "pet_fixture";
                record["spritesheetUrl"] = asset + "?example=temporary"; persist();
                writeEntry(asset + "extra"); require(src.Read(true).Portrait == null);
                writeEntry(asset); var matched = src.Read(true);
                require(matched != null && matched.Portrait != null && !matched.AwaitingArtwork && matched.Portrait.Height == 4);
                matched.Portrait.Dispose();
                record["spritesheetFingerprint"] = "new version"; persist();
                require(src.Read(true).Portrait == null);

                string packaged = Path.Combine(folder,"Packages","OpenAI.Codex_test","LocalCache","Roaming","Codex","Partitions","persist");
                string partitionCache = Path.Combine(packaged,"Cache","Cache_Data");
                Directory.CreateDirectory(partitionCache);
                require(WindowsCaches(folder,folder).Contains(partitionCache));
                File.Delete(Path.Combine(cacheFolder,"fixture_0"));
                const string alternate = "https://example.oaiusercontent.com/files/other/raw";
                record["spritesheetUrl"] = alternate; persist();
                string indexPath = Path.Combine(cacheFolder,"index"), blockPath = Path.Combine(cacheFolder,"data_1");
                using (var index = new BinaryWriter(File.Create(indexPath))) {
                    index.Write(0xc103cac3U); index.Write(0x30000); index.BaseStream.Position = 28; index.Write(1);
                    index.BaseStream.Position = 368; index.Write(0xa0010000U);
                }
                using (var data = new BinaryWriter(File.Create(blockPath))) {
                    data.Write(0xc104cac3U); data.Write(0x20000); data.Write(1); data.Write(256);
                    data.BaseStream.SetLength(8192 + 256); data.BaseStream.Position = 8192 + 32;
                    byte[] key = Encoding.UTF8.GetBytes("1/0/" + alternate + "?fixture=short");
                    data.Write(key.Length); data.BaseStream.Position = 8192+44; data.Write(png.Length);
                    data.BaseStream.Position = 8192+60; data.Write(0x80000001U);
                    data.BaseStream.Position = 8192+96; data.Write(key);
                }
                File.WriteAllBytes(Path.Combine(cacheFolder,"f_000001"),png);
                var block = src.Read(true); require(block != null && !block.AwaitingArtwork && block.Portrait != null && block.Portrait.Height == 4);
                block.Portrait.Dispose();
                using (var thumbnail = new Bitmap(24,24)) {
                    thumbnail.Save(Path.Combine(cacheFolder,"f_000001"),ImageFormat.Png);
                }
                using (var entry = new BinaryWriter(File.OpenWrite(blockPath))) {
                    entry.BaseStream.Position = 8192+44;
                    entry.Write((int)new FileInfo(Path.Combine(cacheFolder,"f_000001")).Length);
                }
                writeEntry(alternate);
                var fromSimple = new PetSource(folder,src.assets,() => new[] {cacheFolder}).Read();
                require(fromSimple.Portrait != null && fromSimple.Portrait.Height == 4);
                fromSimple.Portrait.Dispose();
                record["spritesheetUrl"] = alternate + "/another"; persist();
                var neighbor = src.Read(true); require(neighbor.AwaitingArtwork && neighbor.Portrait == null);

                // An offline portrait belongs to exactly one identity, selection and pixel revision.
                string backup = Path.Combine(folder,"last-pet.cache");
                Func<PetSource> fresh = () => new PetSource(folder,src.assets,() => new[] {cacheFolder},backup);
                const string fingerprint = "[192,234,[231,3,184,152,113,237,45,206,116,168,245,177,107,127,232,120,247,62,3,166,173,8,16,21,38,154,52,120,24,27,101,67]]";
                require(src.Fingerprint(png) == fingerprint);
                record["spritesheetUrl"] = alternate; record["spritesheetFingerprint"] = fingerprint; persist();
                var verified = fresh().Read();
                require(verified.Portrait != null && File.Exists(backup)); verified.Portrait.Dispose();
                File.Delete(Path.Combine(cacheFolder,"fixture_0"));
                var recovered = fresh().Read();
                require(!recovered.AwaitingArtwork && recovered.Portrait != null && recovered.Portrait.Height == 4); recovered.Portrait.Dispose();
                cache["accountId"] = "other"; choices.Clear(); choices["[\"other\",\"u\"]"] = "pet_fixture"; persist();
                require(fresh().Read().Portrait == null);
                cache["accountId"] = "a"; choices.Clear(); choices["[\"a\",\"u\"]"] = "pet_different"; record["id"] = "pet_different"; persist();
                require(fresh().Read().Portrait == null);
                choices["[\"a\",\"u\"]"] = "pet_fixture"; record["id"] = "pet_fixture";
                record["spritesheetFingerprint"] = "[updated]"; persist();
                require(fresh().Read().Portrait == null);

            } finally { Directory.Delete(folder,true); }
        }
    }
}
