using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexBattery {
    internal static class BadgeArt {
        // Keep the same optical proportions as the menubar item regardless of monitor DPI.
        internal static void Draw(Graphics g, Rectangle bounds, Image pet, int? remaining, string style) {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            float width = pet == null ? 44 : 64;
            float scale = Math.Min(bounds.Width / width, bounds.Height / 22f);
            var saved = g.Save();
            try {
                g.TranslateTransform(bounds.Right - width * scale, bounds.Y + (bounds.Height - 22 * scale) / 2);
                g.ScaleTransform(scale, scale);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var capsule = IconArt.Rounded(new RectangleF(0, 0, width, 22), 11))
                using (var surface = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Control :
                                                    Color.FromArgb(58, 123, 129, 145))) g.FillPath(surface, capsule);
                if (pet != null) {
                    float fit = Math.Min(16f / pet.Width, 18f / pet.Height);
                    float w = pet.Width * fit, h = pet.Height * fit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(pet, 14 - w / 2, 11 - h / 2, w, h);
                }
                var body = new RectangleF(width - 38, 4, 30, 14);
                var inner = body; inner.Inflate(-1, -1);
                var track = inner; track.Inflate(-.65f, -.65f);
                int? value = remaining.HasValue ? Math.Max(0, Math.Min(100, remaining.Value)) : (int?)null;
                using (var rim = new SolidBrush(Color.FromArgb(219, 221, 226)))
                using (var basePaint = new SolidBrush(Color.FromArgb(45, 46, 49)))
                using (var charge = new SolidBrush(IconArt.ChargeColor(value, style)))
                using (var ink = new SolidBrush(Color.White))
                using (var frame = IconArt.Rounded(body, 3.5f))
                using (var well = IconArt.Rounded(inner, 2.5f))
                using (var fill = IconArt.Rounded(track, 2))
                using (var tip = IconArt.Rounded(new RectangleF(body.Right + .7f, 8.4f, 1.8f, 5.2f), .85f)) {
                    g.FillPath(rim, tip);
                    g.FillPath(rim, frame);
                    g.FillPath(basePaint, well);
                    if (value > 0) {
                        var clip = g.Save();
                        g.SetClip(new RectangleF(track.X, track.Y, track.Width * value.Value / 100f, track.Height),
                                  CombineMode.Intersect);
                        g.FillPath(charge, fill);
                        g.Restore(clip);
                    }
                    string label = value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + "%" : "-";
                    IconArt.DrawTrayLabel(g, ink, body, track, label, 2);
                }
            } finally { g.Restore(saved); }
        }
    }
}
