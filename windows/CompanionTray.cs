using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexBattery {
    internal static class TrayArt {
        public static int ContentEdge(int slot,string size) {
            double fraction = size == "small" ? .78 : size == "large" ? 1.0 : .92;
            return Math.Min(slot,Math.Max(16,(int)Math.Round(slot*fraction)));
        }

        public static Bitmap Battery(int slot,int content,int? remaining,string color) {
            if (content == slot) return IconArt.Battery(slot,remaining,color);
            var frame = new Bitmap(slot,slot,PixelFormat.Format32bppArgb);
            using (var inner = IconArt.Battery(content,remaining,color))
            using (var g = Graphics.FromImage(frame)) {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(inner,(slot-content)/2,(slot-content)/2);
            }
            return frame;
        }
    }

    // Explorer allocates one square to each notification icon. Give the portrait
    // its own slot so the battery can always use the full width for its digits.
    internal sealed class CompanionTray : IDisposable {
        readonly NotifyIcon item;
        Image source;
        Icon artwork;
        int pixels, contentPixels;

        internal CompanionTray(ContextMenuStrip menu, Action openMenu) {
            item = new NotifyIcon { Text = "Codex Battery · Pet", ContextMenuStrip = menu };
            item.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) openMenu();
            };
        }

        internal bool Visible { get { return item.Visible; } }

        internal void Update(Image portrait, int edge, int content) {
            if (Object.ReferenceEquals(source,portrait) && (portrait == null || (edge == pixels && content == contentPixels))) return;
            if (portrait == null) {
                item.Visible = false;
                item.Icon = null;
            } else {
                using (var canvas = new Bitmap(edge,edge,PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(canvas))
                using (var borders = new ImageAttributes()) {
                    int inset = Math.Max(1,edge/24);
                    float scale = Math.Min((content-2*inset)/(float)portrait.Width,(content-2*inset)/(float)portrait.Height);
                    int w = Math.Max(1,(int)Math.Round(portrait.Width*scale));
                    int h = Math.Max(1,(int)Math.Round(portrait.Height*scale));
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    borders.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(portrait,new Rectangle((edge-w)/2,(edge-h)/2,w,h),
                                       0,0,portrait.Width,portrait.Height,GraphicsUnit.Pixel,borders);
                    var next = IconArt.ToIcon(canvas);
                    try {
                        item.Icon = next;
                        item.Visible = true;
                    } catch {
                        next.Dispose();
                        throw;
                    }
                    var old = artwork;
                    artwork = next;
                    if (old != null) old.Dispose();
                }
            }
            if (portrait == null && artwork != null) { artwork.Dispose(); artwork = null; }
            source = portrait;
            pixels = edge; contentPixels = content;
        }

        public void Dispose() {
            item.Visible = false;
            item.Dispose();
            if (artwork != null) artwork.Dispose();
            source = null;
        }
    }
}
