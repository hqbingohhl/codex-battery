using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace CodexBattery {
    static class PetShortcut {
        [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
        static extern void SHChangeNotify(int action, uint flags, string path, IntPtr unused);

        [DllImport("shell32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
        static extern bool SHGetPathFromIDListEx(IntPtr idList, [Out] StringBuilder path, uint count, uint flags);

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern bool MoveFileEx(string source, string destination, uint flags);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink {}
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int count);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }

        public static void Save(string linkPath, Image portrait, bool creating) {
            Save(linkPath, portrait, creating, Path.Combine(Preferences.Folder,"Icons"));
        }
        static void Save(string linkPath, Image portrait, bool creating, string artworkFolder) {
            if (String.IsNullOrEmpty(linkPath) || !Path.GetExtension(linkPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                throw new IOException("请选择 Windows 快捷方式 (.lnk)。");
            linkPath = Path.GetFullPath(linkPath);
            var original = new FileInfo(linkPath);
            bool existed = original.Exists;
            if (!creating && !existed) return;
            long originalSize = existed ? original.Length : -1;
            DateTime originalTime = existed ? original.LastWriteTimeUtc : DateTime.MinValue;
            string stage = Path.Combine(original.DirectoryName, "." + Guid.NewGuid().ToString("N") + ".lnk");
            var link = (IShellLinkW)new ShellLink();
            try {
                var disk = (IPersistFile)link;
                if (!creating) {
                    // Change only IconLocation. Keep the user's launch arguments, window state,
                    // working directory and hotkey, and honor a shortcut that they retargeted.
                    disk.Load(linkPath, 0);
                    if (!TargetsApplication(link)) return;
                } else {
                    link.SetPath(Application.ExecutablePath);
                    link.SetWorkingDirectory(AppDomain.CurrentDomain.BaseDirectory);
                    link.SetDescription("Codex Battery");
                }

                byte[] bytes = IconArt.ShortcutIcon(portrait);
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                // Explorer keys its icon cache by path and index. Stable, content-specific
                // names also keep older user-created shortcuts usable.
                string iconPath = Path.Combine(artworkFolder, hash + ".ico");
                bool newIcon = !File.Exists(iconPath);
                if (newIcon) {
                    Storage.WriteAtomic(iconPath, bytes);
                    SHChangeNotify(2,5,iconPath, IntPtr.Zero); // SHCNE_CREATE / SHCNF_PATHW
                }
                if (!creating) {
                    var prior = new StringBuilder(32768);
                    int index; link.GetIconLocation(prior, prior.Capacity, out index);
                    if (!newIcon && index == 0 && SamePath(prior.ToString(),iconPath)) return;
                }
                link.SetIconLocation(iconPath,0);
                disk.Save(stage,true);
                if (!creating) {
                    original.Refresh();
                    if (!original.Exists || original.Length != originalSize || original.LastWriteTimeUtc != originalTime) return;
                }
                // A same-directory rename also works when the user places the .lnk on a FAT USB drive.
                if (!MoveFileEx(stage,linkPath,(existed ? 1u : 0u) | 8u))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                SHChangeNotify(existed ? 0x2000 : 2,5,linkPath,IntPtr.Zero);
            } finally {
                Marshal.FinalReleaseComObject(link);
                if (File.Exists(stage)) File.Delete(stage);
            }
        }
        static bool TargetsApplication(IShellLinkW link) {
            // A shortcut can contain both legacy ANSI LinkInfo and a Unicode shell item ID list.
            // Verify the actual shell item when the raw path has lost non-ASCII characters.
            var target = new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,4);
            if (SamePath(target.ToString(),Application.ExecutablePath)) return true;
            IntPtr idList; link.GetIDList(out idList);
            if (idList == IntPtr.Zero) return false;
            try {
                target.Clear();
                return SHGetPathFromIDListEx(idList,target,(uint)target.Capacity,0) &&
                       SamePath(target.ToString(),Application.ExecutablePath);
            } finally { Marshal.FreeCoTaskMem(idList); }
        }
        static bool SamePath(string first, string second) {
            if (String.IsNullOrWhiteSpace(first)) return false;
            try {
                return String.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(first)),
                                     Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
            } catch (ArgumentException) { return false; } catch (NotSupportedException) { return false; }
        }

        public static void SelfTest() {
            string folder = Path.Combine(Path.GetTempPath(),"CodexBatteryLink-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                string shortcut = Path.Combine(folder,"fixture.lnk");
                string icons = Path.Combine(folder,"icons");
                Save(shortcut,null,true,icons);
                string battery;
                var link = (IShellLinkW)new ShellLink();
                try {
                    var disk = (IPersistFile)link; disk.Load(shortcut,0);
                    var path = new StringBuilder(32768);
                    int index; link.GetIconLocation(path,path.Capacity,out index); battery = path.ToString();
                    if (!File.Exists(battery)) throw new InvalidDataException("Shortcut icon missing");
                    link.SetArguments("--fixture"); disk.Save(shortcut,true);
                } finally { Marshal.FinalReleaseComObject(link); }
                using (var pet = new Bitmap(24,32)) {
                    using (var brush=Graphics.FromImage(pet)) brush.Clear(Color.Orange);
                    Save(shortcut,pet,false,icons);
                }
                link = (IShellLinkW)new ShellLink();
                try {
                    ((IPersistFile)link).Load(shortcut,0);
                    var path = new StringBuilder(32768); int index;
                    link.GetIconLocation(path,path.Capacity,out index);
                    if (SamePath(path.ToString(),battery) || !File.Exists(path.ToString()))
                        throw new InvalidDataException("Shortcut icon did not change");
                    var args = new StringBuilder(256); link.GetArguments(args,args.Capacity);
                    if (args.ToString() != "--fixture") throw new InvalidDataException("Shortcut lost launch options");
                } finally { Marshal.FinalReleaseComObject(link); }
            } finally { Directory.Delete(folder,true); }
        }
    }
}
