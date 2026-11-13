using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AutoSetup
{
    /// <summary>
    /// 한 화면 구성:
    ///   ① 설치 프로그램 목록 (4줄 높이, 넘치면 스크롤)
    ///   ② 윈도우 초기 설정 목록 (그 아래, 남는 공간 전체)
    ///   ③ 추가 앱(winget) - [추가 앱 선택] 을 누르면 오른쪽에 체크 목록이 열림
    /// </summary>
    public class MainForm : Form
    {
        public const string AppName = "Windows 자동 설치 도우미";
        const int VisibleProgramRows = 4;

        readonly string appDir, settingsPath, defaultDir, logDir;
        readonly bool autoStart, justUpdated;
        bool checkUpdates = true;

        readonly List<ProgramItem> programs = new List<ProgramItem>();
        readonly List<RegEntry> customRegs = new List<RegEntry>();
        readonly Dictionary<string, ListViewItem> tweakItems = new Dictionary<string, ListViewItem>();
        readonly List<Control> lockWhileRunning = new List<Control>();

        float scale = 1f;
        int serialCount, widthBeforeWinget;
        bool rebuilding, progDoubleClick, setDoubleClick, running;
        string logFile;
        SetupRunner runner;
        SerialPopup serialPopup;
        ProgressForm prog;

        Panel content, wgPanel;
        ListView lvProg, lvSet;
        ListViewGroup customGroup;
        Label lblSummary;
        CheckBox chkRecurse, chkRestore, chkReboot, chkClean;
        TextBox tbPc, tbWgCustom;
        CheckedListBox clbWg;
        Button btnWinget, btnInstall, btnSave;
        ToolTip tip;

        static readonly Color Accent = Color.FromArgb(0, 84, 166);
        static readonly Color Gray = Color.FromArgb(100, 100, 100);

        public MainForm(string appDir, bool autoStart, bool justUpdated = false)
        {
            this.appDir = appDir;
            this.autoStart = autoStart;
            this.justUpdated = justUpdated;
            settingsPath = Path.Combine(appDir, "settings.json");
            defaultDir = Path.Combine(appDir, "install_file");
            logDir = Path.Combine(appDir, "logs");

            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Font = new Font("Malgun Gothic", 9f);
            AutoScaleMode = AutoScaleMode.None;
            Text = AppName + "  " + AppInfo.VersionText;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(S(1060), S(780));
            MinimumSize = new Size(S(900), S(620));
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            LoadSettings();
        }

        // ================================================================== 공용 도우미
        int S(double n) { return (int)Math.Round(n * scale); }
        Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
        Padding Pad(int a) { return Pad(a, a, a, a); }

        Button Btn(string text, EventHandler onClick, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Pad(8, 3, 8, 3), Margin = Pad(3), UseVisualStyleBackColor = true,
            };
            if (primary)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Color.FromArgb(0, 103, 192);
                b.ForeColor = Color.White;
                b.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
                b.Padding = Pad(18, 6, 18, 6);
                b.UseVisualStyleBackColor = false;
            }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        Label Lbl(string text, Color? color = null)
        {
            var l = new Label { Text = text, AutoSize = true, Margin = Pad(3, 8, 3, 3) };
            if (color.HasValue) l.ForeColor = color.Value;
            return l;
        }

        Button HeaderButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = Color.FromArgb(0, 70, 140), Padding = Pad(6, 2, 6, 2), Margin = Pad(4, 4, 4, 4),
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 140, 200);
            b.Click += onClick;
            return b;
        }

        static Image LoadResourceImage(string name)
        {
            try
            {
                var s = typeof(MainForm).Assembly.GetManifestResourceStream(name);
                return s != null ? Image.FromStream(s) : null;
            }
            catch { return null; }
        }

        Label SectionTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Accent, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = Pad(0, 6, 0, 2) };
        }

        static FlowLayoutPanel Flow(FlowDirection dir = FlowDirection.LeftToRight, bool wrap = true)
        {
            return new FlowLayoutPanel { FlowDirection = dir, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = wrap, Margin = new Padding(0) };
        }

        Control Separator()
        {
            return new Label { AutoSize = false, Width = 1, Height = S(24), BackColor = Color.FromArgb(210, 210, 210), Margin = Pad(6, 6, 6, 3) };
        }

        void Info(string msg, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            MessageBox.Show(this, msg, AppName, MessageBoxButtons.OK, icon);
        }

        // ================================================================== 화면 구성
        void BuildUi()
        {
            tip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };

            content = new Panel { Dock = DockStyle.Fill, Padding = Pad(14, 6, 14, 6) };
            var settingsSection = BuildSettingsSection();
            var programSection = BuildProgramSection();
            content.Controls.Add(settingsSection);   // Fill
            content.Controls.Add(programSection);    // Top
            lockWhileRunning.Add(content);

            wgPanel = BuildWingetPanel();
            wgPanel.Visible = false;
            lockWhileRunning.Add(wgPanel);

            // 상단 헤더 (왼쪽: 제목 / 오른쪽: 버전 · 업데이트 · 커피)
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = S(64), BackColor = Accent, ColumnCount = 2, Padding = Pad(0, 0, 12, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var titles = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            int textX = S(16);
            Image logo = LoadResourceImage("logo.png");
            if (logo != null)
            {
                titles.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(S(50), S(50)), Location = new Point(S(12), S(7)), BackColor = Color.Transparent });
                textX = S(70);
            }
            titles.Controls.Add(new Label
            {
                Text = AppName, AutoSize = true, ForeColor = Color.White,
                Font = new Font("Malgun Gothic", 15f, FontStyle.Bold), Location = new Point(textX, S(8))
            });
            titles.Controls.Add(new Label
            {
                Text = "설치할 프로그램과 윈도우 설정을 고른 뒤 아래 버튼을 누르면 순서대로 자동 진행됩니다.",
                AutoSize = true, ForeColor = Color.FromArgb(210, 228, 248), Location = new Point(textX + S(2), S(40))
            });
            var headRight = Flow(FlowDirection.LeftToRight, false);
            headRight.Anchor = AnchorStyles.Right;
            var lblVer = new Label { Text = AppInfo.VersionText, AutoSize = true, ForeColor = Color.FromArgb(190, 212, 240), Margin = Pad(3, 9, 8, 3) };
            var btnUpdate = HeaderButton("⟳ 업데이트 확인", delegate { CheckForUpdate(true); });
            tip.SetToolTip(btnUpdate, AppInfo.UpdateConfigured ? "GitHub 에서 새 버전이 있는지 확인합니다." : "업데이트 저장소가 아직 설정되지 않았습니다.");
            var btnCoffee = HeaderButton("☕ 커피 한 잔", delegate { using (var f = new CoffeeForm(scale, Font)) f.ShowDialog(this); });
            btnCoffee.BackColor = Color.FromArgb(255, 221, 87);
            btnCoffee.ForeColor = Color.FromArgb(60, 40, 20);
            btnCoffee.FlatAppearance.BorderColor = Color.FromArgb(255, 221, 87);
            tip.SetToolTip(btnCoffee, "이 프로그램이 도움이 되셨다면 커피 한 잔으로 응원해 주세요!");
            headRight.Controls.AddRange(new Control[] { lblVer, btnUpdate, btnCoffee });
            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(headRight, 1, 0);

            // 하단 버튼 영역
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2,
                Padding = Pad(12, 6, 12, 8), BackColor = Color.FromArgb(243, 243, 243)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var opts = Flow();
            opts.Anchor = AnchorStyles.Left;
            chkRestore = new CheckBox { Text = "시작 전 시스템 복원 지점 만들기", AutoSize = true, Checked = true, Margin = Pad(3, 6, 16, 3) };
            chkReboot = new CheckBox { Text = "완료 후 자동으로 다시 시작", AutoSize = true, Margin = Pad(3, 6, 16, 3) };
            chkClean = new CheckBox { Text = "추가 앱 설치 후 내려받은 설치 파일 삭제", AutoSize = true, Checked = true, Margin = Pad(3, 6, 16, 3) };
            tip.SetToolTip(chkClean, "③ 추가 앱(winget)이 인터넷에서 내려받은 설치 파일만 삭제합니다.\n① 설치 프로그램 목록의 파일(install_file 등)은 삭제하지 않습니다.");
            opts.Controls.AddRange(new Control[] { chkRestore, chkReboot, chkClean });
            lockWhileRunning.Add(opts);
            var actions = Flow(FlowDirection.LeftToRight, false);
            actions.Anchor = AnchorStyles.Right;
            btnSave = Btn("설정 저장", delegate { if (SaveSettings(true)) Info("설정을 저장했습니다.\n" + settingsPath); });
            btnInstall = Btn("▶  설정 적용", delegate { StartJob(false); }, true);
            actions.Controls.AddRange(new Control[] { btnSave, btnInstall });
            lockWhileRunning.Add(btnSave);
            lockWhileRunning.Add(btnInstall);
            bottom.Controls.Add(opts, 0, 0);
            bottom.Controls.Add(actions, 1, 0);

            // Dock 순서: 먼저 추가한 컨트롤이 마지막에 배치됨 (Fill 은 남은 공간)
            Controls.Add(content);
            Controls.Add(wgPanel);
            Controls.Add(header);
            Controls.Add(bottom);
        }

        // ---------------------------------------------------------------- ① 설치 프로그램
        Control BuildProgramSection()
        {
            var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // 제목 + 도구 모음
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // 줄바꿈을 끄지 않으면 AutoSize 표에서 버튼이 세로로 쌓인 높이로 계산되어 빈 공간이 생긴다
            var left = Flow(FlowDirection.LeftToRight, false);
            chkRecurse =new CheckBox { Text = "하위 폴더 포함", AutoSize = true, Margin = Pad(4, 8, 4, 3) };
            left.Controls.AddRange(new Control[]
            {
                Btn("＋ 파일 추가", delegate { AddFilesDialog(); }),
                Btn("＋ 폴더 추가", delegate { AddFolderDialog(); }),
                chkRecurse,
                Separator(),
                Btn("옵션 편집", delegate { EditSelectedProgram(); }),
                Btn("시리얼 파일", delegate { OpenSelectedSerial(); }),
                Btn("▲", delegate { MoveSelected(-1); }),
                Btn("▼", delegate { MoveSelected(1); }),
                Btn("✕ 제거", delegate { RemoveSelected(); }),
                Btn("⟳", delegate { ReloadPrograms(); }),
            });
            tip.SetToolTip(left.Controls[left.Controls.Count - 1], "새로 고침 (install_file 폴더 다시 읽기, 시리얼 파일 다시 확인)");
            tip.SetToolTip(left.Controls[left.Controls.Count - 4], "선택한 항목을 위로 (설치 순서)");
            tip.SetToolTip(left.Controls[left.Controls.Count - 3], "선택한 항목을 아래로 (설치 순서)");
            btnWinget = Btn("＋ 추가 앱 선택 ▶", delegate { ToggleWingetPanel(); });
            btnWinget.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            tip.SetToolTip(btnWinget, "크롬, 카카오톡, 반디집 등 자주 쓰는 앱을 인터넷에서 받아 설치할 수 있게 오른쪽에 목록을 엽니다.");
            bar.Controls.Add(left, 0, 0);
            bar.Controls.Add(btnWinget, 1, 0);

            lvProg = new ListView
            {
                View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = true,
                HideSelection = false, MultiSelect = true, AllowDrop = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Height = S(130), Margin = Pad(3, 2, 3, 2),
                SmallImageList = new ImageList { ImageSize = new Size(1, S(22)) },
            };
            foreach (var c in new[] { Tuple.Create("#", 34), Tuple.Create("파일명", 210), Tuple.Create("유형", 130), Tuple.Create("설치 방식", 90),
                                      Tuple.Create("설치 인수", 210), Tuple.Create("시리얼", 150), Tuple.Create("위치", 200) })
                lvProg.Columns.Add(c.Item1, S(c.Item2));
            lvProg.ClientSizeChanged += delegate { DeferFill(lvProg, S(120)); };
            lvProg.ItemChecked += (s, e) =>
            {
                if (rebuilding) return;
                ((ProgramItem)e.Item.Tag).Enabled = e.Item.Checked;
                UpdateSummary();
            };
            // 더블클릭(옵션 편집) 시 체크 상태가 같이 바뀌는 ListView 기본 동작 방지
            lvProg.MouseDown += (s, e) => { progDoubleClick = e.Clicks > 1; };
            lvProg.MouseUp += (s, e) => { progDoubleClick = false; };
            lvProg.ItemCheck += (s, e) => { if (progDoubleClick) e.NewValue = e.CurrentValue; };
            lvProg.DoubleClick += delegate { EditSelectedProgram(); };
            lvProg.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !running) RemoveSelected(); };
            lvProg.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop) && !running) e.Effect = DragDropEffects.Copy; };
            lvProg.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (string f in files)
                {
                    if (Directory.Exists(f)) AddFolder(f, chkRecurse.Checked);
                    else AddFile(f);
                }
                RefreshList();
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("옵션 편집", null, delegate { EditSelectedProgram(); });
            menu.Items.Add("시리얼 파일 열기/만들기", null, delegate { OpenSelectedSerial(); });
            menu.Items.Add("파일 위치 열기", null, delegate
            {
                if (lvProg.SelectedItems.Count > 0)
                    try { Process.Start("explorer.exe", "/select,\"" + ((ProgramItem)lvProg.SelectedItems[0].Tag).Path + "\""); } catch { }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("전체 선택", null, delegate { foreach (var p in programs) p.Enabled = true; RefreshList(); });
            menu.Items.Add("전체 해제", null, delegate { foreach (var p in programs) p.Enabled = false; RefreshList(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("목록에서 제거", null, delegate { RemoveSelected(); });
            lvProg.ContextMenuStrip = menu;

            var foot = Flow();
            foot.Margin = Pad(0, 0, 0, 4);
            lblSummary = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = Pad(3, 3, 12, 3) };
            var hint = new Label
            {
                AutoSize = true, ForeColor = Gray, Margin = Pad(3, 3, 3, 3),
                Text = "시리얼: 설치 파일과 같은 이름의 .txt (예: Hangul.exe → Hangul.txt)  ·  파일/폴더 끌어다 놓기  ·  위에서부터 순서대로 설치  ·  더블클릭: 옵션 편집"
            };
            foot.Controls.AddRange(new Control[] { lblSummary, hint });

            tl.Controls.Add(SectionTitle("① 설치 프로그램"), 0, 0);
            tl.Controls.Add(bar, 0, 1);
            tl.Controls.Add(lvProg, 0, 2);
            tl.Controls.Add(foot, 0, 3);
            return tl;
        }

        // ---------------------------------------------------------------- ② 윈도우 초기 설정
        Control BuildSettingsSection()
        {
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0), Padding = Pad(0, 6, 0, 0) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = Flow();
            bar.Dock = DockStyle.Fill;
            tbPc = new TextBox { Width = S(150), MaxLength = 15, Margin = Pad(3, 5, 3, 3) };
            tip.SetToolTip(tbPc, "비워 두면 변경하지 않습니다. 영문/숫자/하이픈, 15자 이내. 재시작 후 적용.");
            bar.Controls.AddRange(new Control[]
            {
                Btn("권장값으로", delegate { foreach (var t in Tweaks.All) tweakItems[t.Id].Checked = t.Default; }),
                Btn("모두 선택", delegate { foreach (ListViewItem it in lvSet.Items) it.Checked = true; }),
                Btn("모두 해제", delegate { foreach (ListViewItem it in lvSet.Items) it.Checked = false; }),
                Separator(),
                Btn("＋ 레지스트리 직접 추가", delegate { AddRegEntry(); }),
                Btn("편집", delegate { EditSelectedRegEntry(); }),
                Btn("삭제", delegate { DeleteSelectedRegEntries(); }),
                Separator(),
                Lbl("PC 이름 변경"),
                tbPc,
                Lbl("(현재: " + Environment.MachineName + ")", Color.Gray),
            });

            lvSet = new ListView
            {
                View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false,
                MultiSelect = true, ShowItemToolTips = true, Dock = DockStyle.Fill, Margin = Pad(3, 2, 3, 2),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                SmallImageList = new ImageList { ImageSize = new Size(1, S(22)) },
            };
            lvSet.Columns.Add("설정 항목", S(340));
            lvSet.Columns.Add("설명", S(600));
            lvSet.ClientSizeChanged += delegate { DeferFill(lvSet, S(200)); };

            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var t in Tweaks.All)
            {
                ListViewGroup g;
                if (!groups.TryGetValue(t.Group, out g))
                {
                    g = new ListViewGroup(t.Group, t.Group);
                    lvSet.Groups.Add(g);
                    groups[t.Group] = g;
                }
                var it = new ListViewItem(new[] { t.Name, t.Desc }, g) { Checked = t.Default, Tag = t, ToolTipText = t.Desc };
                lvSet.Items.Add(it);
                tweakItems[t.Id] = it;
            }
            customGroup = new ListViewGroup("custom", "사용자 레지스트리 (직접 추가한 항목)");
            lvSet.Groups.Add(customGroup);

            lvSet.ItemChecked += (s, e) =>
            {
                var r = e.Item.Tag as RegEntry;
                if (r != null) r.Enabled = e.Item.Checked;
            };
            lvSet.MouseDown += (s, e) => { setDoubleClick = e.Clicks > 1; };
            lvSet.MouseUp += (s, e) => { setDoubleClick = false; };
            lvSet.ItemCheck += (s, e) => { if (setDoubleClick && lvSet.Items[e.Index].Tag is RegEntry) e.NewValue = e.CurrentValue; };
            lvSet.DoubleClick += delegate { if (lvSet.SelectedItems.Count == 1 && lvSet.SelectedItems[0].Tag is RegEntry) EditSelectedRegEntry(); };
            lvSet.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !running) DeleteSelectedRegEntries(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("레지스트리 직접 추가", null, delegate { AddRegEntry(); });
            menu.Items.Add("편집", null, delegate { EditSelectedRegEntry(); });
            menu.Items.Add("삭제", null, delegate { DeleteSelectedRegEntries(); });
            lvSet.ContextMenuStrip = menu;

            tl.Controls.Add(SectionTitle("② 윈도우 초기 설정"), 0, 0);
            tl.Controls.Add(bar, 0, 1);
            tl.Controls.Add(lvSet, 0, 2);
            return tl;
        }

        /// <summary>마지막 열을 남은 폭에 맞춰 가로 스크롤이 생기지 않게 한다.</summary>
        static void FillLastColumn(ListView lv, int min)
        {
            if (lv.Columns.Count == 0) return;
            int used = 0;
            for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            int w = Math.Max(min, lv.ClientSize.Width - used - 4);
            if (lv.Columns[lv.Columns.Count - 1].Width != w) lv.Columns[lv.Columns.Count - 1].Width = w;
        }

        /// <summary>레이아웃(세로 스크롤 표시 등)이 끝난 뒤 열 폭을 맞춘다.</summary>
        void DeferFill(ListView lv, int min)
        {
            if (!lv.IsHandleCreated) return;
            lv.BeginInvoke(new Action(() => FillLastColumn(lv, min)));
        }

        // ---------------------------------------------------------------- ③ 추가 앱 (오른쪽 패널)
        Panel BuildWingetPanel()
        {
            var p = new Panel { Dock = DockStyle.Right, Width = S(320), Padding = Pad(14, 8, 12, 6), BackColor = Color.FromArgb(246, 248, 251) };
            p.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(215, 220, 228)), 0, 0, 0, p.Height);

            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, S(70)));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var close = new Button { Text = "✕", FlatStyle = FlatStyle.Flat, AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Gray };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { ToggleWingetPanel(); };
            head.Controls.Add(SectionTitle("③ 추가 앱 (winget)"), 0, 0);
            head.Controls.Add(close, 1, 0);

            var desc = new Label
            {
                AutoSize = true, ForeColor = Gray, Margin = Pad(3, 2, 3, 6), MaximumSize = new Size(S(285), 0),
                Text = "체크한 앱은 인터넷에서 최신 버전을 받아 ① 설치 프로그램 다음에 설치됩니다."
            };

            clbWg = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            foreach (var w in Installers.WingetCatalog) clbWg.Items.Add(w.Name);
            clbWg.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke(new Action(UpdateActionButton)); };

            var lblCustom = new Label { AutoSize = true, Text = "목록에 없는 앱 ID (쉼표/줄바꿈 구분)", Margin = Pad(3, 8, 3, 2) };
            tip.SetToolTip(lblCustom, "ID 찾기: 명령 프롬프트에서  winget search 앱이름");
            tbWgCustom = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            tbWgCustom.TextChanged += delegate { UpdateActionButton(); };

            tl.Controls.Add(head, 0, 0);
            tl.Controls.Add(desc, 0, 1);
            tl.Controls.Add(clbWg, 0, 2);
            tl.Controls.Add(lblCustom, 0, 3);
            tl.Controls.Add(tbWgCustom, 0, 4);
            p.Controls.Add(tl);
            return p;
        }

        void ToggleWingetPanel()
        {
            bool show = !wgPanel.Visible;
            if (WindowState == FormWindowState.Normal)
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                if (show)
                {
                    widthBeforeWinget = Width;
                    int w = Math.Min(wa.Width, Width + wgPanel.Width);
                    if (Left + w > wa.Right) Left = Math.Max(wa.Left, wa.Right - w);
                    Width = w;
                }
                else if (widthBeforeWinget > 0) Width = widthBeforeWinget;
            }
            wgPanel.Visible = show;
            UpdateActionButton();
            DeferFill(lvProg, S(120));
            DeferFill(lvSet, S(200));
        }

        List<string> WingetIds()
        {
            var ids = new List<string>();
            for (int i = 0; i < clbWg.Items.Count; i++)
                if (clbWg.GetItemChecked(i)) ids.Add(Installers.WingetCatalog[i].Id);
            foreach (string x in Regex.Split(tbWgCustom.Text, @"[\s,;]+"))
                if (x.Length > 0 && !ids.Contains(x)) ids.Add(x);
            return ids;
        }

        /// <summary>설치할 것이 있으면 '설정 및 설치', 없으면 '설정 적용'</summary>
        void UpdateActionButton()
        {
            int wg = WingetIds().Count;
            bool install = programs.Any(p => p.Enabled && File.Exists(p.Path)) || wg > 0;
            if (!running) btnInstall.Text = install ? "▶  설정 및 설치" : "▶  설정 적용";
            btnWinget.Text = wgPanel.Visible ? "추가 앱 닫기 ◀"
                           : wg > 0 ? "＋ 추가 앱 선택 (" + wg + ") ▶" : "＋ 추가 앱 선택 ▶";
        }

        // ================================================================== 설치 목록
        ProgramItem NewItem(string path, bool enabled = true, string args = null, string mode = "auto")
        {
            InstallerInfo info = File.Exists(path) ? Installers.Detect(path) : null;
            var p = new ProgramItem
            {
                Path = path, Enabled = enabled, Mode = string.IsNullOrEmpty(mode) ? "auto" : mode,
                Kind = info != null ? info.Kind : "?",
                Label = info != null ? info.Label : "(파일 없음)",
                DefaultArgs = info != null ? info.Args : "",
            };
            p.Args = args ?? p.DefaultArgs;
            return p;
        }

        bool AddFile(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch { return false; }
            if (!Installers.IsSupported(full)) return false;
            if (programs.Any(p => string.Equals(p.Path, full, StringComparison.OrdinalIgnoreCase))) return false;
            programs.Add(NewItem(full));
            return true;
        }

        int AddFolder(string folder, bool recurse)
        {
            int n = 0;
            try
            {
                foreach (string f in Directory.GetFiles(folder, "*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                                              .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (AddFile(f)) n++;
            }
            catch (Exception ex) { Info("폴더를 읽는 중 오류가 발생했습니다.\n" + ex.Message, MessageBoxIcon.Warning); }
            return n;
        }

        void AddFilesDialog()
        {
            using (var d = new OpenFileDialog
            {
                Title = "설치 파일 선택",
                Multiselect = true,
                Filter = "설치 파일 (*.exe;*.msi;*.msix;*.msixbundle;*.appx;*.appxbundle;*.reg;*.bat;*.cmd;*.ps1)|*.exe;*.msi;*.msix;*.msixbundle;*.appx;*.appxbundle;*.reg;*.bat;*.cmd;*.ps1|모든 파일 (*.*)|*.*",
                InitialDirectory = Directory.Exists(defaultDir) ? defaultDir : appDir,
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in d.FileNames) AddFile(f);
                RefreshList();
            }
        }

        void AddFolderDialog()
        {
            using (var d = new FolderBrowserDialog
            {
                Description = "설치 파일이 들어 있는 폴더를 선택하세요. 폴더 안의 설치 파일이 모두 목록에 추가됩니다.",
                SelectedPath = Directory.Exists(defaultDir) ? defaultDir : appDir,
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                int n = AddFolder(d.SelectedPath, chkRecurse.Checked);
                RefreshList();
                if (n == 0) Info("추가할 새 설치 파일이 없습니다.");
            }
        }

        void ReloadPrograms()
        {
            Installers.ClearCache();
            foreach (var p in programs) p.Kind = "?";
            if (Directory.Exists(defaultDir)) AddFolder(defaultDir, false);
            RefreshList();
        }

        void RefreshList()
        {
            var selected = new HashSet<ProgramItem>(lvProg.SelectedItems.Cast<ListViewItem>().Select(i => (ProgramItem)i.Tag));
            rebuilding = true;
            lvProg.BeginUpdate();
            lvProg.Items.Clear();
            int idx = 0, ser = 0;
            foreach (var p in programs)
            {
                idx++;
                bool exists = File.Exists(p.Path);
                if (exists && p.Kind == "?")
                {
                    var info = Installers.Detect(p.Path);
                    p.Kind = info.Kind; p.Label = info.Label; p.DefaultArgs = info.Args;
                }
                SerialInfo serial = exists ? Installers.ReadSerial(p.Path) : null;
                InstallPlan plan = Installers.Resolve(p, serial);
                bool hasSerial = serial != null && !string.IsNullOrEmpty(serial.Serial);
                if (hasSerial) ser++;

                string modeText = plan.Mode == "silent" ? "무인" : "대화형";
                if (p.Mode == "auto") modeText += " (자동)";
                string argText = plan.DisplayArgs;
                if (p.Kind == "APPX" || p.Kind == "REG") argText = "-";
                else if (plan.Mode != "silent" && p.Kind != "BAT" && p.Kind != "PS1") argText = "(대화형: 인수 없이 실행)";
                else if (plan.ArgsFromTxt) argText += "   ← txt";
                string serialText = "-";
                if (serial != null)
                {
                    if (!hasSerial) serialText = "txt 있음 (시리얼 비어 있음)";
                    else if (plan.SerialInArgs) serialText = "감지 → 인수로 자동 입력";
                    else serialText = "감지 → 클립보드 복사";
                }

                var it = new ListViewItem(idx.ToString());
                it.SubItems.Add(Path.GetFileName(p.Path));
                it.SubItems.Add(exists ? p.Label : "(파일 없음)");
                it.SubItems.Add(modeText);
                it.SubItems.Add(argText);
                it.SubItems.Add(serialText);
                it.SubItems.Add(Path.GetDirectoryName(p.Path));
                it.Checked = p.Enabled;
                it.Tag = p;
                if (!exists) it.ForeColor = Color.Gray;
                else if (hasSerial) it.ForeColor = Color.FromArgb(0, 90, 170);
                lvProg.Items.Add(it);
                if (selected.Contains(p)) it.Selected = true;
            }
            lvProg.EndUpdate();
            rebuilding = false;
            serialCount = ser;
            UpdateSummary();
        }

        void UpdateSummary()
        {
            if (programs.Count == 0)
                lblSummary.Text = "설치할 파일이 없습니다 - [＋ 파일 추가] / [＋ 폴더 추가] 또는 목록으로 끌어다 놓으세요.";
            else
                lblSummary.Text = string.Format("전체 {0}개  ·  설치 선택 {1}개  ·  시리얼 감지 {2}개",
                    programs.Count, programs.Count(p => p.Enabled), serialCount);
            UpdateActionButton();
        }

        void MoveSelected(int delta)
        {
            if (lvProg.SelectedItems.Count != 1) return;
            var p = (ProgramItem)lvProg.SelectedItems[0].Tag;
            int i = programs.IndexOf(p), n = i + delta;
            if (n < 0 || n >= programs.Count) return;
            programs.RemoveAt(i);
            programs.Insert(n, p);
            RefreshList();
            lvProg.Items[n].EnsureVisible();
        }

        void RemoveSelected()
        {
            foreach (ListViewItem it in lvProg.SelectedItems) programs.Remove((ProgramItem)it.Tag);
            RefreshList();
        }

        void EditSelectedProgram()
        {
            if (running) return;
            if (lvProg.SelectedItems.Count == 0) { Info("목록에서 프로그램을 먼저 선택하세요."); return; }
            var p = (ProgramItem)lvProg.SelectedItems[0].Tag;
            using (var d = new ProgramEditor(p, scale, Font, OpenSerialFile))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    p.Args = d.ArgsValue;
                    p.Mode = d.ModeValue;
                }
            }
            RefreshList();
        }

        void OpenSelectedSerial()
        {
            if (lvProg.SelectedItems.Count == 0) { Info("목록에서 프로그램을 먼저 선택하세요."); return; }
            OpenSerialFile(((ProgramItem)lvProg.SelectedItems[0].Tag).Path);
        }

        void OpenSerialFile(string installerPath)
        {
            SerialInfo s = Installers.ReadSerial(installerPath);
            string file = s != null ? s.File : Installers.SerialFilePath(installerPath);
            if (s == null)
            {
                try { File.WriteAllText(file, Installers.SerialTemplate(installerPath), new UTF8Encoding(true)); }
                catch (Exception ex) { Info("시리얼 파일을 만들 수 없습니다.\n" + ex.Message, MessageBoxIcon.Error); return; }
            }
            try { Process.Start("notepad.exe", "\"" + file + "\""); } catch { }
        }

        // ================================================================== 사용자 레지스트리
        void RefreshCustomRegs()
        {
            foreach (var it in lvSet.Items.Cast<ListViewItem>().Where(i => i.Tag is RegEntry).ToList()) lvSet.Items.Remove(it);
            foreach (var r in customRegs)
            {
                string name = string.IsNullOrEmpty(r.Name) ? "(기본값)" : r.Name;
                string col0 = r.DeleteValue ? name + "  → 값 삭제" : name + " = " + r.Value + "  (" + r.Type + ")";
                var it = new ListViewItem(new[] { col0, r.Key }, customGroup) { Checked = r.Enabled, Tag = r, ToolTipText = r.Display() };
                it.ForeColor = Color.FromArgb(0, 90, 170);
                lvSet.Items.Add(it);
            }
        }

        void AddRegEntry()
        {
            using (var d = new RegEntryEditor(null, scale, Font))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                customRegs.Add(d.Result);
            }
            RefreshCustomRegs();
            var last = lvSet.Items.Cast<ListViewItem>().LastOrDefault(i => i.Tag is RegEntry);
            if (last != null) last.EnsureVisible();
        }

        void EditSelectedRegEntry()
        {
            var sel = lvSet.SelectedItems.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is RegEntry);
            if (sel == null) { Info("목록 아래쪽 '사용자 레지스트리' 에서 편집할 항목을 선택하세요.\n(기본 설정 항목은 체크만 할 수 있습니다)"); return; }
            var r = (RegEntry)sel.Tag;
            using (var d = new RegEntryEditor(r, scale, Font))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                customRegs[customRegs.IndexOf(r)] = d.Result;
            }
            RefreshCustomRegs();
        }

        void DeleteSelectedRegEntries()
        {
            var sel = lvSet.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag is RegEntry).Select(i => (RegEntry)i.Tag).ToList();
            if (sel.Count == 0) { Info("목록 아래쪽 '사용자 레지스트리' 에서 삭제할 항목을 선택하세요.\n(기본 설정 항목은 체크를 해제하면 적용되지 않습니다)"); return; }
            foreach (var r in sel) customRegs.Remove(r);
            RefreshCustomRegs();
        }

        // ================================================================== 설정 저장/불러오기
        string ToStored(string path)
        {
            string root = appDir.TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
        }

        string FromStored(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(Path.Combine(appDir, path));
        }

        bool SaveSettings(bool showError)
        {
            var s = new SettingsData
            {
                Programs = programs.Select(p => new StoredProgram { Path = ToStored(p.Path), Enabled = p.Enabled, Args = p.Args ?? "", Mode = p.Mode }).ToList(),
                Tweaks = Tweaks.All.ToDictionary(t => t.Id, t => tweakItems[t.Id].Checked),
                PcName = tbPc.Text.Trim(),
                Winget = Enumerable.Range(0, clbWg.Items.Count).Where(i => clbWg.GetItemChecked(i)).Select(i => Installers.WingetCatalog[i].Id).ToList(),
                WingetCustom = tbWgCustom.Text,
                RestorePoint = chkRestore.Checked,
                RebootAfter = chkReboot.Checked,
                Recurse = chkRecurse.Checked,
                CleanDownloads = chkClean.Checked,
                CheckUpdates = checkUpdates,
                CustomRegs = customRegs.ToList(),
            };
            try { s.Save(settingsPath); return true; }
            catch (Exception ex)
            {
                if (showError) Info("설정을 저장하지 못했습니다.\n" + ex.Message, MessageBoxIcon.Error);
                return false;
            }
        }

        void LoadSettings()
        {
            SettingsData s = SettingsData.Load(settingsPath);
            if (s != null)
            {
                foreach (var sp in s.Programs)
                {
                    if (sp == null || string.IsNullOrEmpty(sp.Path)) continue;
                    string full = FromStored(sp.Path);
                    if (programs.Any(p => string.Equals(p.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
                    programs.Add(NewItem(full, sp.Enabled, sp.Args, sp.Mode));
                }
                foreach (var kv in s.Tweaks)
                {
                    ListViewItem it;
                    if (tweakItems.TryGetValue(kv.Key, out it)) it.Checked = kv.Value;
                }
                customRegs.AddRange(s.CustomRegs.Where(r => r != null && Reg.Normalize(r.Key) != null));
                tbPc.Text = s.PcName ?? "";
                for (int i = 0; i < Installers.WingetCatalog.Length; i++)
                    clbWg.SetItemChecked(i, s.Winget.Contains(Installers.WingetCatalog[i].Id));
                tbWgCustom.Text = s.WingetCustom ?? "";
                chkRestore.Checked = s.RestorePoint;
                chkReboot.Checked = s.RebootAfter;
                chkRecurse.Checked = s.Recurse;
                chkClean.Checked = s.CleanDownloads;
                checkUpdates = s.CheckUpdates;
            }
            // 기본 폴더(install_file)의 새 파일 자동 추가
            try { Directory.CreateDirectory(defaultDir); } catch { }
            if (Directory.Exists(defaultDir)) AddFolder(defaultDir, false);
            RefreshCustomRegs();
            RefreshList();
        }

        // ================================================================== 작업 실행
        void SetRunning(bool on)
        {
            running = on;
            foreach (var c in lockWhileRunning) c.Enabled = !on;
            if (on) btnInstall.Text = "진행 중...";
            else UpdateActionButton();
        }

        public void StartJob(bool noConfirm)
        {
            if (running) return;
            string pc = tbPc.Text.Trim();
            if (pc.Length > 0 && (pc.Length > 15 || !Regex.IsMatch(pc, "^[A-Za-z0-9-]+$") || Regex.IsMatch(pc, "^[0-9]+$")))
            {
                tbPc.Focus();
                Info("PC 이름은 영문/숫자/하이픈(-)으로 15자 이내여야 하며 숫자만으로 만들 수 없습니다.", MessageBoxIcon.Warning);
                return;
            }
            SaveSettings(false);

            var job = new SetupJob
            {
                Programs = programs.Where(p => p.Enabled).Select(p => new ProgramItem
                {
                    Path = p.Path, Enabled = true, Args = p.Args, Mode = p.Mode, Kind = p.Kind, Label = p.Label, DefaultArgs = p.DefaultArgs
                }).ToList(),
                Tweaks = Tweaks.All.Where(t => tweakItems[t.Id].Checked).ToList(),
                PcName = pc,
                Winget = WingetIds(),
                RestorePoint = chkRestore.Checked,
                RebootAfter = chkReboot.Checked,
                CleanDownloads = chkClean.Checked,
            };
            foreach (var r in customRegs.Where(x => x.Enabled))
            {
                var entry = r;
                job.Tweaks.Add(new Tweak { Id = "custom", Group = "사용자 레지스트리", Name = "레지스트리: " + entry.Display(), Apply = log => Reg.Apply(entry) });
            }

            bool install = job.Programs.Count > 0 || job.Winget.Count > 0;
            string title = install ? "설정 및 설치" : "설정 적용";
            if (!install && job.Tweaks.Count == 0 && pc.Length == 0)
            {
                Info("실행할 작업이 없습니다. 설치할 프로그램이나 윈도우 설정을 선택하세요.");
                return;
            }

            if (!noConfirm)
            {
                int missing = job.Programs.Count(p => !File.Exists(p.Path));
                var sb = new StringBuilder("[" + title + "] 다음 작업을 실행합니다.\n\n");
                if (job.Programs.Count > 0) sb.AppendFormat("· 설치 프로그램: {0}개\n", job.Programs.Count);
                if (job.Winget.Count > 0) sb.AppendFormat("· 추가 앱 (winget): {0}개\n", job.Winget.Count);
                sb.AppendFormat("· 윈도우 설정: {0}개\n", job.Tweaks.Count);
                if (pc.Length > 0) sb.AppendFormat("· PC 이름 변경: {0}\n", pc);
                if (job.RestorePoint) sb.Append("· 시작 전 시스템 복원 지점 생성\n");
                if (job.RebootAfter) sb.Append("· 완료 후 자동 재시작\n");
                if (missing > 0) sb.AppendFormat("\n※ 찾을 수 없는 파일 {0}개는 건너뜁니다.\n", missing);
                sb.Append("\n계속할까요?");
                if (MessageBox.Show(this, sb.ToString(), AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            try { Directory.CreateDirectory(logDir); logFile = Path.Combine(logDir, "AutoSetup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"); }
            catch { logFile = null; }

            if (prog != null) { try { prog.Dispose(); } catch { } }
            prog = new ProgressForm(scale, Font);
            prog.SkipRequested += delegate { if (runner != null) runner.Skip = true; };
            prog.StopRequested += delegate
            {
                if (runner != null) runner.Cancel = true;
                AddLog(LogLevel.Warn, "중지 요청 - 현재 단계가 끝나면 멈춥니다.");
            };
            prog.OpenLogRequested += delegate
            {
                try { Directory.CreateDirectory(logDir); Process.Start("explorer.exe", "\"" + logDir + "\""); } catch { }
            };
            SetRunning(true);
            prog.Show(this);

            runner = new SetupRunner();
            runner.Log += (lv, msg) => Ui(() => AddLog(lv, msg));
            runner.Progress += (v, max, text) => Ui(() => { if (prog != null) prog.SetProgress(v, max, text); });
            runner.SerialShow += (name, serial, interactive) => Ui(() => ShowSerial(name, serial, interactive));
            runner.SerialClose += () => Ui(CloseSerial);

            var r2 = runner;
            var th = new Thread(() =>
            {
                JobResult res;
                try { res = r2.Run(job); }
                catch (Exception ex) { res = new JobResult(); res.Fails.Add("작업 오류: " + ex.Message); }
                Ui(() => JobDone(res));
            }) { IsBackground = true, Name = "SetupWorker" };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        void Ui(Action a)
        {
            try { if (!IsDisposed) BeginInvoke(a); } catch { }
        }

        void JobDone(JobResult r)
        {
            CloseSerial();
            runner = null;
            AddLog(LogLevel.Step, string.Format("작업 종료 - 성공 {0}개, 실패/확인 필요 {1}개", r.Ok, r.Fails.Count));
            if (logFile != null) AddLog(LogLevel.Info, "로그 파일: " + logFile);
            logFile = null;
            SetRunning(false);
            if (prog != null) prog.SetDone(r.Cancelled ? "중지됨" : (r.Fails.Count > 0 ? "완료 (확인 필요 항목 있음)" : "모든 작업을 마쳤습니다."));

            IWin32Window owner = prog != null && prog.Visible ? (IWin32Window)prog : this;
            var sb = new StringBuilder();
            sb.AppendFormat("작업이 끝났습니다.\n\n성공: {0}개\n실패/확인 필요: {1}개", r.Ok, r.Fails.Count);
            if (r.Fails.Count > 0) sb.Append("\n\n" + string.Join("\n", r.Fails.Take(15).Select(f => "· " + f)));
            if (r.RebootScheduled)
            {
                sb.Append("\n\n60초 후 PC가 다시 시작됩니다.\n재시작을 취소하려면 [취소]를 누르세요.");
                if (MessageBox.Show(owner, sb.ToString(), AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.Cancel)
                {
                    Util.Run(Util.Sys32("shutdown.exe"), "/a");
                    AddLog(LogLevel.Warn, "예약된 재시작을 취소했습니다.");
                }
            }
            else
            {
                if (r.RebootNeeded) sb.Append("\n\n일부 변경 사항은 PC를 다시 시작해야 적용됩니다.");
                MessageBox.Show(owner, sb.ToString(), AppName, MessageBoxButtons.OK, r.Fails.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        void AddLog(LogLevel level, string text)
        {
            string sym; Color color;
            switch (level)
            {
                case LogLevel.Ok: sym = "✔"; color = Color.FromArgb(16, 124, 16); break;
                case LogLevel.Warn: sym = "!"; color = Color.FromArgb(176, 106, 0); break;
                case LogLevel.Err: sym = "✖"; color = Color.FromArgb(196, 43, 28); break;
                case LogLevel.Step: sym = "■"; color = Accent; break;
                default: sym = " "; color = Color.FromArgb(40, 40, 40); break;
            }
            string line = string.Format("[{0:HH:mm:ss}] {1} {2}", DateTime.Now, sym, text);
            if (prog != null && !prog.IsDisposed) prog.AddLine(line, color, level == LogLevel.Step);
            if (logFile != null) { try { File.AppendAllText(logFile, line + "\r\n", Encoding.UTF8); } catch { } }
        }

        void ShowSerial(string name, string serial, bool interactive)
        {
            CloseSerial();
            try { Clipboard.SetDataObject(serial, true, 20, 100); } catch { }
            serialPopup = new SerialPopup(name, serial, interactive, scale, Font);
            serialPopup.Show();
        }

        void CloseSerial()
        {
            if (serialPopup == null) return;
            try { serialPopup.Close(); serialPopup.Dispose(); } catch { }
            serialPopup = null;
        }

        // ================================================================== 폼 이벤트
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitProgramList();
        }

        /// <summary>설치 프로그램 목록 높이를 정확히 4줄로 맞춘다 (넘치면 스크롤).</summary>
        void FitProgramList()
        {
            FillLastColumn(lvProg, S(120));   // 가로 스크롤을 먼저 없앤 뒤 높이를 잰다
            bool temp = lvProg.Items.Count == 0;
            rebuilding = true;
            if (temp) lvProg.Items.Add(new ListViewItem("0"));
            Rectangle r = lvProg.GetItemRect(0);
            if (temp) lvProg.Items.Clear();
            rebuilding = false;
            int frame = lvProg.Height - lvProg.ClientSize.Height;
            lvProg.Height = r.Top + r.Height * VisibleProgramRows + frame + 1;
            FillLastColumn(lvProg, S(120));
            FillLastColumn(lvSet, S(200));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (justUpdated) Info(AppInfo.VersionText + " 으로 업데이트되었습니다.");
            if (autoStart) { StartJob(true); return; }
            if (checkUpdates && AppInfo.UpdateConfigured) CheckForUpdate(false);
        }

        // ================================================================== 자동 업데이트 (GitHub Releases)
        bool updateChecking;

        /// <summary>manual=false 이면 시작 시 조용히 확인 (새 버전이 있을 때만 알림)</summary>
        void CheckForUpdate(bool manual)
        {
            if (updateChecking) return;
            if (!AppInfo.UpdateConfigured)
            {
                if (manual) Info("업데이트 저장소가 아직 설정되지 않았습니다.\n(src\\AppInfo.cs 의 GitHubOwner / GitHubRepo 를 채운 뒤 다시 빌드하세요)");
                return;
            }
            updateChecking = true;
            var t = new Thread(() =>
            {
                UpdateInfo u = null; string err = null;
                try { u = Updater.Check(); }
                catch (Exception ex) { err = ex.Message; }
                Ui(() =>
                {
                    updateChecking = false;
                    if (err != null) { if (manual) Info("업데이트를 확인하지 못했습니다.\n" + err, MessageBoxIcon.Warning); return; }
                    if (u == null) { if (manual) Info("최신 버전을 사용 중입니다. (" + AppInfo.VersionText + ")"); return; }
                    OfferUpdate(u);
                });
            }) { IsBackground = true };
            t.Start();
        }

        void OfferUpdate(UpdateInfo u)
        {
            if (running) return;
            string notes = (u.Notes ?? "").Trim();
            notes = Regex.Replace(notes, @"SHA-?256[^\r\n]*", "").Trim();
            if (notes.Length > 600) notes = notes.Substring(0, 600) + " ...";
            string msg = "새 버전이 있습니다.\n\n현재: " + AppInfo.VersionText + "\n최신: " + u.Tag +
                         (notes.Length > 0 ? "\n\n" + notes : "") + "\n\n지금 업데이트할까요? (프로그램이 자동으로 다시 시작됩니다)";
            if (MessageBox.Show(this, msg, AppName + " 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            string file;
            using (var d = new UpdateDownloadForm(u, scale, Font))
            {
                d.ShowDialog(this);
                if (d.Error != null || d.DownloadedFile == null)
                {
                    Info("업데이트 파일을 받지 못했습니다.\n" + (d.Error ?? ""), MessageBoxIcon.Warning);
                    return;
                }
                file = d.DownloadedFile;
            }
            try
            {
                SaveSettings(false);
                Updater.ApplyAndRestart(file);
                Close();
            }
            catch (Exception ex)
            {
                Info("업데이트를 적용하지 못했습니다.\n" + ex.Message + "\n\n프로그램 폴더에 쓰기 권한이 있는지 확인하세요.", MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running)
            {
                if (MessageBox.Show(this, "작업이 진행 중입니다. 정말 종료할까요?\n(진행 중인 설치 프로그램은 계속 실행됩니다)", AppName,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (runner != null) { runner.Cancel = true; runner.Skip = true; }
            }
            SaveSettings(false);
            CloseSerial();
            base.OnFormClosing(e);
        }
    }
}
