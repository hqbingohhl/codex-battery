using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexBattery {
    internal static class PetFallback {
        static string CustomFile { get { return Path.Combine(Preferences.Folder, "fallback-pet.png"); } }
        public static bool HasCustom { get { return File.Exists(CustomFile); } }

        // A fallback is opt-in. Only the file imported by the user qualifies.
        public static Bitmap Load() { string ignored; return Load(out ignored); }
        public static Bitmap Load(out string sourceName) { return Load(CustomFile,out sourceName); }
        static Bitmap Load(string custom,out string sourceName) {
            sourceName = "";
            if (!File.Exists(custom)) return null;
            try {
                using (var stream = File.OpenRead(custom))
                using (var image = Image.FromStream(stream)) {
                    var detached = new Bitmap(image);
                    sourceName = "自选图片";
                    return detached;
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
              catch (ArgumentException) { } catch (ExternalException) { } catch (OutOfMemoryException) { }
            return null;
        }

        public static void Import(string path) { Import(path, CustomFile); }
        public static void Reset() { File.Delete(CustomFile); }

        static void Import(string path, string destination) {
            byte[] bytes;
            using (var file = File.OpenRead(path)) {
                if (file.Length == 0 || file.Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("请选择 16 MB 以内的 PNG 或 JPEG 图片。");
                using (var image = Image.FromStream(file, false, false)) {
                    if (image.RawFormat.Guid != ImageFormat.Png.Guid && image.RawFormat.Guid != ImageFormat.Jpeg.Guid)
                        throw new InvalidDataException("请选择 PNG 或 JPEG 图片。");
                    if ((long)image.Width * image.Height > 24000000)
                        throw new InvalidDataException("请将图片缩小到 2400 万像素以内再导入。");
                    if (Array.IndexOf(image.PropertyIdList, 0x0112) >= 0) {
                        var orientation = image.GetPropertyItem(0x0112).Value;
                        var rotations = new[] { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX,
                            RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX,
                            RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                        if (orientation.Length >= 2) {
                            int n = BitConverter.ToUInt16(orientation, 0);
                            if (n > 1 && n <= rotations.Length) image.RotateFlip(rotations[n - 1]);
                        }
                    }
                    double scale = Math.Min(1.0, 256.0 / Math.Max(image.Width, image.Height));
                    int w = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(image.Height * scale));
                    using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (var surface = Graphics.FromImage(bitmap))
                    using (var wrap = new ImageAttributes())
                    using (var output = new MemoryStream()) {
                        surface.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        wrap.SetWrapMode(WrapMode.TileFlipXY);
                        surface.DrawImage(image, new Rectangle(0, 0, w, h), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrap);
                        bitmap.Save(output, ImageFormat.Png);
                        bytes = output.ToArray();
                    }
                }
            }
            Storage.WriteAtomic(destination, bytes);
        }

        internal static void SelfTest() {
            string dir = Path.Combine(Path.GetTempPath(), "CodexFallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string input = Path.Combine(dir, "图片.png"), saved = Path.Combine(dir, "stored.png");
                using (var fixture = new Bitmap(400, 200))
                using (var g = Graphics.FromImage(fixture)) {
                    g.Clear(Color.SeaGreen);
                    fixture.Save(input, ImageFormat.Png);
                }
                Import(input, saved);
                File.Delete(input);
                string label;
                using (var avatar = Load(saved,out label)) {
                    if (avatar == null || label != "自选图片" || avatar.Size != new Size(256, 128) || avatar.GetPixel(120, 64).G != Color.SeaGreen.G)
                        throw new InvalidDataException("Imported avatar failed");
                }
                File.WriteAllText(saved, "broken image");
                if (Load(saved,out label) != null || label != "") throw new InvalidDataException("Invalid image showed a placeholder");
                File.Delete(saved);
                if (Load(saved,out label) != null || label != "") throw new InvalidDataException("Unconfigured fallback showed a placeholder");
            } finally { Directory.Delete(dir, true); }
        }
    }
}
