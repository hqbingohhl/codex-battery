using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexBattery {
    static class Storage {
        internal static void WriteAtomic(string path, byte[] bytes) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public class Preferences {
        public string Window = "primary", Color = "auto", TraySize = "normal", CliPath = "", Shortcut = "";
        public string Language = "system";
        public int RefreshSeconds = 60, PetSize = 48;
        public bool ShowPet = true, LowAlert = false, CompactTray = false;
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBattery"); } }
        internal string SettingsFile = Path.Combine(Folder,"settings.json");
        public static Preferences Load() { return Load(Path.Combine(Folder,"settings.json")); }
        internal static Preferences Load(string file) {
            try {
                var p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file));
                if (p == null) return new Preferences { SettingsFile=file };
                p.SettingsFile=file;
                if (!UiText.IsChoice(p.Language)) p.Language = "system";
                if (!new[] {30,60,120}.Contains(p.RefreshSeconds)) p.RefreshSeconds = 60;
                if (!new[] {32,48,72}.Contains(p.PetSize)) p.PetSize = 48;
                if (!new[] {"primary","secondary","minimum"}.Contains(p.Window)) p.Window = "primary";
                if (!new[] {"auto","teal","mono"}.Contains(p.Color)) p.Color = "auto";
                if (!new[] {"small","normal","large"}.Contains(p.TraySize)) p.TraySize = "normal";
                return p;
            } catch { return new Preferences { SettingsFile=file }; }
        }
        public void Save() { Storage.WriteAtomic(SettingsFile, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this))); }
    }
}
