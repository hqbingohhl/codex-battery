using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CodexBattery {
    // NotifyIcon opens this same menu for both pictures and for keyboard activation.
    // Capture its anchor once, then place it using the size WinForms actually lays out.
    internal sealed class TrayMenu : ContextMenuStrip {
        Rectangle area;
        Point anchor;
        DockStyle edge;
        int gap;
        bool anchored;

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr child,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string caption);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string caption);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);

        static Rectangle WindowBounds(IntPtr hwnd) {
            NativeRect r;
            return hwnd!=IntPtr.Zero && GetWindowRect(hwnd,out r)
                ? Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom) : Rectangle.Empty;
        }
        static int Clamp(int value,int min,int max) { return Math.Max(min,Math.Min(value,max)); }
        static DockStyle TaskbarSide(Rectangle bar,Rectangle screen) {
            if(bar.Width>=bar.Height)
                return Math.Abs(bar.Top-screen.Top)<Math.Abs(screen.Bottom-bar.Bottom)?DockStyle.Top:DockStyle.Bottom;
            return Math.Abs(bar.Left-screen.Left)<Math.Abs(screen.Right-bar.Right)?DockStyle.Left:DockStyle.Right;
        }
        static DockStyle ReservedSide(Rectangle bounds,Rectangle work) {
            int bottom=bounds.Bottom-work.Bottom,top=work.Top-bounds.Top;
            int left=work.Left-bounds.Left,right=bounds.Right-work.Right;
            if(bottom>=top && bottom>=left && bottom>=right) return DockStyle.Bottom;
            if(top>=left && top>=right) return DockStyle.Top;
            return left>=right ? DockStyle.Left : DockStyle.Right;
        }
        static Rectangle OutsideTaskbar(Rectangle work,Rectangle bar,DockStyle side) {
            int left=work.Left,right=work.Right,top=work.Top,bottom=work.Bottom;
            switch(side) {
                case DockStyle.Top: top=Math.Max(top,bar.Bottom); break;
                case DockStyle.Left: left=Math.Max(left,bar.Right); break;
                case DockStyle.Right: right=Math.Min(right,bar.Left); break;
                default: bottom=Math.Min(bottom,bar.Top); break;
            }
            return left<right && top<bottom ? Rectangle.FromLTRB(left,top,right,bottom) : work;
        }
        static Point ClockEnd(Rectangle work,DockStyle side,int inset) {
            int x=side==DockStyle.Left ? work.Left : work.Right-Math.Min(inset,work.Width/2);
            int y=side==DockStyle.Top ? work.Top : work.Bottom-Math.Min(inset,work.Height/2);
            return new Point(x,y);
        }
        void CaptureAnchor() {
            anchor=Cursor.Position;
            var screen=Screen.FromPoint(anchor);
            area=screen.WorkingArea;
            gap=Math.Max(4,SystemInformation.SmallIconSize.Width/4);
            IntPtr root=GetAncestor(WindowFromPoint(anchor),2); // GA_ROOT
            var name=new StringBuilder(128);
            GetClassName(root,name,name.Capacity);
            string host=name.ToString();
            // A click in Explorer's hidden-icons flyout belongs beside that icon.
            if(host=="NotifyIconOverflowWindow" || host=="TopLevelWindowForOverflowXamlIsland") {
                edge=DockStyle.None;
                return;
            }
            bool onTaskbar=host=="Shell_TrayWnd" || host=="Shell_SecondaryTrayWnd";
            // Work-area coordinates are per monitor. A reserved strip under the pointer is
            // a better anchor than the primary taskbar for a click on another display.
            if(screen.Bounds.Contains(anchor) && !area.Contains(anchor)) {
                if(anchor.Y>=area.Bottom) edge=DockStyle.Bottom;
                else if(anchor.Y<area.Top) edge=DockStyle.Top;
                else edge=anchor.X<area.Left ? DockStyle.Left : DockStyle.Right;
                return;
            }
            IntPtr taskbar=onTaskbar ? root : FindWindow("Shell_TrayWnd",null);
            var bar=WindowBounds(taskbar);
            var barScreen=Screen.FromRectangle(bar);
            // A floating notification toolbar can have the same class as Explorer's docked
            // taskbar. Only use its bounds for a keyboard invocation if it spans the edge.
            if(bar.Width>0 && bar.Height>0 && (onTaskbar || bar.Width>=barScreen.Bounds.Width/2 ||
                                               bar.Height>=barScreen.Bounds.Height/2)) {
                screen=barScreen;
                edge=TaskbarSide(bar,screen.Bounds);
                area=OutsideTaskbar(screen.WorkingArea,bar,edge);
                if(!onTaskbar && !bar.Contains(anchor)) {
                    // Win+B can open the menu while the pointer is on another monitor.
                    var notify=WindowBounds(FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null));
                    anchor=notify.Width>0 && notify.Height>0
                        ? new Point(notify.Left+notify.Width/2,notify.Top+notify.Height/2)
                        : ClockEnd(area,edge,gap*16);
                }
                return;
            }
            // Explorer may be restarting or an alternate shell can own the notification area.
            edge=ReservedSide(screen.Bounds,area);
            if(area==screen.Bounds && anchor.Y>=area.Bottom-gap*16)
                area=Rectangle.FromLTRB(area.Left,area.Top,area.Right,anchor.Y);
            else anchor=ClockEnd(area,edge,gap*16);
        }
        internal static Point Place(Rectangle work,Point at,DockStyle side,Size size,int space) {
            int x=at.X-size.Width/2,y=at.Y-size.Height/2;
            switch(side) {
                case DockStyle.Top: y=work.Top+space; break;
                case DockStyle.Left: x=work.Left+space; break;
                case DockStyle.Right: x=work.Right-space-size.Width; break;
                case DockStyle.Bottom: y=work.Bottom-space-size.Height; break;
                default:
                    x=at.X;
                    y=at.Y+space+size.Height<=work.Bottom ? at.Y+space : at.Y-space-size.Height;
                    break;
            }
            return new Point(Clamp(x,work.Left,work.Right-size.Width),Clamp(y,work.Top,work.Bottom-size.Height));
        }
        internal static void VerifyPlacement() {
            // The secondary monitor has a negative origin and its taskbar occupies 60 pixels.
            var bounds=new Rectangle(-1920,-100,1920,1080);
            var size=new Size(330,260);
            var bars=new[] {new Rectangle(-1920,920,1920,60), new Rectangle(-1920,-100,1920,60),
                            new Rectangle(-1920,-100,60,1080), new Rectangle(-60,-100,60,1080)};
            var sides=new[] {DockStyle.Bottom,DockStyle.Top,DockStyle.Left,DockStyle.Right};
            for(int i=0;i<bars.Length;i++) {
                var bar=bars[i];
                if(TaskbarSide(bar,bounds)!=sides[i]) throw new InvalidOperationException("Taskbar edge");
                var work=OutsideTaskbar(bounds,bar,sides[i]);
                var anchor=new Point(bar.Right-1,bar.Bottom-1);
                var popup=new Rectangle(Place(work,anchor,sides[i],size,6),size);
                if(!work.Contains(popup) || popup.IntersectsWith(bar) ||
                   (sides[i]==DockStyle.Bottom && popup.Bottom!=bar.Top-6) ||
                   (sides[i]==DockStyle.Top && popup.Top!=bar.Bottom+6) ||
                   (sides[i]==DockStyle.Left && popup.Left!=bar.Right+6) ||
                   (sides[i]==DockStyle.Right && popup.Right!=bar.Left-6))
                    throw new InvalidOperationException("Menu covers the taskbar or leaves the monitor");
            }
        }
        protected override void OnOpening(CancelEventArgs e) {
            CaptureAnchor();
            anchored=true;
            base.OnOpening(e);
            if(e.Cancel) anchored=false;
            else Location=Place(area,anchor,edge,Size,gap);
        }
        protected override void SetBoundsCore(int x,int y,int width,int height,BoundsSpecified specified) {
            if(anchored && width>0 && height>0) {
                var at=Place(area,anchor,edge,new Size(width,height),gap);
                x=at.X; y=at.Y;
                specified|=BoundsSpecified.Location;
            }
            base.SetBoundsCore(x,y,width,height,specified);
        }
        protected override void OnClosed(ToolStripDropDownClosedEventArgs e) {
            anchored=false;
            base.OnClosed(e);
        }
    }
}
