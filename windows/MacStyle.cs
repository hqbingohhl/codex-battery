using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexBattery {
    internal static class MacStyle {
        static readonly bool dark = UsesDarkAppearance();
        // GDI draws WinForms labels and menus; GDI+ measures and paints other controls.
        // Keep the collection alive for the process so both use the same private TrueType faces.
        static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        static readonly FontFamily body = LoadFace("Text", "CB Interface Text");
        static readonly FontFamily heading = LoadFace("Medium", "CB Interface Medium");
        static readonly FontFamily numerals = LoadFace("Numerals", "CB Interface Numerals");

        [DllImport("gdi32.dll", EntryPoint="AddFontResourceExW", CharSet=CharSet.Unicode)]
        static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);

        static bool UsesDarkAppearance() {
            try {
                var choice = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
                return choice is int && (int)choice == 0;
            } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
            return false;
        }

        static FontFamily LoadFace(string file, string family) {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","Fonts",file + ".ttf");
            if (!File.Exists(path)) return null;
            try {
                const uint privateToProcess = 0x10;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
                    AddFontResourceEx(path, privateToProcess, IntPtr.Zero) == 0) return null;
                fonts.AddFontFile(path);
                return new FontFamily(family, fonts);
            } catch (ArgumentException) { } catch (IOException) { }
              catch (UnauthorizedAccessException) { } catch (ExternalException) { }
            return null;
        }

        public static Font Text(float points, bool semibold=false) {
            var face = semibold ? heading : body;
            return face == null ? new Font("Microsoft YaHei UI",points,FontStyle.Regular,GraphicsUnit.Point)
                                : new Font(face,points,FontStyle.Regular,GraphicsUnit.Point);
        }
        public static Font Number(float points) {
            return numerals == null ? new Font("Segoe UI",points,FontStyle.Regular,GraphicsUnit.Point)
                                    : new Font(numerals,points,FontStyle.Regular,GraphicsUnit.Point);
        }
        internal static void VerifyTypography() {
            if (body == null || heading == null || numerals == null)
                throw new InvalidDataException("未能加载随附的界面字体，请完整解压 ZIP。");
        }

        static Color Tone(int light, int dim, Color accessible) {
            return SystemInformation.HighContrast ? accessible : Color.FromArgb(unchecked((int)0xff000000) | (dark ? dim : light));
        }
        public static Color Surface { get { return Tone(0xf6f6f8,0x202124,SystemColors.Window); } }
        public static Color Card { get { return Tone(0xffffff,0x2c2d31,SystemColors.Window); } }
        public static Color Ink { get { return Tone(0x202124,0xf4f4f6,SystemColors.WindowText); } }
        public static Color Muted { get { return Tone(0x676970,0xb9bbc2,SystemColors.WindowText); } }
        public static Color Line { get { return Tone(0xdedfe3,0x47484d,SystemColors.WindowText); } }
        public static Color Track { get { return Tone(0xe7e8eb,0x45474b,SystemColors.Control); } }
        public static Color ButtonFace { get { return Tone(0xfdfdfd,0x343539,SystemColors.Control); } }
        public static Color ButtonHover { get { return Tone(0xefeff2,0x3d3e42,SystemColors.ControlLight); } }
        public static Color ButtonPressed { get { return Tone(0xe6e7ea,0x47484d,SystemColors.ControlDark); } }
        public static Color Accent { get { return Tone(0x246853,0x8ed6b9,SystemColors.HotTrack); } }
        public static Color Capacity(int percent, string mode, bool stale) {
            if(stale || mode=="mono") return Muted;
            if(mode=="teal") return Tone(0x269e9a,0x56c7c1,SystemColors.Highlight);
            if(percent<=20) return Tone(0xe2655e,0xf5847a,SystemColors.Highlight);
            if(percent<=50) return Tone(0xe59b3e,0xecad55,SystemColors.Highlight);
            return Tone(0x38ad76,0x50bd8c,SystemColors.Highlight);
        }
    }
}
