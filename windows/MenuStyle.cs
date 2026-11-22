using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexBattery {
    internal sealed class MenuHeading : ToolStripLabel {
        internal Image Portrait { get; private set; }
        internal int? Remaining { get; private set; }
        internal string ColorStyle { get; private set; }
        internal MenuHeading() : base(UiText.T("Codex 剩余额度")) {
            Enabled=false; AutoSize=false; Margin=Padding.Empty; Padding=Padding.Empty;
        }
        internal void SizeFor(Font font,float scale) {
            Font=font;
            int text=TextRenderer.MeasureText(Text,font).Width;
            Size=new Size(text+(int)Math.Ceiling(85*scale),Math.Max(font.Height+(int)Math.Round(8*scale),(int)Math.Round(30*scale)));
        }
        internal Rectangle BadgeBounds(float scale) {
            int w=(int)Math.Round(64*scale), h=(int)Math.Round(22*scale);
            return new Rectangle(Width-w-(int)Math.Round(3*scale),(Height-h)/2,w,h);
        }
        internal Rectangle TitleBounds(float scale) {
            // ToolStripDropDownMenu already offsets label items past its check column.
            int left=0;
            return new Rectangle(left,0,Math.Max(1,BadgeBounds(scale).Left-left-(int)Math.Round(6*scale)),Height);
        }
        protected override void OnPaint(PaintEventArgs e) {
            float scale=e.Graphics.DpiX/96f;
            BadgeArt.Draw(e.Graphics,BadgeBounds(scale),Portrait,Remaining,ColorStyle);
            TextRenderer.DrawText(e.Graphics,Text,Font,TitleBounds(scale),MacStyle.Muted,TextFormatFlags.Left|
                TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|
                TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        internal void Update(Image portrait,int? remaining,string color) {
            if(Object.ReferenceEquals(Portrait,portrait) && Remaining==remaining && ColorStyle==color) return;
            Portrait=portrait; Remaining=remaining; ColorStyle=color; Invalidate();
        }
    }
    // Keep ToolStrip's popup placement, checked states, accessibility and keyboard navigation.
    // Only the surface, insets and drawing differ from the standard Windows dropdown.
    internal sealed class MenuStyle : ToolStripProfessionalRenderer {
        static Color Surface { get {
            return SystemInformation.HighContrast?SystemColors.Menu:MacStyle.Surface.GetBrightness()<.5f
                ? Color.FromArgb(47,48,52) : Color.FromArgb(237,238,241);
        } }
        static Color Edge { get {
            return SystemInformation.HighContrast?SystemColors.MenuText:MacStyle.Surface.GetBrightness()<.5f
                ? Color.FromArgb(72,73,78) : Color.FromArgb(202,204,209);
        } }
        static Color Selection { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(8,114,231); } }
        static Color SelectedInk { get { return SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White; } }

        static Color AncestorFill { get {
            return SystemInformation.HighContrast?SystemColors.Control:MacStyle.Surface.GetBrightness()<.5f
                ? Color.FromArgb(66,68,73) : Color.FromArgb(214,216,221);
        } }
        internal void Format(ToolStripDropDownMenu menu,Font font,float scale) {
            menu.Font=font;
            menu.ForeColor=MacStyle.Ink; menu.BackColor=Surface;
            menu.ShowImageMargin=false; menu.ShowCheckMargin=true;
            int inset=Math.Max(3,(int)Math.Round(4*scale));
            menu.Padding=new Padding(inset,inset,inset,inset);
            if(menu.Items.Count>0 && menu.Items[0] is MenuHeading)
                ((MenuHeading)menu.Items[0]).SizeFor(font,scale);
            // Child menus already inherit the renderer. Each popup still needs its own drawing hooks.
            if(!Object.ReferenceEquals(menu.Tag,this)) {
                menu.Tag=this;
                menu.Renderer=this;
                menu.SizeChanged+=delegate { SetCorners(menu,scale); };
                SetCorners(menu,scale);
                ToolStripItem previous=null;
                menu.Paint+=delegate {
                    var active=ActiveChild(menu);
                    if(previous!=active) { previous=active; InvalidateOwners(menu); }
                };
                menu.Opened+=delegate { InvalidateOwners(menu); };
                menu.Closed+=delegate { InvalidateOwners(menu); };
            }
            foreach(ToolStripItem item in menu.Items) {
                item.Font=font;
                var entry=item as ToolStripMenuItem;
                if(entry==null) continue;
                int vertical=Math.Max(1,(int)Math.Round(2*scale));
                entry.Padding=new Padding(2,vertical,2,vertical);
                if(entry.HasDropDownItems) Format((ToolStripDropDownMenu)entry.DropDown,font,scale);
            }
        }
        static void SetCorners(ToolStrip menu,float scale) {
            if(menu.Width<2 || menu.Height<2 || SystemInformation.HighContrast) return;
            using(var shape=Dashboard.Rounded(new RectangleF(0,0,menu.Width,menu.Height),9*scale)) {
                var previous=menu.Region;
                menu.Region=new Region(shape);
                if(previous!=null) previous.Dispose();
            }
        }
        static bool IsHighlighted(ToolStripItem item) {
            var entry=item as ToolStripMenuItem;
            return item.Enabled && (item.Selected || (entry!=null && entry.HasDropDownItems && entry.DropDown.Visible));
        }
        static ToolStripItem ActiveChild(ToolStripDropDown menu) {
            foreach(ToolStripItem child in menu.Items)
                if(child.Available && IsHighlighted(child)) return child;
            return null;
        }
        static bool IsAncestor(ToolStripItem item) {
            var branch=item as ToolStripMenuItem;
            return branch!=null && branch.HasDropDownItems && branch.DropDown.Visible && ActiveChild(branch.DropDown)!=null;
        }
        static bool IsCurrent(ToolStripItem item) { return IsHighlighted(item) && !IsAncestor(item); }
        static void InvalidateOwners(ToolStripDropDown menu) {
            for(var owner=menu.OwnerItem;owner!=null;) {
                var strip=owner.Owner;
                if(strip==null) return;
                strip.Invalidate(owner.Bounds);
                if(strip.Visible) strip.Update();
                var parent=strip as ToolStripDropDown;
                owner=parent==null?null:parent.OwnerItem;
            }
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            using(var brush=new SolidBrush(Surface))
                e.Graphics.FillRectangle(brush,e.AffectedBounds);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            float scale=e.Graphics.DpiX/96f;
            using(var edge=new Pen(Edge,Math.Max(1,scale)))
            using(var round=Dashboard.Rounded(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),
                         SystemInformation.HighContrast ? 1 : 9*scale)) {
                var saved=e.Graphics.Save();
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; e.Graphics.DrawPath(edge,round);
                e.Graphics.Restore(saved);
            }
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if(!IsHighlighted(e.Item)) return;
            float scale=e.Graphics.DpiX/96f;
            using(var brush=new SolidBrush(IsAncestor(e.Item)?AncestorFill:Selection))
            using(var path=Dashboard.Rounded(new RectangleF(4*scale,scale,e.Item.Width-8*scale,e.Item.Height-2*scale),5*scale)) {
                var saved=e.Graphics.Save(); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush,path); e.Graphics.Restore(saved);
            }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            var ink=e.Item.Enabled ? (IsCurrent(e.Item)?SelectedInk:MacStyle.Ink) : MacStyle.Muted;
            TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,ink,e.TextFormat |
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|
                TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            var area=e.ImageRectangle; float scale=e.Graphics.DpiX/96f;
            float x=area.X+area.Width/2f, y=area.Y+area.Height/2f;
            using(var pen=new Pen(IsCurrent(e.Item)?SelectedInk:MacStyle.Ink,Math.Max(1.5f,1.7f*scale))) {
                var saved=e.Graphics.Save(); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                e.Graphics.DrawLines(pen,new[]{new PointF(x-4*scale,y),new PointF(x-scale,y+3*scale),new PointF(x+5*scale,y-4*scale)});
                e.Graphics.Restore(saved);
            }
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            var r=e.ArrowRectangle; float u=e.Graphics.DpiX/96f;
            float x=r.Left+r.Width/2f,y=r.Top+r.Height/2f;
            float sign=e.Direction==ArrowDirection.Left?-1:1;
            if(e.Direction!=ArrowDirection.Left && e.Direction!=ArrowDirection.Right) {base.OnRenderArrow(e);return;}
            using(var pen=new Pen(IsCurrent(e.Item)?SelectedInk:MacStyle.Muted,Math.Max(1.2f,1.4f*u))) {
                var saved=e.Graphics.Save(); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                e.Graphics.DrawLines(pen,new[]{new PointF(x-1.5f*u*sign,y-3.5f*u),new PointF(x+2*u*sign,y),new PointF(x-1.5f*u*sign,y+3.5f*u)});
                e.Graphics.Restore(saved);
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            float u=e.Graphics.DpiX/96f,inset=8*u,y=e.Item.Height/2f;
            using(var pen=new Pen(Edge,Math.Max(1,u))) e.Graphics.DrawLine(pen,inset,y,e.Item.Width-inset,y);
        }
    }
}
