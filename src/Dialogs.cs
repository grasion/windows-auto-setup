using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoSetup
{
    /// <summary>설치 항목 옵션 편집 창</summary>
    public class ProgramEditor : Form
    {
        readonly float scale;
        readonly ComboBox cbMode;
        readonly TextBox tbArgs;

        public string ArgsValue { get { return tbArgs.Text.Trim(); } }
        public string ModeValue
        {
            get { return cbMode.SelectedIndex == 1 ? "silent" : cbMode.SelectedIndex == 2 ? "interactive" : "auto"; }
        }

        int S(double n) { return (int)Math.Round(n * scale); }
        Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = Pad(3, 6, 3, 3) };
        }

        Button Btn(string text)
        {
            return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Pad(8, 3, 8, 3), Margin = Pad(3, 3, 3, 3), UseVisualStyleBackColor = true };
        }

        public ProgramEditor(ProgramItem item, float scale, Font font, Action<string> openSerial)
        {
            this.scale = scale;
            Text = "설치 옵션 편집";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Pad(12, 12, 12, 12);

            var tl = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblFile = Lbl(Path.GetFileName(item.Path));
            lblFile.Font = new Font(font, FontStyle.Bold);
            tl.Controls.Add(Lbl("파일"), 0, 0); tl.Controls.Add(lblFile, 1, 0);
            tl.Controls.Add(Lbl("유형"), 0, 1); tl.Controls.Add(Lbl(item.Label), 1, 1);

            cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(380) };
            cbMode.Items.AddRange(new object[] { "자동 - 시리얼이 있으면 대화형, 없으면 무인", "무인 설치 (인수 사용, 창 없이)", "대화형 설치 (설치 화면을 직접 진행)" });
            cbMode.SelectedIndex = item.Mode == "silent" ? 1 : item.Mode == "interactive" ? 2 : 0;
            tl.Controls.Add(Lbl("설치 방식"), 0, 2); tl.Controls.Add(cbMode, 1, 2);

            var argRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            tbArgs = new TextBox { Width = S(310), Text = item.Args ?? "", Margin = Pad(3, 4, 3, 3) };
            var btnDef = Btn("기본값");
            string def = item.DefaultArgs ?? "";
            btnDef.Click += delegate { tbArgs.Text = def; };
            argRow.Controls.AddRange(new Control[] { tbArgs, btnDef });
            tl.Controls.Add(Lbl("설치 인수"), 0, 3); tl.Controls.Add(argRow, 1, 3);

            SerialInfo serial = Installers.ReadSerial(item.Path);
            string serialText;
            if (serial == null) serialText = "없음  (같은 폴더에 '" + Path.GetFileNameWithoutExtension(item.Path) + ".txt' 를 만들면 감지)";
            else if (string.IsNullOrEmpty(serial.Serial)) serialText = Path.GetFileName(serial.File) + "  (시리얼이 비어 있음)";
            else serialText = Path.GetFileName(serial.File) + "  →  " + Util.Mask(serial.Serial);
            var serRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
            var btnSer = Btn(serial != null ? "시리얼 파일 열기" : "시리얼 파일 만들기");
            string path = item.Path;
            btnSer.Click += delegate { openSerial(path); };
            serRow.Controls.AddRange(new Control[] { Lbl(serialText), btnSer });
            tl.Controls.Add(Lbl("시리얼"), 0, 4); tl.Controls.Add(serRow, 1, 4);

            var help = Lbl("· 무인 설치 인수는 설치 프로그램 종류에 따라 자동으로 채워집니다. 동작하지 않으면 제작사 안내를 참고해 수정하세요.\n" +
                           "· 인수에 {SERIAL} 을 넣으면 txt 의 시리얼 번호로 바뀌어 무인 설치됩니다. (예: /S /SN={SERIAL})\n" +
                           "· 대화형 설치는 인수 없이 실행되며, 시리얼은 클립보드와 알림 창으로 제공됩니다.\n" +
                           "· txt 파일에 ARGS= 나 MODE= 를 적으면 여기 설정보다 우선합니다.");
            help.ForeColor = Color.FromArgb(100, 100, 100);
            help.MaximumSize = new Size(S(500), 0);
            help.Margin = Pad(3, 10, 3, 6);
            tl.Controls.Add(help, 0, 5); tl.SetColumnSpan(help, 2);

            var btnRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var ok = Btn("확인"); ok.DialogResult = DialogResult.OK;
            var cancel = Btn("취소"); cancel.DialogResult = DialogResult.Cancel;
            btnRow.Controls.AddRange(new Control[] { cancel, ok });
            tl.Controls.Add(btnRow, 0, 6); tl.SetColumnSpan(btnRow, 2);

            Controls.Add(tl);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>사용자 레지스트리 항목 추가/편집 창</summary>
    public class RegEntryEditor : Form
    {
        readonly float scale;
        readonly TextBox tbKey, tbName, tbValue;
        readonly ComboBox cbType;
        readonly RadioButton rbSet, rbDelete;
        public RegEntry Result;

        int S(double n) { return (int)Math.Round(n * scale); }
        Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
        Label Lbl(string text) { return new Label { Text = text, AutoSize = true, Margin = Pad(3, 7, 3, 3) }; }

        public RegEntryEditor(RegEntry src, float scale, Font font)
        {
            this.scale = scale;
            Text = src == null ? "레지스트리 항목 추가" : "레지스트리 항목 편집";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Pad(12, 12, 12, 12);
            var e = src ?? new RegEntry();

            var tl = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var gray = Color.FromArgb(100, 100, 100);
            tbKey = new TextBox { Width = S(520), Text = e.Key, Margin = Pad(3, 4, 3, 0) };
            var keyHelp = new Label { Text = "regedit 주소 표시줄의 경로를 그대로 붙여넣어도 됩니다. (HKEY_CURRENT_USER\\... 또는 HKCU\\...)", AutoSize = true, ForeColor = gray, Margin = Pad(3, 2, 3, 6) };
            var keyBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            keyBox.Controls.AddRange(new Control[] { tbKey, keyHelp });
            tl.Controls.Add(Lbl("키 경로"), 0, 0); tl.Controls.Add(keyBox, 1, 0);

            tbName = new TextBox { Width = S(300), Text = e.Name, Margin = Pad(3, 4, 3, 3) };
            var nameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            nameRow.Controls.AddRange(new Control[] { tbName, new Label { Text = "비워 두면 (기본값)", AutoSize = true, ForeColor = gray, Margin = Pad(3, 7, 3, 3) } });
            tl.Controls.Add(Lbl("값 이름"), 0, 1); tl.Controls.Add(nameRow, 1, 1);

            rbSet = new RadioButton { Text = "값 설정", AutoSize = true, Checked = !e.DeleteValue, Margin = Pad(3, 5, 12, 3) };
            rbDelete = new RadioButton { Text = "값 삭제", AutoSize = true, Checked = e.DeleteValue, Margin = Pad(3, 5, 3, 3) };
            var actRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            actRow.Controls.AddRange(new Control[] { rbSet, rbDelete });
            tl.Controls.Add(Lbl("동작"), 0, 2); tl.Controls.Add(actRow, 1, 2);

            cbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = Pad(3, 4, 3, 3) };
            cbType.Items.AddRange(Reg.TypeLabels);
            int ti = Array.IndexOf(Reg.Types, e.Type);
            cbType.SelectedIndex = ti < 0 ? 0 : ti;
            tl.Controls.Add(Lbl("종류"), 0, 3); tl.Controls.Add(cbType, 1, 3);

            tbValue = new TextBox { Width = S(520), Text = e.Value, Margin = Pad(3, 4, 3, 0) };
            var valHelp = new Label { Text = "숫자: 10진수 또는 0x 로 시작하는 16진수 (예: 1, 0x1f)   이진값: 16진수 바이트 (예: 01 00 ff)", AutoSize = true, ForeColor = gray, Margin = Pad(3, 2, 3, 6) };
            var valBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            valBox.Controls.AddRange(new Control[] { tbValue, valHelp });
            tl.Controls.Add(Lbl("값 데이터"), 0, 4); tl.Controls.Add(valBox, 1, 4);

            EventHandler sync = delegate { cbType.Enabled = tbValue.Enabled = rbSet.Checked; };
            rbSet.CheckedChanged += sync;
            sync(null, EventArgs.Empty);

            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = Pad(0, 8, 0, 0) };
            var ok = new Button { Text = "확인", AutoSize = true, Padding = Pad(8, 3, 8, 3), UseVisualStyleBackColor = true };
            var cancel = new Button { Text = "취소", AutoSize = true, Padding = Pad(8, 3, 8, 3), UseVisualStyleBackColor = true, DialogResult = DialogResult.Cancel };
            ok.Click += delegate
            {
                var r = new RegEntry
                {
                    Enabled = src == null || src.Enabled,
                    Key = Reg.Normalize(tbKey.Text) ?? tbKey.Text.Trim(),
                    Name = tbName.Text.Trim(),
                    Type = Reg.Types[Math.Max(0, cbType.SelectedIndex)],
                    Value = tbValue.Text,
                    DeleteValue = rbDelete.Checked,
                };
                string err = Reg.Validate(r);
                if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Result = r;
                DialogResult = DialogResult.OK;
            };
            btnRow.Controls.AddRange(new Control[] { cancel, ok });
            tl.Controls.Add(btnRow, 0, 5); tl.SetColumnSpan(btnRow, 2);

            Controls.Add(tl);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>설치/설정 진행 상황 창</summary>
    public class ProgressForm : Form
    {
        readonly Label lblStatus;
        readonly ProgressBar pb;
        readonly RichTextBox log;
        readonly Button btnSkip, btnStop, btnClose;
        bool running = true;

        public event EventHandler SkipRequested, StopRequested, OpenLogRequested;

        public ProgressForm(float scale, Font font)
        {
            Func<double, int> S = n => (int)Math.Round(n * scale);
            Text = "진행 상황 - " + MainForm.AppName;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(S(780), S(540));
            MinimumSize = new Size(S(520), S(360));
            ShowInTaskbar = true;
            MinimizeBox = true;
            MaximizeBox = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(S(10), S(10), S(10), S(4)) };
            lblStatus = new Label { Text = "준비 중...", AutoSize = true, Font = new Font(font.FontFamily, 10f, FontStyle.Bold), Margin = new Padding(S(3)) };
            pb = new ProgressBar { Dock = DockStyle.Fill, Height = S(22), Margin = new Padding(S(3), S(4), S(3), S(4)) };
            top.Controls.Add(lblStatus, 0, 0);
            top.Controls.Add(pb, 0, 1);

            log = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(250, 250, 250),
                Font = new Font("Malgun Gothic", 9f), DetectUrls = false, BorderStyle = BorderStyle.None
            };
            var logWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12), S(4), S(12), S(4)) };
            logWrap.Controls.Add(log);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(S(8)), BackColor = Color.FromArgb(243, 243, 243) };
            Func<string, Button> B = t => new Button { Text = t, AutoSize = true, Padding = new Padding(S(8), S(3), S(8), S(3)), Margin = new Padding(S(3)), UseVisualStyleBackColor = true };
            btnClose = B("닫기"); btnClose.Enabled = false; btnClose.Click += delegate { Close(); };
            btnStop = B("■ 중지"); btnStop.Click += delegate { var h = StopRequested; if (h != null) h(this, EventArgs.Empty); };
            btnSkip = B("현재 단계 건너뛰기"); btnSkip.Click += delegate { var h = SkipRequested; if (h != null) h(this, EventArgs.Empty); };
            var btnLog = B("로그 폴더 열기"); btnLog.Click += delegate { var h = OpenLogRequested; if (h != null) h(this, EventArgs.Empty); };
            new ToolTip().SetToolTip(btnSkip, "설치 창이 응답이 없거나 끝났는데도 계속 대기할 때 누르면 다음 항목으로 넘어갑니다. (설치 프로그램을 강제로 종료하지는 않습니다)");
            bottom.Controls.AddRange(new Control[] { btnClose, btnStop, btnSkip, btnLog });

            Controls.Add(logWrap);
            Controls.Add(top);
            Controls.Add(bottom);
        }

        public void SetProgress(int value, int max, string text)
        {
            pb.Maximum = Math.Max(1, max);
            pb.Value = Math.Min(Math.Max(0, value), pb.Maximum);
            lblStatus.Text = text;
        }

        public void AddLine(string line, Color color, bool bold)
        {
            log.SelectionStart = log.TextLength;
            log.SelectionLength = 0;
            log.SelectionColor = color;
            log.SelectionFont = bold ? new Font(log.Font, FontStyle.Bold) : log.Font;
            log.AppendText(line + "\r\n");
            log.ScrollToCaret();
        }

        public string LogText { get { return log.Text; } }

        public void SetDone(string status)
        {
            running = false;
            lblStatus.Text = status;
            btnSkip.Enabled = false;
            btnStop.Enabled = false;
            btnClose.Enabled = true;
            btnClose.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 진행 중에는 창을 닫지 않고 최소화만 허용
            if (running && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MessageBox.Show(this, "작업이 진행 중입니다. 멈추려면 [중지]를 누르세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            base.OnFormClosing(e);
        }
    }

    /// <summary>☕ 커피 후원 QR 창</summary>
    public class CoffeeForm : Form
    {
        /// <summary>QR 이미지: exe 에 포함된 리소스(빌드 시 src\coffee_qr.png) → exe 옆 coffee_qr.png 순서로 찾는다.</summary>
        public static Image LoadQr()
        {
            try
            {
                var s = typeof(CoffeeForm).Assembly.GetManifestResourceStream("coffee_qr.png");
                if (s != null) return Image.FromStream(s);
            }
            catch { }
            try
            {
                string f = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "coffee_qr.png");
                if (File.Exists(f)) using (var tmp = Image.FromFile(f)) return new Bitmap(tmp);
            }
            catch { }
            return null;
        }

        public CoffeeForm(float scale, Font font)
        {
            Func<double, int> S = n => (int)Math.Round(n * scale);
            Text = "커피 한 잔 후원하기";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.White;
            Padding = new Padding(S(20), S(16), S(20), S(14));

            var fl = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            var title = new Label { Text = "☕ 개발자에게 커피 한 잔", AutoSize = true, Font = new Font(font.FontFamily, 13f, FontStyle.Bold), ForeColor = Color.FromArgb(111, 78, 55), Margin = new Padding(0, 0, 0, S(4)) };
            var msg = new Label
            {
                Text = "이 프로그램이 도움이 되셨다면 커피 한 잔으로 응원해 주세요!\n휴대폰 카메라로 QR 코드를 찍으면 됩니다. 감사합니다 :)",
                AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(0, 0, 0, S(12))
            };
            fl.Controls.Add(title);
            fl.Controls.Add(msg);

            Image qr = LoadQr();
            if (qr != null)
            {
                var pic = new PictureBox { Image = qr, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(S(260), S(260)), Margin = new Padding(S(40), 0, 0, S(10)) };
                fl.Controls.Add(pic);
            }
            else
            {
                fl.Controls.Add(new Label
                {
                    Text = "(QR 이미지가 아직 등록되지 않았습니다)", AutoSize = true, ForeColor = Color.Gray,
                    Margin = new Padding(0, S(20), 0, S(20))
                });
            }

            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var close = new Button { Text = "닫기", AutoSize = true, Padding = new Padding(S(10), S(3), S(10), S(3)), DialogResult = DialogResult.OK, UseVisualStyleBackColor = true };
            row.Controls.Add(close);
            if (AppInfo.CoffeeUrl.Length > 0)
            {
                var link = new Button { Text = "후원 페이지 열기", AutoSize = true, Padding = new Padding(S(10), S(3), S(10), S(3)), UseVisualStyleBackColor = true };
                link.Click += delegate { try { System.Diagnostics.Process.Start(AppInfo.CoffeeUrl); } catch { } };
                row.Controls.Add(link);
            }
            fl.Controls.Add(row);
            Controls.Add(fl);
            AcceptButton = close;
            CancelButton = close;
        }
    }

    /// <summary>업데이트 다운로드 진행 창</summary>
    public class UpdateDownloadForm : Form
    {
        readonly UpdateInfo info;
        readonly ProgressBar pb;
        public string DownloadedFile;
        public string Error;

        public UpdateDownloadForm(UpdateInfo info, float scale, Font font)
        {
            this.info = info;
            Func<double, int> S = n => (int)Math.Round(n * scale);
            Text = "업데이트";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ControlBox = false;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(420), S(100));
            var lbl = new Label { Text = "새 버전 " + info.Tag + " 을(를) 내려받는 중입니다...", AutoSize = true, Location = new Point(S(16), S(16)) };
            pb = new ProgressBar { Location = new Point(S(16), S(48)), Size = new Size(S(388), S(22)) };
            Controls.Add(lbl);
            Controls.Add(pb);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var t = new System.Threading.Thread(() =>
            {
                try { DownloadedFile = Updater.Download(info, p => BeginInvoke(new Action(() => pb.Value = Math.Min(100, Math.Max(0, p))))); }
                catch (Exception ex) { Error = ex.Message; }
                BeginInvoke(new Action(() => { DialogResult = Error == null ? DialogResult.OK : DialogResult.Abort; }));
            }) { IsBackground = true };
            t.Start();
        }
    }

    /// <summary>설치 중 화면 오른쪽 아래에 시리얼을 보여 주는 작은 창 (항상 위)</summary>
    public class SerialPopup : Form
    {
        public SerialPopup(string name, string serial, bool interactive, float scale, Font font)
        {
            Func<double, int> S = n => (int)Math.Round(n * scale);
            Text = "시리얼 번호";
            TopMost = true;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Font = font;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(S(10));

            var fl = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var t1 = new Label { Text = name, AutoSize = true, Font = new Font(font, FontStyle.Bold), Margin = new Padding(S(3)) };
            var t2 = new Label
            {
                Text = interactive ? "설치 창의 시리얼 입력란에서 Ctrl+V 로 붙여넣으세요." : "시리얼이 클립보드에 복사되었습니다.",
                AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(S(3))
            };
            var tb = new TextBox { ReadOnly = true, Text = serial, Font = new Font("Consolas", 13f), Width = S(360), Margin = new Padding(S(3), S(6), S(3), S(6)) };
            var btn = new Button { Text = "다시 복사", AutoSize = true, Padding = new Padding(S(8), S(3), S(8), S(3)), UseVisualStyleBackColor = true };
            btn.Click += delegate { try { Clipboard.SetDataObject(serial, true, 20, 100); } catch { } };
            fl.Controls.AddRange(new Control[] { t1, t2, tb, btn });
            Controls.Add(fl);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }
    }
}
