using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Codex Battery")]
[assembly: AssemblyProduct("Codex Battery")]
[assembly: AssemblyVersion("1.9.0.0")]

namespace CodexBattery {
    public sealed class Dashboard : Form {
        readonly Preferences prefs;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly CompanionTray companion;
        readonly ContextMenuStrip trayMenu = new TrayMenu(), settings = new ContextMenuStrip();
        readonly MenuStyle menuStyle=new MenuStyle();
        readonly Font menuFont=MacStyle.Text(10.5f);
        MenuHeading menuHeading;
        ToolStripMenuItem primaryLine, secondaryLine, emptyLine, issueLine;
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
        readonly Panel viewport = new Panel(), page = new Panel(), divider = new Panel();
        readonly Panel footer = new Panel();
        // These are logical pixels, matching the macOS panel at its standard display scale.
        const float PointsPerDip=72f/96f;
        readonly Label heading=TextLabel(21,true), status=TextLabel(13,false), petStatus=TextLabel(13,false);
        readonly QuotaRow primary = new QuotaRow(), secondary = new QuotaRow();
        readonly PictureBox portrait = new PictureBox();
        readonly LinkLabel retryPet = new LinkLabel();
        readonly PanelButton settingsButton=new PanelButton(), hideButton=new PanelButton(), refreshButton=new PanelButton(true);
        readonly UsageClient client;
        readonly PetSource pets;
        readonly HashSet<string> alerted = new HashSet<string>();
        Snapshot snapshot = new Snapshot();
        Icon batteryIcon, appIcon;
        readonly Bitmap startupPet;
        Bitmap petImage, fallbackImage;
        string fallbackName;
        PetState petState = PetState.Reading;
        bool awaitingArtwork { get { return petState == PetState.AwaitingArtwork; } }
        bool usesStartupPet { get { return petState == PetState.Reading || petState == PetState.Unconfigured; } }
        bool resizing, userHeight, arranging, arrangePending;
        int dragHeight;
        bool exiting, sessionEnding, petBusy, forcePending, disposedResources;
        DateTime lastRead = DateTime.MinValue, nextPet = DateTime.MinValue;
        string error, batteryKey, petInfo = "正在读取 ChatGPT Pet…", shortcutIssue;

        public Dashboard() : this(Preferences.Load(), new PetSource(), null) {}
        internal Dashboard(Preferences preferences, PetSource source, Func<string,ProcessStartInfo> launch) {
            prefs=preferences; pets=source;
            UiText.Use(prefs.Language);
            companion = new CompanionTray(trayMenu, ShowTrayMenu);
            Text = "Codex Battery";
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = MacStyle.Text(10);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(328,172); ClientSize = new Size(464,270);
            BuildWindow();
            settings.Font=trayMenu.Font=menuFont;
            Action<string> fail=delegate(string detail) { error=detail; Render(); };
            client = launch==null ? new UsageClient(Post, GotUsage, fail) : new UsageClient(Post, GotUsage, fail, launch, null);
            trayMenu.Opening += delegate(object sender,System.ComponentModel.CancelEventArgs e) {
                BuildMenu(trayMenu,true); e.Cancel=false;
            };
            settings.Opening += delegate(object sender,System.ComponentModel.CancelEventArgs e) {
                BuildMenu(settings,false); e.Cancel=false;
            };
            tray.ContextMenuStrip = trayMenu; tray.Text = "Codex Battery";
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if(e.Button==MouseButtons.Left) ShowTrayMenu(); };
            Load += delegate { SetStartupSize(); };
            Shown += delegate {
                RefreshUsage(); PollPet(true); clock.Start();
            };
            ResizeBegin += delegate { resizing=true; dragHeight=Height; };
            ResizeEnd += delegate {
                if (Height!=dragHeight) userHeight=true;
                resizing=false;
                ArrangeWindow();
            };
            clock.Interval=1000;
            clock.Tick += delegate {
                client.Tick(prefs.CliPath,prefs.RefreshSeconds);
                if(DateTime.UtcNow>=nextPet) PollPet(false);
                Render();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!exiting && !sessionEnding && (e.CloseReason==CloseReason.UserClosing || e.CloseReason==CloseReason.None)) {
                    e.Cancel=true;
                    Hide();
                }
            };
            startupPet=pets.ReadStartupPortrait();
            ApplyPetVisual(); tray.Visible=true;
        }
        // Measure and paint with the same GDI flags. WinForms Label otherwise adds
        // proportional side bearings to the layout, most noticeably around a large percentage.
        sealed class PanelLabel : Label {
            const TextFormatFlags Flags=TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.WordBreak | TextFormatFlags.PreserveGraphicsClipping |
                TextFormatFlags.PreserveGraphicsTranslateTransform;
            Font measuredFont;
            float measuredScale;
            int measuredAscent;

            // GDI may include additional ascent when it selects a private face. Use the
            // same physical font as DrawText so the number and its Chinese unit share a baseline.
            [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
            struct TextMetric {
                public int Height,Ascent,Descent,InternalLeading,ExternalLeading,AverageWidth,MaxWidth,
                           Weight,Overhang,AspectX,AspectY;
                public char First,Last,Default,Break;
                public byte Italic,Underlined,StruckOut,Pitch,CharacterSet;
            }
            [DllImport("gdi32.dll",EntryPoint="GetTextMetricsW")]
            static extern bool GetTextMetrics(IntPtr dc,out TextMetric metric);
            [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
            [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
            public int Baseline(float scale) {
                if(Object.ReferenceEquals(Font,measuredFont) && scale==measuredScale) return measuredAscent;
                var family=Font.FontFamily;
                measuredAscent=(int)Math.Round(Font.SizeInPoints/PointsPerDip*scale*
                    family.GetCellAscent(Font.Style)/family.GetEmHeight(Font.Style));
                if(Environment.OSVersion.Platform==PlatformID.Win32NT) {
                    using(var g=CreateGraphics()) {
                        var nativeFont=Font.ToHfont();
                        try {
                            var dc=g.GetHdc();
                            try {
                                var previous=SelectObject(dc,nativeFont);
                                if(previous!=IntPtr.Zero && previous!=new IntPtr(-1)) {
                                    try {
                                        TextMetric metric;
                                        if(GetTextMetrics(dc,out metric)) measuredAscent=metric.Ascent;
                                    } finally { SelectObject(dc,previous); }
                                }
                            } finally { g.ReleaseHdc(dc); }
                        } finally { DeleteObject(nativeFont); }
                    }
                }
                measuredFont=Font; measuredScale=scale;
                return measuredAscent;
            }
            public PanelLabel() {
                UseMnemonic=false; AutoSize=false; AccessibleRole=AccessibleRole.StaticText;
                UseCompatibleTextRendering=false;
            }
            public override Size GetPreferredSize(Size proposedSize) {
                var box=new Size(proposedSize.Width>0 ? proposedSize.Width : Int32.MaxValue,Int32.MaxValue);
                // The overload without a device context ignores NoPadding on .NET Framework.
                using(var dc=CreateGraphics()) return TextRenderer.MeasureText(dc,Text,Font,box,Flags);
            }
            protected override void OnPaint(PaintEventArgs e) {
                // A native text DC respects the user's Windows font-smoothing and ClearType settings.
                TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,Flags);
            }
        }
        static PanelLabel TextLabel(float dip,bool medium) {
            return new PanelLabel { Font=MacStyle.Text(dip*PointsPerDip,medium), ForeColor=MacStyle.Ink };
        }
        internal static GraphicsPath Rounded(RectangleF bounds,float radius) {
            var path=new GraphicsPath();
            float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            if (d<=0) return path;
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90);
            path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);
            path.CloseFigure();
            return path;
        }
        sealed class PanelButton : Button {
            bool hover,pressed;
            readonly bool refreshGlyph;
            public PanelButton(bool refreshGlyph=false) {
                this.refreshGlyph=refreshGlyph; Font=MacStyle.Text(13*PointsPerDip); FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover=true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover=pressed=false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed=true; Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed=false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) {
                var g=e.Graphics;
                float u=g.DpiX/96f;
                using(var surface=new SolidBrush(MacStyle.Surface)) g.FillRectangle(surface,ClientRectangle);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                Color fill=pressed?MacStyle.ButtonPressed:hover?MacStyle.ButtonHover:MacStyle.ButtonFace;
                using(var path=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),7*u))
                using(var paper=new SolidBrush(fill))
                using(var pen=new Pen(Focused&&ShowFocusCues ? SystemColors.Highlight:MacStyle.Line,Math.Max(1,(Focused&&ShowFocusCues ? 2:1)*u))) {
                    g.FillPath(paper,path); g.DrawPath(pen,path);
                }
                var textBounds=ClientRectangle;
                Color ink=Enabled?MacStyle.Ink:SystemColors.GrayText;
                if(refreshGlyph) {
                    float d=10*u,x=9*u,y=(Height-d)/2;
                    using(var pen=new Pen(ink,Math.Max(1.2f,1.4f*u))) {
                        g.DrawArc(pen,x,y,d,d,12,292);
                        g.DrawLines(pen,new[]{new PointF(x+d-3.6f*u,y+3.1f*u),new PointF(x+d,y+3.1f*u),new PointF(x+d,y-.5f*u)});
                    }
                    textBounds.X+=(int)(17*u); textBounds.Width-=(int)(17*u);
                }
                TextRenderer.DrawText(g,Text,Font,textBounds,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
            }
        }
        sealed class QuotaRow : Panel {
            readonly PanelLabel caption=TextLabel(13,true), reset=TextLabel(13,false), amount=new PanelLabel(), unit=TextLabel(13,false);
            Rectangle track;
            int remaining;
            Color tint;
            public string Caption { get { return caption.Text; } }
            public QuotaRow() {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
                amount.Font=MacStyle.Number(37*PointsPerDip);
                reset.ForeColor=unit.ForeColor=MacStyle.Muted;
                foreach(var label in new[]{caption,reset,amount,unit}) { label.BackColor=MacStyle.Card; Controls.Add(label); }
            }
            public bool SetQuota(Quota quota,bool old,string color) {
                string title=quota==null?UiText.T("可用额度"):UiText.Period(quota);
                string when=quota==null?UiText.T("尚无周期数据"):quota.Reset.HasValue?FormatReset(quota):UiText.T("重置时间未提供");
                string measure=quota==null?"—":quota.Remaining+"%";
                string description=quota==null?"":old?UiText.T("上次剩余"):UiText.T("剩余");
                bool changed=caption.Text!=title || reset.Text!=when || amount.Text!=measure || unit.Text!=description;
                caption.Text=title; reset.Text=when; amount.Text=measure; unit.Text=description;
                amount.AccessibleName=title+" "+description+" "+measure;
                amount.ForeColor=old || quota==null ? MacStyle.Muted : MacStyle.Ink;
                int level=quota==null?0:quota.Remaining;
                Color next=MacStyle.Capacity(level,color,old || quota==null);
                if(remaining!=level || tint!=next) { remaining=level; tint=next; Invalidate(); }
                return changed;
            }
            public int ArrangeRow(int width,float scale) {
                int inset=(int)Math.Round(16*scale), content=width-inset*2;
                int y=PlaceLabel(caption,inset,(int)Math.Round(14*scale),content);
                int top=y+(int)Math.Round(6*scale);
                int valueWidth=Math.Min(Math.Max(1,content),amount.GetPreferredSize(Size.Empty).Width);
                y=PlaceLabel(amount,inset,top,valueWidth);
                int nextX=amount.Right+(int)Math.Round(7*scale);
                if(unit.Text.Length>0) {
                    int unitTop=top+amount.Baseline(scale)-unit.Baseline(scale);
                    y=Math.Max(y,PlaceLabel(unit,nextX,unitTop,width-inset-nextX));
                }
                unit.Visible=unit.Text.Length>0;
                track=new Rectangle(inset,y+(int)Math.Round(8*scale),Math.Max(1,content),Math.Max(3,(int)Math.Round(6*scale)));
                y=PlaceLabel(reset,inset,track.Bottom+(int)Math.Round(9*scale),content);
                Invalidate();
                return y+(int)Math.Round(14*scale);
            }
            public void VerifyTypography(float scale) {
                int valueLine=amount.Top+amount.Baseline(scale), unitLine=unit.Top+unit.Baseline(scale);
                if(!amount.Visible || !unit.Visible || Math.Abs(valueLine-unitLine)>1 ||
                    Math.Abs(unit.Left-amount.Right-7*scale)>1 || amount.Right>ClientSize.Width || track.Top<unit.Bottom)
                    throw new InvalidDataException("Quota text baseline, spacing or bar overlap");
            }
            protected override void OnPaintBackground(PaintEventArgs e) {
                var g=e.Graphics; float u=g.DpiX/96f;
                using(var surface=new SolidBrush(MacStyle.Surface)) g.FillRectangle(surface,ClientRectangle);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var card=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),12*u))
                using(var paper=new SolidBrush(MacStyle.Card))
                using(var edge=new Pen(MacStyle.Line,Math.Max(1,u))) { g.FillPath(paper,card); g.DrawPath(edge,card); }
                if(track.Width<1 || track.Height<1) return;
                using(var bar=Rounded(track,track.Height/2f))
                using(var empty=new SolidBrush(MacStyle.Track))
                using(var fill=new SolidBrush(tint)) {
                    g.FillPath(empty,bar);
                    if(remaining>0) {
                        float width=(float)(track.Width*remaining/100.0);
                        using(var part=Rounded(new RectangleF(track.Left,track.Top,width,track.Height),track.Height/2f)) g.FillPath(fill,part);
                    }
                }
            }
        }
        // Fixed width plus measured height keeps Chinese and mixed-script labels intact when they wrap.
        static int PlaceLabel(Label label,int x,int y,int width) {
            width=Math.Max(1,width);
            label.AutoSize=false;
            var preferred=label.GetPreferredSize(new Size(width,0));
            int height=Math.Max(label.Font.Height,preferred.Height);
            label.SetBounds(x,y,width,height);
            return y+height;
        }
        void UpdateWindowLabels() {
            heading.Text=UiText.T("Codex 剩余额度");
            retryPet.Text=UiText.T("重新同步");
            refreshButton.Text=UiText.T("刷新");
            refreshButton.AccessibleName=UiText.T("立即刷新 Codex 额度");
            settingsButton.Text=UiText.T("设置");
            hideButton.Text=UiText.T("收起到托盘");
        }
        void ChangeLanguage(string value) {
            prefs.Language=value;
            UiText.Use(value);
            UpdateWindowLabels();
            Render();
            ArrangeWindow();
        }
        int ButtonWidth(Button button,float scale,int minimum,int padding) {
            using(var g=button.CreateGraphics()) {
                int textWidth=TextRenderer.MeasureText(g,button.Text,button.Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                return Math.Max((int)Math.Ceiling(minimum*scale),textWidth+(int)Math.Ceiling(padding*scale));
            }
        }
        void BuildWindow() {
            viewport.Dock=DockStyle.Fill; viewport.AutoScroll=true;
            viewport.BackColor=MacStyle.Surface; viewport.ForeColor=MacStyle.Ink;
            page.BackColor=viewport.BackColor; page.ForeColor=viewport.ForeColor; BackColor=MacStyle.Surface;
            viewport.Controls.Add(page);
            UpdateWindowLabels();
            status.ForeColor=petStatus.ForeColor=MacStyle.Muted;
            portrait.SizeMode=PictureBoxSizeMode.Zoom; portrait.Visible=false; portrait.AccessibleName="ChatGPT Pet";
            divider.BackColor=MacStyle.Line;
            retryPet.Font=petStatus.Font; retryPet.AutoSize=false; retryPet.UseMnemonic=false;
            retryPet.LinkColor=MacStyle.Accent; retryPet.ActiveLinkColor=MacStyle.Accent;
            retryPet.Visible=false;
            retryPet.LinkClicked+=delegate { PollPet(true); };
            page.Controls.AddRange(new Control[]{heading,status,portrait,refreshButton,primary,secondary,petStatus,retryPet});
            footer.Dock=DockStyle.Bottom; footer.BackColor=MacStyle.Surface;
            refreshButton.Click+=delegate { RefreshUsage(); };
            settingsButton.Click+=delegate { settings.Show(settingsButton,new Point(0,settingsButton.Height)); };
            hideButton.Click+=delegate { Hide(); };
            footer.Controls.AddRange(new Control[]{settingsButton,hideButton,divider});
            Controls.Add(viewport); Controls.Add(footer);
            viewport.ClientSizeChanged+=delegate { QueueArrange(); };
            footer.SizeChanged+=delegate { QueueArrange(); };
            KeyPreview=true;
            KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.KeyCode==Keys.F5) { RefreshUsage(); e.Handled=true; }
                if(e.KeyCode==Keys.Escape) { Hide(); e.Handled=true; }
            };
        }
        void SetStartupSize() {
            // WinForms can scale the fonts without scaling a programmatically constructed form's bounds.
            float scale;
            using(var g=CreateGraphics()) scale=g.DpiX/96f;
            var area=Screen.FromControl(this).WorkingArea;
            int minWidth=Math.Min(area.Width,(int)Math.Round(328*scale));
            int minHeight=Math.Min(area.Height,(int)Math.Round(172*scale));
            MinimumSize=new Size(minWidth,minHeight);
            var size=SizeFromClientSize(new Size((int)Math.Round(464*scale),(int)Math.Round(270*scale)));
            int width=Math.Min(area.Width,size.Width), height=Math.Min(area.Height,size.Height);
            Bounds=new Rectangle(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height);
            footer.Height=(int)Math.Round(53*scale);
        }
        void QueueArrange() {
            if(arrangePending || disposedResources || !IsHandleCreated || !Visible) return;
            arrangePending=true;
            BeginInvoke((Action)delegate {
                arrangePending=false;
                if(!disposedResources) ArrangeWindow();
            });
        }
        void ArrangeWindow() {
            if(arranging || !IsHandleCreated || !Visible || WindowState==FormWindowState.Minimized) return;
            arranging=true;
            try {
                float scale;
                using(var g=CreateGraphics()) scale=g.DpiX/96f;
                int pad=(int)Math.Round(22*scale), gap=(int)Math.Round(10*scale), small=(int)Math.Round(4*scale);
                int width=viewport.ClientSize.Width;
                int available=Math.Max(1,width-pad*2);
                int petEdge=(int)Math.Round(prefs.PetSize*scale);
                int textX=pad+(portrait.Visible?petEdge+gap:0);
                int refreshWidth=ButtonWidth(refreshButton,scale,69,32), buttonHeight=(int)Math.Round(29*scale);
                int textWidth=Math.Max(1,width-pad-refreshWidth-gap-textX);
                int titleHeight=heading.GetPreferredSize(new Size(textWidth,0)).Height;
                int statusHeight=status.GetPreferredSize(new Size(textWidth,0)).Height;
                int headerHeight=Math.Max(portrait.Visible?petEdge:0,Math.Max(buttonHeight,titleHeight+small+statusHeight));
                int top=(int)Math.Round(17*scale);
                int y=PlaceLabel(heading,textX,top+(headerHeight-titleHeight-small-statusHeight)/2,textWidth);
                PlaceLabel(status,textX,y+small,textWidth);
                if(portrait.Visible) portrait.SetBounds(pad,top+(headerHeight-petEdge)/2,petEdge,petEdge);
                refreshButton.SetBounds(width-pad-refreshWidth,top+(headerHeight-buttonHeight)/2,refreshWidth,buttonHeight);
                y=top+headerHeight+(int)Math.Round(16*scale);
                if(secondary.Visible && available>=418*scale) {
                    int left=(available-gap)/2, right=available-gap-left;
                    int height=Math.Max(primary.ArrangeRow(left,scale),secondary.ArrangeRow(right,scale));
                    primary.SetBounds(pad,y,left,height);
                    secondary.SetBounds(pad+left+gap,y,right,height);
                    y+=height+gap;
                } else {
                    foreach(var row in new[]{primary,secondary}) if(row.Visible) {
                        int height=row.ArrangeRow(available,scale);
                        row.SetBounds(pad,y,available,height);
                        y=row.Bottom+gap;
                    }
                }
                int footerWidth=footer.ClientSize.Width;
                divider.SetBounds(pad,0,Math.Max(1,footerWidth-pad*2),Math.Max(1,(int)scale));
                int toolbarY=(footer.Height-buttonHeight)/2, hideWidth=ButtonWidth(hideButton,scale,99,18);
                settingsButton.SetBounds(pad,toolbarY,ButtonWidth(settingsButton,scale,60,18),buttonHeight);
                hideButton.SetBounds(footerWidth-pad-hideWidth,toolbarY,hideWidth,buttonHeight);
                if(petStatus.Visible) {
                    int linkWidth=retryPet.Visible ? retryPet.GetPreferredSize(Size.Empty).Width+small : 0;
                    int textEnd=PlaceLabel(petStatus,pad,y,available-(retryPet.Visible?linkWidth+gap:0));
                    if(retryPet.Visible) {
                        int linkEnd=PlaceLabel(retryPet,width-pad-linkWidth,y,linkWidth);
                        textEnd=Math.Max(textEnd,linkEnd);
                    }
                    y=textEnd+gap;
                }
                page.Size=new Size(width,y+small);
                if (!userHeight && !resizing && WindowState==FormWindowState.Normal) {
                    int nonClient=Height-ClientSize.Height;
                    var area=Screen.FromControl(this).WorkingArea;
                    int target=Math.Min(area.Height,page.Height+footer.Height+nonClient);
                    if(Height!=Math.Max(MinimumSize.Height,target)) {
                        Height=Math.Max(MinimumSize.Height,target);
                        QueueArrange();
                    }
                }
            } finally { arranging=false; }
        }
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
        internal void ShowTrayMenu() {
            if(trayMenu.Visible || disposedResources) return;
            trayMenu.Show(Cursor.Position);
            if(trayMenu.Visible) SetForegroundWindow(trayMenu.Handle);
        }
        static ToolStripMenuItem Info(ToolStripItemCollection items,string title) {
            var row=new ToolStripMenuItem(title) { Enabled=false };
            items.Add(row); return row;
        }
        void UpdateQuotaLine(ToolStripMenuItem line,Quota quota,string slot,DateTime now) {
            line.Available=quota!=null;
            if(quota==null) { line.Text=""; line.ToolTipText=""; return; }
            bool old=error!=null || snapshot.IsStale(lastRead,now,slot);
            string label=UiText.Period(quota)+"    "+quota.Remaining+"%"+(old?UiText.T("（上次读数）"):"");
            if(line.Text!=label) line.Text=label;
            line.ToolTipText=FormatReset(quota);
        }
        void UpdateTraySummary() {
            if(primaryLine==null) return;
            DateTime now=DateTime.Now;
            var selected=snapshot.Select(prefs.Window);
            int? remaining=selected!=null && error==null && !snapshot.IsStale(lastRead,now,prefs.Window) ? (int?)selected.Remaining : null;
            menuHeading.Update(prefs.CompactTray?null:portrait.Image,remaining,prefs.Color);
            UpdateQuotaLine(primaryLine,snapshot.Primary,"primary",now);
            UpdateQuotaLine(secondaryLine,snapshot.Secondary,"secondary",now);
            emptyLine.Available=snapshot.Primary==null && snapshot.Secondary==null;
            emptyLine.Text=emptyLine.Available ? (client!=null && client.IsRefreshing ? UiText.T("正在读取…") : UiText.T("暂无额度数据")) : "";
            issueLine.Available=error!=null;
            issueLine.Text=error!=null?UiText.T("连接异常 · 打开窗口查看原因"):"";
        }
        void BuildMenu(ContextMenuStrip menu,bool fromTray) {
            menu.SuspendLayout();
            try {
                while(menu.Items.Count>0) {
                    var item=menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose();
                }
                if(fromTray) {
                    menuHeading=new MenuHeading(); menu.Items.Add(menuHeading);
                    primaryLine=Info(menu.Items,"");
                    secondaryLine=Info(menu.Items,"");
                    emptyLine=Info(menu.Items,"");
                    issueLine=Info(menu.Items,"");
                    UpdateTraySummary();
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(UiText.T("打开额度窗口…"),null,delegate { Reveal(); });
                    menu.Items.Add(UiText.T("立即刷新"),null,delegate { RefreshUsage(); });
                    var submenu=new ToolStripMenuItem(UiText.T("设置")); menu.Items.Add(submenu);
                    BuildSettings(submenu.DropDownItems);
                    menu.Items.Add(new ToolStripSeparator());
                } else {
                    BuildSettings(menu.Items);
                }
                menu.Items.Add(UiText.T("退出"),null,delegate { Shutdown(); });
                using(var g=CreateGraphics()) menuStyle.Format(menu,menuFont,g.DpiX/96f);
            } finally { menu.ResumeLayout(true); }
        }
        void BuildSettings(ToolStripItemCollection entries) {
            var periods=new ToolStripMenuItem(UiText.T("显示周期")); entries.Add(periods);
            if(snapshot.Primary!=null && snapshot.Secondary!=null) {
                Option(periods,UiText.Period(snapshot.Primary),prefs.Window=="primary",delegate { prefs.Window="primary"; });
                Option(periods,UiText.Period(snapshot.Secondary),prefs.Window=="secondary",delegate { prefs.Window="secondary"; });
                Option(periods,UiText.T("剩余较少的周期"),prefs.Window=="minimum",delegate { prefs.Window="minimum"; });
            } else {
                var q=snapshot.Primary??snapshot.Secondary;
                Info(periods.DropDownItems,q==null?UiText.T("等待读取额度"):UiText.F("当前账号仅有{0}",UiText.Period(q)));
            }
            var languages=new ToolStripMenuItem(UiText.T("语言")); entries.Add(languages);
            foreach(string choice in new[]{"system","zh","ja","en"}) {
                string code=choice;
                string title=code=="system" ? UiText.T("跟随系统") : code=="zh" ? "简体中文" : code=="ja" ? "日本語" : "English";
                Option(languages,title,prefs.Language==code,delegate { ChangeLanguage(code); });
            }
            var colors=new ToolStripMenuItem(UiText.T("电池颜色")); entries.Add(colors);
            foreach(var pair in new[]{new[]{UiText.T("按余额变色"),"auto"},new[]{UiText.T("青绿色"),"teal"},new[]{UiText.T("经典单色"),"mono"}}) {
                string value=pair[1]; Option(colors,pair[0],prefs.Color==value,delegate {prefs.Color=value;});
            }
            var icons=new ToolStripMenuItem(UiText.T("托盘图标大小")); entries.Add(icons);
            foreach(var pair in new[]{new[]{UiText.T("小"),"small"},new[]{UiText.T("标准"),"normal"},new[]{UiText.T("大"),"large"}}) {
                string value=pair[1]; Option(icons,pair[0],prefs.TraySize==value,delegate {prefs.TraySize=value;});
            }
            var compact=new ToolStripMenuItem(UiText.T("紧凑托盘（仅电池）")){Checked=prefs.CompactTray};
            compact.Click+=delegate { prefs.CompactTray=!prefs.CompactTray; Save(); Render(); }; entries.Add(compact);
            var pet=new ToolStripMenuItem("ChatGPT Pet"); entries.Add(pet);
            Info(pet.DropDownItems,UiText.PetStatus(petInfo));
            Option(pet,UiText.T("显示 ChatGPT 的 Pet"),prefs.ShowPet,delegate { prefs.ShowPet=!prefs.ShowPet; ApplyPetVisual(); });
            pet.DropDownItems.Add(UiText.T("重新同步"),null,delegate {PollPet(true);});
            pet.DropDownItems.Add(UiText.T("选择备用图片…"),null,delegate {ChooseFallback();});
            if(PetFallback.HasCustom) pet.DropDownItems.Add(UiText.T("移除自选备用图片"),null,delegate {
                try { PetFallback.Reset(); ReloadFallback(); }
                catch(Exception e) { MessageBox.Show(this,UiText.F("移除备用图片失败。{0}",UiText.T(e.GetBaseException().Message)),Text); }
            });
            pet.DropDownItems.Add(UiText.T("创建跟随 Pet 的快捷方式…"),null,delegate {CreateShortcut();});
            var sizes=new ToolStripMenuItem(UiText.T("窗口宠物大小")); entries.Add(sizes);
            foreach(int n in new[]{32,48,72}) {
                int size=n; string text=n==32?UiText.T("小"):n==48?UiText.T("标准"):UiText.T("大");
                Option(sizes,UiText.F("{0}（{1} 点）",text,size),prefs.PetSize==size,delegate {prefs.PetSize=size; ArrangeWindow();});
            }
            entries.Add(new ToolStripSeparator());
            var cadence=new ToolStripMenuItem(UiText.T("自动刷新")); entries.Add(cadence);
            foreach(int n in new[]{30,60,120}) {
                int seconds=n; Option(cadence,UiText.F("每 {0} 秒",seconds),prefs.RefreshSeconds==seconds,delegate {prefs.RefreshSeconds=seconds;});
            }
            var alert=new ToolStripMenuItem(UiText.T("剩余 20% 时提醒")){Checked=prefs.LowAlert};
            alert.Click+=delegate {prefs.LowAlert=!prefs.LowAlert; Save();}; entries.Add(alert);
            entries.Add(UiText.T("选择 Codex 程序…"),null,delegate {ChooseCli();});
            entries.Add(new ToolStripSeparator());
            Info(entries,"Codex Battery "+typeof(Dashboard).Assembly.GetName().Version.ToString(2));
        }
        void Option(ToolStripMenuItem parent,string title,bool selected,Action change) {
            var entry=new ToolStripMenuItem(title){Checked=selected};
            entry.Click+=delegate {change();Save();Render();}; parent.DropDownItems.Add(entry);
        }
        void Save() {
            try { prefs.Save(); } catch(Exception e) { MessageBox.Show(this,UiText.F("保存设置失败。{0}",UiText.T(e.Message)),Text); }
        }
        public void Reveal() {
            Show();
            if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
            Render(); ArrangeWindow(); Activate(); BringToFront();
        }
        void ChooseCli() {
            using(var d=new OpenFileDialog{Title=UiText.T("选择 Codex"),Filter=UiText.T("Codex 程序")+"|codex.exe;codex.cmd"}) {
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                prefs.CliPath=d.FileName; Save(); client.Stop(); RefreshUsage();
            }
        }
        void Post(Action action) {
            try { if(!IsDisposed && IsHandleCreated) BeginInvoke(action); } catch(InvalidOperationException) {}
        }
        void RefreshUsage() { client.Refresh(prefs.CliPath); Render(); }
        void GotUsage(Snapshot data) {
            snapshot=data; lastRead=DateTime.Now; error=null;
            if(prefs.LowAlert) foreach(var q in new[]{data.Primary,data.Secondary}.Where(q=>q!=null && q.Remaining<=20)) {
                if(alerted.Add(q.Name+"|"+q.Reset)) tray.ShowBalloonTip(4000,UiText.T("Codex 额度提醒"),UiText.F("{0}剩余 {1}%",UiText.Period(q),q.Remaining),ToolTipIcon.Warning);
            }
            Render();
        }
        void PollPet(bool force) {
            if(petBusy){forcePending |= force;return;}
            petBusy=true; nextPet=DateTime.UtcNow.AddSeconds(6);
            ThreadPool.QueueUserWorkItem(delegate {
                PetSelection selection=null; string issue=null;
                try { selection=pets.Read(force); } catch(Exception){issue="暂时无法读取本机 Pet，稍后重试";}
                var result=selection;
                try { BeginInvoke((Action)delegate {
                    petBusy=false;
                    if(!exiting && !IsDisposed) {
                        if(result!=null) {
                            var old=petImage;
                            petImage=result.Portrait; petInfo=result.Status;
                            if(result.State!=PetState.Unreadable || !usesStartupPet) petState=result.State;
                            try { ApplyPetVisual(); } finally { if(old!=null)old.Dispose(); }
                        } else if(issue!=null){petInfo=issue; Render();}
                        else if(force){ApplyPetVisual();}
                        if(forcePending){forcePending=false;PollPet(true);}
                    } else if(result!=null && result.Portrait!=null)result.Portrait.Dispose();
                }); } catch(InvalidOperationException){ if(result!=null && result.Portrait!=null) result.Portrait.Dispose(); }
            });
        }
        void ChooseFallback() {
            using(var dialog=new OpenFileDialog { Title=UiText.T("选择备用 Pet 图片"),Filter=UiText.T("PNG 或 JPEG 图片")+"|*.png;*.jpg;*.jpeg" }) {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try { PetFallback.Import(dialog.FileName); ReloadFallback(); }
                catch(Exception e) { MessageBox.Show(this,UiText.F("读取图片失败。{0}",UiText.T(e.GetBaseException().Message)),Text); }
            }
        }
        void ReloadFallback() {
            var old=fallbackImage;
            fallbackImage=null; fallbackName=null;
            try { ApplyPetVisual(); } finally { if(old!=null) old.Dispose(); }
            PollPet(true);
        }
        void ApplyPetVisual() {
            if (prefs.ShowPet && awaitingArtwork && fallbackImage==null) {
                try { fallbackImage=PetFallback.Load(out fallbackName); }
                catch (IOException) { } catch(UnauthorizedAccessException) { }
                catch (ArgumentException) { } catch(System.Runtime.InteropServices.ExternalException) { }
            }
            Image current=usesStartupPet ? startupPet : petImage;
            if(awaitingArtwork && fallbackImage!=null) current=fallbackImage;
            if(!prefs.ShowPet) current=null;
            portrait.Image=current; portrait.Visible=current!=null;
            using(var art=IconArt.AppImage(current,128)) {
                var next=IconArt.ToIcon(art); Icon=next;
                var previous=appIcon;appIcon=next;if(previous!=null)previous.Dispose();
            }
            shortcutIssue=null;
            // A previously synchronized shortcut keeps its last image while the app discovers the selection.
            if(!usesStartupPet && petState!=PetState.Unreadable && !string.IsNullOrEmpty(prefs.Shortcut)) {
                try { PetShortcut.Save(prefs.Shortcut,current,false); } catch(Exception){shortcutIssue="快捷方式图标暂未更新";}
            }
            Render(); ArrangeWindow();
        }
        void CreateShortcut() {
            using(var dialog=new SaveFileDialog {Title=UiText.T("保存跟随 Pet 的快捷方式"),Filter=UiText.T("Windows 快捷方式")+"|*.lnk",FileName="Codex Battery.lnk",
                AddExtension=true,DefaultExt="lnk",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DereferenceLinks=false}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try { PetShortcut.Save(dialog.FileName,portrait.Image,true); prefs.Shortcut=dialog.FileName; shortcutIssue=null; Save(); }
                catch(Exception e){MessageBox.Show(this,UiText.F("创建快捷方式失败。{0}",UiText.T(e.GetBaseException().Message)),Text);}
            }
        }
        static string FormatReset(Quota quota) {
            if (quota==null || !quota.Reset.HasValue) return "";
            DateTime reset=quota.Reset.Value;
            TimeSpan left=reset-DateTime.Now;
            string until=left.TotalSeconds<=0 ? UiText.T("已到重置时间") :
                left.TotalDays>=1 ? ((int)left.TotalDays==1 ? UiText.T("1 天后") : UiText.F("{0} 天后",(int)left.TotalDays)) :
                left.TotalHours>=1 ? UiText.F("{0} 小时后",(int)left.TotalHours) : UiText.F("{0} 分钟后",Math.Ceiling(left.TotalMinutes));
            string pattern=UiText.Language=="en" ? "MMM d, HH:mm" : "M月d日 HH:mm";
            return UiText.F("重置于 {0} · {1}",reset.ToString(pattern,UiText.Culture),until);
        }
        void Render() {
            var q=snapshot.Select(prefs.Window);
            bool stale=error!=null || lastRead==DateTime.MinValue || snapshot.IsStale(lastRead,DateTime.Now,prefs.Window);
            int? percent=!stale && q!=null?(int?)q.Remaining:null;
            int pixels=Math.Max(16,SystemInformation.SmallIconSize.Width);
            int content=TrayArt.ContentEdge(pixels,prefs.TraySize);
            companion.Update(prefs.CompactTray ? null : portrait.Image,pixels,content);
            string key=percent+"|"+prefs.Color+"|"+pixels+"|"+content;
            if(key!=batteryKey) {
                using(var bmp=TrayArt.Battery(pixels,content,percent,prefs.Color)) {
                    var icon=IconArt.ToIcon(bmp); tray.Icon=icon;
                    var old=batteryIcon;batteryIcon=icon;if(old!=null)old.Dispose();
                }
                batteryKey=key;
            }
            string tip=percent.HasValue ? "Codex · "+UiText.F("{0}剩余 {1}%",UiText.Period(q),percent) : "Codex · "+(error!=null?UiText.T("连接异常"):lastRead==DateTime.MinValue?UiText.T("等待读取"):UiText.T("暂无最新额度"));
            if(tray.Text!=tip)tray.Text=tip;
            if(trayMenu.Visible) UpdateTraySummary();
            if(!Visible) return;
            bool layoutChanged=primary.SetQuota(snapshot.Primary??snapshot.Secondary,stale,prefs.Color);
            bool both=snapshot.Primary!=null && snapshot.Secondary!=null;
            layoutChanged |= both!=secondary.Visible; secondary.Visible=both;
            if(both) layoutChanged |= secondary.SetQuota(snapshot.Secondary,stale,prefs.Color);
            string oldStatus=status.Text, oldPetStatus=petStatus.Text;
            bool oldRetry=retryPet.Visible, oldShowStatus=petStatus.Visible;
            string age=lastRead==DateTime.MinValue?"":UiText.F("更新于 {0}",lastRead.ToString("HH:mm:ss",UiText.Culture));
            if(client!=null && client.IsRefreshing) status.Text=age.Length==0?UiText.T("正在读取额度…"):UiText.F("正在刷新 · {0}",age);
            else if(error!=null) status.Text=UiText.T(error)+(age.Length==0?"":"\n"+age);
            else status.Text=age.Length==0?UiText.T("正在读取额度…"):(stale?UiText.F("数据已过期 · {0}",age):age);
            bool showingFallback=awaitingArtwork && portrait.Image!=null;
            petStatus.Visible=prefs.ShowPet || shortcutIssue!=null;
            string petCaption=UiText.PetStatus(petInfo);
            if(awaitingArtwork) {
                bool generic=petInfo=="等待 ChatGPT 加载所选 Pet 的图像";
                if(generic) petCaption=UiText.T("Pet 原图待同步");
                if(showingFallback && fallbackImage!=null) {
                    string source=String.IsNullOrWhiteSpace(fallbackName)?UiText.T("自选备用图片"):UiText.T(fallbackName);
                    petCaption+=" · "+source;
                }
            }
            petStatus.Text=(prefs.ShowPet?petCaption:"")+
                (shortcutIssue==null?"":(prefs.ShowPet?" · ":"")+UiText.T(shortcutIssue));
            retryPet.Visible=prefs.ShowPet && awaitingArtwork;
            if(layoutChanged || oldStatus!=status.Text || oldPetStatus!=petStatus.Text ||
                oldRetry!=retryPet.Visible || oldShowStatus!=petStatus.Visible) ArrangeWindow();
        }
        internal bool HasFixtureData { get { return lastRead!=DateTime.MinValue && petState==PetState.Selected && petImage!=null; } }
        internal bool NoPetConfigured { get { return petState==PetState.Unconfigured; } }
        internal void VerifyStartupPet() {
            if(!usesStartupPet || !companion.Visible || startupPet==null || portrait.Image!=startupPet ||
                appIcon==null || appIcon!=Icon || retryPet.Visible)
                throw new InvalidDataException("Expected the built-in Codex pet in the window and notification area");
            using(var expected=new Bitmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","Pets","codex.png"))) {
                if(expected.Size!=startupPet.Size) throw new InvalidDataException("Incorrect startup pet dimensions");
                for(int y=0;y<expected.Height;y++) for(int x=0;x<expected.Width;x++)
                    if(expected.GetPixel(x,y).ToArgb()!=startupPet.GetPixel(x,y).ToArgb()) throw new InvalidDataException("Incorrect startup pet artwork");
            }
        }

        internal bool CurrentlyUsingFallback { get {
            return awaitingArtwork && portrait.Visible && portrait.Image!=null;
        } }
        internal bool AwaitingWithoutPortrait { get {
            return awaitingArtwork && prefs.ShowPet && !portrait.Visible && portrait.Image==null;
        } }
        internal bool PetExplicitlyOff { get {
            return petState==PetState.Disabled && !portrait.Visible && !companion.Visible && !retryPet.Visible;
        } }
        void VerifyGeometry() {
            ArrangeWindow();
            foreach(var control in new Control[]{primary,secondary,portrait,heading,status,petStatus,retryPet,refreshButton,settingsButton,hideButton}) {
                if(!control.Visible) continue;
                var bounds=RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                if(!ClientRectangle.Contains(bounds)) throw new InvalidDataException("Control clipped: "+control.GetType().Name+" "+bounds);
            }
            if(!userHeight && Math.Abs(footer.Top-page.Height)>Math.Max(14,Font.Height*2))
                throw new InvalidDataException("Unexpected empty area below the dashboard");
        }
        internal void VerifyMissingArtwork() {
            if(!AwaitingWithoutPortrait || companion.Visible || !retryPet.Visible || petStatus.Text.Length==0)
                throw new InvalidDataException("Missing artwork should leave the portrait empty and permit retry");
            VerifyGeometry();
            prefs.ShowPet=false; ApplyPetVisual();
            if(portrait.Visible || petStatus.Visible || retryPet.Visible) throw new InvalidDataException("Hidden pet controls should collapse");
            prefs.ShowPet=true; ApplyPetVisual();
            if(!AwaitingWithoutPortrait || !retryPet.Visible) throw new InvalidDataException("Re-enabling the pet should restore retry");
        }
        internal void VerifyFallback() {
            if(!CurrentlyUsingFallback || petStatus.Text.Length==0 || !retryPet.Visible)
                throw new InvalidDataException("Missing artwork did not show the fallback and retry action");
            VerifyGeometry();
            prefs.ShowPet=false; ApplyPetVisual();
            if(portrait.Visible || petStatus.Visible || retryPet.Visible)
                throw new InvalidDataException("Hidden pet controls should collapse");
            prefs.ShowPet=true; ApplyPetVisual();
            if(!CurrentlyUsingFallback) throw new InvalidDataException("Re-enabling the pet did not restore its image");
        }
        void VerifyTrayMenu() {
            if(tray.ContextMenuStrip!=trayMenu || prefs.CompactTray || !companion.Visible)
                throw new InvalidDataException("Default tray must show the battery and synchronized pet");
            ShowTrayMenu();
            try {
                if(!trayMenu.Visible || trayMenu.Items[0]!=menuHeading || !primaryLine.Available ||
                    !primaryLine.Text.Contains("77%") || secondaryLine.Available || menuHeading.Remaining!=77 ||
                    menuHeading.Portrait!=portrait.Image) throw new InvalidDataException("Tray quota summary");
                var settingsEntry=trayMenu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text==UiText.T("设置"));
                settingsEntry.Select(); settingsEntry.ShowDropDown();
                using(var g=trayMenu.CreateGraphics()) {
                    float scale=g.DpiX/96f;
                    var title=menuHeading.TitleBounds(scale); var badge=menuHeading.BadgeBounds(scale);
                    int textWidth=TextRenderer.MeasureText(g,menuHeading.Text,menuHeading.Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                    if(title.Width<textWidth || title.Right>=badge.Left || badge.Right>menuHeading.Width ||
                        trayMenu.ClientSize.Width-settingsEntry.Width>20*scale)
                        throw new InvalidDataException("Menu header, badge or action column is clipped");
                    Rectangle parent=trayMenu.Bounds,child=settingsEntry.DropDown.Bounds;
                    var screen=Screen.FromRectangle(parent).WorkingArea;
                    if(Math.Max(parent.Left-screen.Left,screen.Right-parent.Right)>=child.Width) {
                        var overlap=Rectangle.Intersect(parent,child);
                        if(overlap.Width>10*scale && overlap.Height>10*scale)
                            throw new InvalidDataException("Settings menu covers its parent");
                    }
                }
                var colors=settingsEntry.DropDownItems.OfType<ToolStripMenuItem>().Single(i=>i.Text==UiText.T("电池颜色"));
                colors.Select(); colors.ShowDropDown();
                var choices=colors.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
                if(!settingsEntry.DropDown.Visible || !colors.DropDown.Visible ||
                    !choices.Select(i=>i.Text).SequenceEqual(new[]{UiText.T("按余额变色"),UiText.T("青绿色"),UiText.T("经典单色")}) ||
                    !choices[0].Checked || choices.Skip(1).Any(i=>i.Checked))
                    throw new InvalidDataException("Nested color menu, navigation or selected option");
                colors.HideDropDown(); settingsEntry.HideDropDown();
                prefs.CompactTray=true; Render();
                if(companion.Visible || menuHeading.Portrait!=null || !tray.Visible)
                    throw new InvalidDataException("Compact mode should leave only the battery");
                prefs.CompactTray=false; Render();
                if(!companion.Visible || menuHeading.Portrait==null) throw new InvalidDataException("Pet tray did not return");
                var fresh=lastRead;
                try {
                    lastRead=DateTime.Now.AddMinutes(-5); Render();
                    if(!primaryLine.Text.Contains(UiText.T("（上次读数）")) || menuHeading.Remaining.HasValue)
                        throw new InvalidDataException("Expired quota was presented as current");
                } finally { lastRead=fresh; Render(); }
            } finally { trayMenu.Close(); }
        }
        void VerifyLanguages() {
            string original=prefs.Language;
            foreach(var choice in new[]{
                new[]{"ja","日本語","設定","週間の利用枠"},new[]{"en","English","Settings","Weekly limit"},
                new[]{"zh","简体中文","设置","每周额度"}}) {
                BuildMenu(settings,false);
                var languages=settings.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text==UiText.T("语言"));
                languages.DropDownItems.OfType<ToolStripMenuItem>().Single(i=>i.Text==choice[1]).PerformClick();
                if(settingsButton.Text!=choice[2] || primary.Caption!=choice[3] ||
                   Preferences.Load(prefs.SettingsFile).Language!=choice[0] || !tray.Text.Contains(choice[3]))
                    throw new InvalidDataException("Language did not switch or persist: "+choice[0]);
                VerifyGeometry();
                VerifyTrayMenu();
                using(var g=CreateGraphics()) {
                    foreach(var button in new[]{settingsButton,hideButton,refreshButton}) {
                        int textWidth=TextRenderer.MeasureText(g,button.Text,button.Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                        int glyphSpace=button==refreshButton ? (int)Math.Round(17*g.DpiX/96f) : 0;
                        if(textWidth+glyphSpace>button.Width) throw new InvalidDataException("Localized button is clipped");
                    }
                }
            }
            ChangeLanguage(original);
        }
        // The fixture publishes this account through the same child process pipe as the real CLI.
        internal void VerifyWindow() {
            if (!tray.Visible || tray.Icon==null || !tray.Text.Contains("77%") || !primary.Caption.Contains(UiText.T("每周额度")) ||
                secondary.Visible || !settingsButton.Visible || !portrait.Visible || appIcon==null) throw new InvalidDataException("Weekly window, pet or tray check failed");
            if(CurrentlyUsingFallback || retryPet.Visible)
                throw new InvalidDataException("Synchronized artwork should replace the fallback");
            VerifyGeometry(); VerifyLanguages();
            using(var g=CreateGraphics()) primary.VerifyTypography(g.DpiX/96f);
            if(settings.Font.Name!=status.Font.Name || trayMenu.Font.Name!=settings.Font.Name)
                throw new InvalidDataException("Menu text does not use the dashboard typeface");
            var saved=snapshot;
            snapshot=new Snapshot { Primary=new Quota { Minutes=300, Used=65, Reset=DateTime.Now.AddHours(2) },
                                    Secondary=saved.Primary??saved.Secondary };
            Render();
            if(!secondary.Visible || primary.Bounds.IntersectsWith(secondary.Bounds) || primary.Caption!=UiText.F("{0} 小时额度",5))
                throw new InvalidDataException("Two quota periods did not expand the dashboard");
            VerifyGeometry();
            int savedWidth=Width;
            Width=MinimumSize.Width; ArrangeWindow();
            if(secondary.Top<primary.Bottom || secondary.Width!=primary.Width)
                throw new InvalidDataException("Narrow dashboard did not stack the quota cards");
            Width=savedWidth; ArrangeWindow();
            userHeight=true; Height+=37; int manualHeight=Height;
            snapshot=saved; Render();
            if(Height!=manualHeight) throw new InvalidDataException("Dashboard discarded a manual height");
            userHeight=false; ArrangeWindow();
            BuildMenu(settings,false);
            var sub=(ToolStripMenuItem)settings.Items[0];
            if(sub.DropDownItems.Count!=1 || sub.DropDownItems[0].Text!=UiText.F("当前账号仅有{0}",UiText.T("每周额度"))) throw new InvalidDataException("Single-window menu check failed");
            var previouslyRead=lastRead; lastRead=DateTime.Now.AddMinutes(-5); Render();
            if(tray.Text.Contains("77%")) throw new InvalidDataException("Stale tray check failed");
            lastRead=previouslyRead; Render();
            Close(); if(Visible || !tray.Visible || IsDisposed) throw new InvalidDataException("Close-to-tray check failed");
            Reveal(); if(!Visible) throw new InvalidDataException("Reopen check failed");
        }
        internal void Shutdown() { exiting=true; Close(); }
        protected override void WndProc(ref Message message) {
            const int WM_CLOSE=0x0010, WM_QUERYENDSESSION=0x0011, WM_ENDSESSION=0x0016;
            if(message.Msg==WM_CLOSE && !exiting && !sessionEnding) {
                Hide();
                message.Result=IntPtr.Zero;
                return;
            }
            if(message.Msg==WM_QUERYENDSESSION || message.Msg==WM_ENDSESSION)
                sessionEnding=message.Msg==WM_QUERYENDSESSION || message.WParam!=IntPtr.Zero;
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !disposedResources) {
                disposedResources=true;
                exiting=true; clock.Stop();clock.Dispose(); if(client!=null)client.Dispose();
                companion.Dispose();
                tray.Visible=false;tray.Dispose();settings.Dispose();trayMenu.Dispose();menuFont.Dispose();
                if(batteryIcon!=null)batteryIcon.Dispose();
                Icon=null; if(appIcon!=null)appIcon.Dispose();
                portrait.Image=null; if(petImage!=null)petImage.Dispose();
                if(startupPet!=null)startupPet.Dispose();
                if(fallbackImage!=null)fallbackImage.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
