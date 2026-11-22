using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexBattery {
    static class IconArt {
        // A narrow numeral one leaves room for the full "100%" inside a 16-pixel tray cell.
        static readonly string[][] glyphs = {
            new[]{"111","101","101","101","111"}, new[]{"1","1","1","1","1"},
            new[]{"111","001","111","100","111"}, new[]{"111","001","111","001","111"},
            new[]{"101","101","111","001","001"}, new[]{"111","100","111","001","111"},
            new[]{"111","100","111","101","111"}, new[]{"111","001","010","010","010"},
            new[]{"111","101","111","101","111"}, new[]{"111","101","111","001","111"},
            new[]{"101","001","010","100","101"}, new[]{"000","000","111","000","000"}
        };
        static int GlyphIndex(char ch) { return ch == '%' ? 10 : ch == '-' ? 11 : ch - '0'; }

        internal static Color ChargeColor(int? value,string style) {
            if (!value.HasValue || style == "mono") return Color.FromArgb(89,91,96);
            if (style == "teal") return Color.FromArgb(38,108,113);
            if (value > 50) return Color.FromArgb(57,115,65);
            return value > 20 ? Color.FromArgb(128,88,37) : Color.FromArgb(154,64,52);
        }

        public static Bitmap Battery(int size, int? remaining, string style) {
            if (size < 16) throw new ArgumentOutOfRangeException("size");
            int? value = remaining.HasValue ? Math.Max(0,Math.Min(100,remaining.Value)) : (int?)null;
            string label = value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%" : "-";
            // The casing, green charge and type follow the dark Mac menu-bar rendering.
            Color rim = Color.FromArgb(218,220,226);
            Color ink = Color.FromArgb(254,254,254);
            float u = size/16f;
            bool smallest = size < 20;
            // The larger sizes retain the reference's wide proportions. Smaller taskbar
            // cells use a little extra height to preserve counters in "68%" and "100%".
            float bodyHeight = (smallest ? 9.8f : size < 24 ? 9.1f : size < 32 ? 8.45f : size < 40 ? 7.65f : 6.6f)*u;
            var body = new RectangleF(smallest ? 0 : .18f*u,(size-bodyHeight)/2,
                                      (smallest ? 14.8f : 14.27f)*u,bodyHeight);
            const int samples = 4;
            Bitmap image;
            using (var canvas = new Bitmap(size*samples,size*samples,PixelFormat.Format32bppPArgb)) {
              using (var g = Graphics.FromImage(canvas)) {
                g.ScaleTransform(samples,samples);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                float rimWidth = Math.Max(.77f,.48f*u);
                var interior = body;
                interior.Inflate(-rimWidth,-rimWidth);
                var track = interior;
                float inset = Math.Max(.30f,.39f*u);
                track.Inflate(-inset,-inset);
                float radius = bodyHeight*.24f;
                using (var edge = new SolidBrush(rim))
                using (var surface = new SolidBrush(Color.FromArgb(45,46,49)))
                using (var charge = new SolidBrush(ChargeColor(value,style)))
                using (var type = new SolidBrush(ink))
                using (var casing = Rounded(body,radius))
                using (var inner = Rounded(interior,Math.Max(.35f,radius-rimWidth)))
                using (var well = Rounded(track,Math.Max(.25f,radius-rimWidth-inset))) {
                    var tip = smallest ? new RectangleF(14.65f*u,6.35f*u,1.35f*u,3.3f*u) :
                                         new RectangleF(14.90f*u,8*u-bodyHeight*.19f,1.02f*u,bodyHeight*.38f);
                    using (var terminal = Rounded(tip,tip.Width*.48f)) g.FillPath(edge,terminal);
                    g.FillPath(edge,casing);
                    g.FillPath(surface,inner);
                    if (value > 0) {
                        var saved = g.Save();
                        g.SetClip(new RectangleF(track.X-1,track.Y-1,track.Width*value.Value/100f + 1,track.Height+2));
                        g.FillPath(charge,well);
                        g.Restore(saved);
                    }
                    if (!smallest) DrawTrayLabel(g,type,body,track,label,u);
                }
              }
              image = ReduceTrayCanvas(canvas,samples);
            }
            if (smallest) using (var g = Graphics.FromImage(image)) using (var type = new SolidBrush(ink))
                DrawHintedLabel(g,type,body,label);
            return image;
        }

        // Average premultiplied samples to preserve soft transparent edges on light and dark taskbars.
        static Bitmap ReduceTrayCanvas(Bitmap canvas,int samples) {
            int size=canvas.Width/samples, pixels=samples*samples;
            var output=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            var source=canvas.LockBits(new Rectangle(Point.Empty,canvas.Size),ImageLockMode.ReadOnly,canvas.PixelFormat);
            try {
                var dest=output.LockBits(new Rectangle(Point.Empty,output.Size),ImageLockMode.WriteOnly,output.PixelFormat);
                try {
                    byte[] strip=new byte[canvas.Width*4*samples], row=new byte[size*4];
                    for(int y=0;y<size;y++) {
                        for(int sy=0;sy<samples;sy++)
                            Marshal.Copy(IntPtr.Add(source.Scan0,(y*samples+sy)*source.Stride),strip,sy*canvas.Width*4,canvas.Width*4);
                        for(int x=0;x<size;x++) for(int channel=0;channel<4;channel++) {
                            int sum=0;
                            for(int sy=0;sy<samples;sy++) for(int sx=0;sx<samples;sx++)
                                sum+=strip[4*(sy*canvas.Width+x*samples+sx)+channel];
                            row[x*4+channel]=(byte)((sum+pixels/2)/pixels);
                        }
                        Marshal.Copy(row,0,IntPtr.Add(dest.Scan0,y*dest.Stride),row.Length);
                    }
                } finally { output.UnlockBits(dest); }
                return output;
            } catch { output.Dispose(); throw; }
            finally { canvas.UnlockBits(source); }
        }

        static void DrawHintedLabel(Graphics graphics,Brush ink,RectangleF body,string label) {
            int width = -1;
            foreach (char ch in label) width += 1 + glyphs[GlyphIndex(ch)][0].Length;
            int x = (int)Math.Round(body.X+(body.Width-width)/2), y = (int)Math.Round(body.Y+(body.Height-5)/2,MidpointRounding.AwayFromZero);
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Default;
            foreach (char ch in label) {
                string[] glyph = glyphs[GlyphIndex(ch)];
                for (int row=0;row<glyph.Length;row++)
                    for (int column=0;column<glyph[row].Length;column++)
                        if (glyph[row][column]=='1') graphics.FillRectangle(ink,x+column,y+row,1,1);
                x += glyph[0].Length+1;
            }
        }

        // Use the ink bounds of the Windows font so every percentage is centered, including 100%.
        internal static void DrawTrayLabel(Graphics g,Brush ink,RectangleF body,RectangleF track,string label,float u) {
            if (label == "-") {
                float width=(float)Math.Round(body.Width*.29f), height=Math.Max(1,(float)Math.Round(.85f*u));
                var line=new RectangleF((float)Math.Round(body.X+(body.Width-width)/2),(float)Math.Round(body.Y+(body.Height-height)/2),width,height);
                using (var dash=Rounded(line,height*.45f)) g.FillPath(ink,dash);
                return;
            }
            using (var font = new Font("Segoe UI",32,FontStyle.Bold,GraphicsUnit.Pixel))
            using (var family = font.FontFamily)
            using (var text = new GraphicsPath())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone()) {
                float advance=0;
                foreach (char ch in label) {
                    using (var glyph = new GraphicsPath()) {
                        glyph.AddString(ch.ToString(),family,(int)font.Style,32,PointF.Empty,format);
                        var box = glyph.GetBounds();
                        using (var align = new Matrix(1,0,0,1,advance-box.X,0)) glyph.Transform(align);
                        text.AddPath(glyph,false);
                        advance+=box.Width+2.7f;
                    }
                }
                var bounds = text.GetBounds();
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                float inkHeight = (float)Math.Round(body.Height*.545f);
                float sy = inkHeight/bounds.Height;
                float sx = Math.Min(sy,(track.Width-.55f*u)/bounds.Width);
                float x = body.X + (body.Width-bounds.Width*sx)/2 - bounds.X*sx;
                float y = (float)Math.Round(body.Y + (body.Height-inkHeight)/2,MidpointRounding.AwayFromZero) - bounds.Y*sy;
                using (var fit = new Matrix(sx,0,0,sy,x,y)) text.Transform(fit);
                g.FillPath(ink,text);
            }
        }

        public static void SelfTestBattery() {
            foreach (int size in new[] {16,20,24,32,48,64}) {
                var marks = new System.Collections.Generic.HashSet<string>();
                foreach (int? value in new int?[] {null,0,1,20,50,68,88,99,100}) {
                    using (var bitmap = Battery(size,value,"auto")) {
                        int top = size, bottom = -1, left = size, right = -1, digits = 0;
                        var mask = new System.Text.StringBuilder();
                        for (int y=0;y<size;y++) for (int x=0;x<size;x++) {
                            Color pixel = bitmap.GetPixel(x,y);
                            if (pixel.A > 128) {
                                left = Math.Min(left,x); right = Math.Max(right,x);
                                top = Math.Min(top,y); bottom = Math.Max(bottom,y);
                            }
                            if (x >= size*.08 && x <= size*.82 && y >= size*.37 && y <= size*.64) {
                                // Include high-coverage antialiased white type, away from the light casing.
                                bool ink = pixel.A > 160 && 30*pixel.R+59*pixel.G+11*pixel.B > 16800;
                                if (ink) digits++;
                                mask.Append(ink ? '1' : '0');
                            }
                        }
                        if (right-left < size*.84 || bottom-top > size*.74 || bottom-top < size*.365 ||
                            bitmap.GetPixel(0,0).A != 0 || bitmap.GetPixel(size-1,size-1).A != 0 ||
                            digits < size/6 || !marks.Add(mask.ToString()))
                            throw new InvalidDataException("托盘电池轮廓或百分比未正确绘制 ("+size+"px, "+value+")");
                    }
                }
            }
            using (var high = Battery(64,68,"auto"))
            using (var medium = Battery(64,50,"auto"))
            using (var low = Battery(64,18,"auto"))
            using (var fixedTeal = Battery(64,18,"teal"))
            using (var mono = Battery(64,68,"mono")) {
                Color h=high.GetPixel(10,23), m=medium.GetPixel(10,23), l=low.GetPixel(10,23),
                      t=fixedTeal.GetPixel(10,23), n=mono.GetPixel(10,23);
                if (!(h.G-h.B > 22 && m.R > m.G && m.G > m.B && l.R-l.G > m.R-m.G &&
                      t.G-t.R > 20 && Math.Abs(t.G-t.B) < 12 && Math.Abs(n.R-n.B) < 24))
                    throw new InvalidDataException("托盘电池颜色模式未正确绘制");
                Color empty = high.GetPixel(48,23), rim = high.GetPixel(28,19), terminal = high.GetPixel(61,32);
                if (empty.R > 62 || empty.G > 62 || empty.B > 62 || rim.R < 175 || terminal.R < 175 ||
                    high.GetPixel(36,23).G < 100 || medium.GetPixel(36,23).G > 62)
                    throw new InvalidDataException("深色电池、浅色描边、电极或电量位置异常");
            }
        }

        public static Icon ToIcon(Bitmap bitmap) {
            // A fresh ICO keeps both its 32-bit alpha and explicit AND mask. GDI bitmap
            // handles can lose their transparent corners on the trip through Icon.Clone().
            byte[] frame=IconFrame(bitmap);
            using (var data=new MemoryStream())
            using (var writer=new BinaryWriter(data)) {
                writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);
                writer.Write((byte)(bitmap.Width%256)); writer.Write((byte)(bitmap.Height%256));
                writer.Write((short)0); writer.Write((short)1); writer.Write((short)32);
                writer.Write(frame.Length); writer.Write(22); writer.Write(frame);
                data.Position=0;
                return new Icon(data,bitmap.Width,bitmap.Height);
            }
        }

        internal static GraphicsPath Rounded(RectangleF r, float radius) {
            float d = 2 * radius;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right-d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom-d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        public static Bitmap AppImage(Image portrait, int size) {
            // These four transparent tiles come from the same native renderer as the Mac app icon.
            string tile = (portrait == null ? "battery-" : "portrait-") + (size <= 32 ? "32" : "256") + ".png";
            var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try {
                using (var source = typeof(IconArt).Assembly.GetManifestResourceStream("CodexBattery.Icons." + tile)
                                 ?? File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","Icons",tile)))
                using (var background = Image.FromStream(source))
                using (var g = Graphics.FromImage(image)) {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(background, new Rectangle(0,0,size,size));
                    if (portrait != null) {
                        float scale = size / 256f;
                        float fit = Math.Min(192f / portrait.Width,152f / portrait.Height) * scale;
                        float width = portrait.Width * fit, height = portrait.Height * fit;
                        g.DrawImage(portrait,128f*scale-width/2f,107f*scale-height/2f,width,height);
                    }
                }
            } catch {
                image.Dispose();
                throw;
            }
            return image;
        }

        // Use traditional 32-bit DIBs with transparency masks. Framework icon
        // readers and Explorer both understand these at small and large sizes.
        static byte[] IconFrame(Bitmap bmp) {
            int size = bmp.Width;
            var rect = new Rectangle(0,0,size,size);
            var pixels = bmp.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                using(var file=new MemoryStream())
                using(var writer=new BinaryWriter(file)) {
                    writer.Write(40); writer.Write(size); writer.Write(size*2);
                    writer.Write((short)1); writer.Write((short)32);
                    for(int i=0;i<6;i++) writer.Write(0);
                    byte[] row=new byte[size*4];
                    int maskStride=((size+31)/32)*4;
                    byte[] masks=new byte[size*maskStride];
                    for(int y=0;y<size;y++) {
                        Marshal.Copy(IntPtr.Add(pixels.Scan0,(size-1-y)*pixels.Stride),row,0,row.Length);
                        writer.Write(row);
                        for(int x=0;x<size;x++) if(row[4*x+3]==0)
                            masks[y*maskStride+x/8] |= (byte)(0x80 >> (x%8));
                    }
                    writer.Write(masks);
                    return file.ToArray();
                }
            } finally { bmp.UnlockBits(pixels); }
        }
        public static byte[] ShortcutIcon(Image portrait) {
            int[] sizes = {16,24,32,48,64,128,256};
            var images = new byte[sizes.Length][];
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output)) {
                writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++) {
                    using (var bmp = AppImage(portrait,sizes[i])) images[i] = IconFrame(bmp);
                    writer.Write((byte)(sizes[i] % 256)); writer.Write((byte)(sizes[i] % 256));
                    writer.Write((short)0); writer.Write((short)1); writer.Write((short)32);
                    writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
                }
                foreach (var image in images) writer.Write(image);
                return output.ToArray();
            }
        }
    }
}
