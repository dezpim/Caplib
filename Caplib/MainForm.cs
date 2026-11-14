using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Caplib
{
    internal sealed class CaptureJob
    {
        public int Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public Point ClickPoint { get; set; }
        public MouseClickType Button { get; set; }
        public CaptureScope Scope { get; set; }
        public bool DrawMarker { get; set; }
        public SaveImageFormat Format { get; set; }
        public string OutputPath { get; set; }
        public Rectangle Bounds { get; set; }
    }

    internal sealed class MainForm : Form
    {
        private const int HOTKEY_ID = 9001;

        private readonly string _baseDirectory;
        private readonly ClickHook _clickHook;
        private BlockingCollection<CaptureJob> _queue;
        private Thread _workerThread;
        private Thread _autoCaptureThread;

        private bool _isRecording;
        private bool _isAutoRunning;
        private volatile bool _cancelRequested;
        private int _captureCount;
        private string _currentSessionDir;
        private string _lastZipPath;

        // UI Controls - Header
        private Label _lblStatus;
        private Label _lblCount;

        // UI Controls - Mode
        private RadioButton _rbAutoMode;
        private RadioButton _rbManualMode;

        // UI Controls - Settings
        private Panel _settingsPanel;
        private Label _lblPages;
        private NumericUpDown _numPages;
        private Label _lblDelay;
        private NumericUpDown _numDelay;
        private Label _lblCountdown;
        private NumericUpDown _numCountdown;
        private ComboBox _cmbScope;
        private ComboBox _cmbFormat;
        private ComboBox _cmbClickFilter;
        private CheckBox _chkMarker;
        private CheckBox _chkZipAndClean;

        // UI Controls - Info & Actions
        private Label _lblHotkeyInfo;
        private Label _lblPath;
        private Label _lblLastFile;
        private Button _btnToggle;
        private Button _btnOpenFolder;

        public MainForm()
        {
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _currentSessionDir = Path.Combine(_baseDirectory, "captures");
            _clickHook = new ClickHook();
            _clickHook.MouseClicked += OnMouseClicked;

            InitializeUi();
            RegisterGlobalHotkey();
        }

        private void RegisterGlobalHotkey()
        {
            try
            {
                NativeMethods.RegisterHotKey(Handle, HOTKEY_ID, NativeMethods.MOD_NONE, NativeMethods.VK_F8);
            }
            catch
            {
                // Hotkey 등록 실패 시에도 앱 실행 유지
            }
        }

        private void UnregisterGlobalHotkey()
        {
            try
            {
                NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID);
            }
            catch
            {
                // 무시
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleOperation();
            }
            base.WndProc(ref m);
        }

        private void InitializeUi()
        {
            Text = "Caplib - 자동 넘김 & 클릭 캡처 유틸리티";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 520);
            Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular);
            BackColor = Color.FromArgb(248, 250, 252);

            // 1. Header (상태 & 카운트)
            _lblStatus = new Label();
            _lblStatus.Text = "● 대기 중";
            _lblStatus.Font = new Font("Malgun Gothic", 11f, FontStyle.Bold);
            _lblStatus.ForeColor = Color.FromArgb(100, 116, 139);
            _lblStatus.Location = new Point(18, 14);
            _lblStatus.Size = new Size(250, 24);
            Controls.Add(_lblStatus);

            _lblCount = new Label();
            _lblCount.Text = "진행: 0 / 20 페이지";
            _lblCount.TextAlign = ContentAlignment.MiddleRight;
            _lblCount.ForeColor = Color.FromArgb(51, 65, 85);
            _lblCount.Location = new Point(270, 16);
            _lblCount.Size = new Size(182, 22);
            Controls.Add(_lblCount);

            // 2. Mode Radio Buttons
            _rbAutoMode = new RadioButton();
            _rbAutoMode.Text = "자동 넘김 캡처 (우측 화살표)";
            _rbAutoMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
            _rbAutoMode.ForeColor = Color.FromArgb(30, 41, 59);
            _rbAutoMode.Location = new Point(18, 44);
            _rbAutoMode.Size = new Size(220, 24);
            _rbAutoMode.Checked = true;
            _rbAutoMode.CheckedChanged += OnModeChanged;
            Controls.Add(_rbAutoMode);

            _rbManualMode = new RadioButton();
            _rbManualMode.Text = "수동 클릭 감지";
            _rbManualMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular);
            _rbManualMode.ForeColor = Color.FromArgb(71, 85, 105);
            _rbManualMode.Location = new Point(260, 44);
            _rbManualMode.Size = new Size(180, 24);
            _rbManualMode.CheckedChanged += OnModeChanged;
            Controls.Add(_rbManualMode);

            // 3. Settings Panel
            _settingsPanel = new Panel();
            _settingsPanel.BackColor = Color.White;
            _settingsPanel.BorderStyle = BorderStyle.FixedSingle;
            _settingsPanel.Location = new Point(18, 74);
            _settingsPanel.Size = new Size(434, 256);
            Controls.Add(_settingsPanel);

            // Row 1: 페이지 수 (자동 모드)
            _lblPages = AddLabel(_settingsPanel, "페이지 수", 14, 14);
            _numPages = new NumericUpDown();
            _numPages.Location = new Point(130, 11);
            _numPages.Size = new Size(100, 25);
            _numPages.Minimum = 1;
            _numPages.Maximum = 9999;
            _numPages.Value = 20;
            _settingsPanel.Controls.Add(_numPages);
            Label lblPagesUnit = AddLabel(_settingsPanel, "페이지 (반복 횟수)", 236, 14);
            lblPagesUnit.ForeColor = Color.FromArgb(148, 163, 184);

            // Row 2: 넘김 후 딜레이 (자동 모드)
            _lblDelay = AddLabel(_settingsPanel, "넘김 후 대기", 14, 46);
            _numDelay = new NumericUpDown();
            _numDelay.Location = new Point(130, 43);
            _numDelay.Size = new Size(100, 25);
            _numDelay.DecimalPlaces = 1;
            _numDelay.Increment = 0.1m;
            _numDelay.Minimum = 0.1m;
            _numDelay.Maximum = 10.0m;
            _numDelay.Value = 0.5m;
            _settingsPanel.Controls.Add(_numDelay);
            Label lblDelayUnit = AddLabel(_settingsPanel, "초 (이미지 로딩 대기)", 236, 46);
            lblDelayUnit.ForeColor = Color.FromArgb(148, 163, 184);

            // Row 3: 시작 전 카운트다운 (자동 모드)
            _lblCountdown = AddLabel(_settingsPanel, "시작 전 대기", 14, 78);
            _numCountdown = new NumericUpDown();
            _numCountdown.Location = new Point(130, 75);
            _numCountdown.Size = new Size(100, 25);
            _numCountdown.Minimum = 0;
            _numCountdown.Maximum = 10;
            _numCountdown.Value = 3;
            _settingsPanel.Controls.Add(_numCountdown);
            Label lblCountdownUnit = AddLabel(_settingsPanel, "초 (뷰어 창 선택 시간)", 236, 78);
            lblCountdownUnit.ForeColor = Color.FromArgb(148, 163, 184);

            // Row 4: 캡처 범위
            AddLabel(_settingsPanel, "캡처 범위", 14, 110);
            _cmbScope = new ComboBox();
            _cmbScope.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbScope.Items.AddRange(new object[] { "전체 화면 (모든 모니터)", "주 모니터", "활성 창" });
            _cmbScope.SelectedIndex = 0;
            _cmbScope.Location = new Point(130, 107);
            _cmbScope.Size = new Size(286, 25);
            _settingsPanel.Controls.Add(_cmbScope);

            // Row 5: 저장 형식
            AddLabel(_settingsPanel, "저장 형식", 14, 142);
            _cmbFormat = new ComboBox();
            _cmbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbFormat.Items.AddRange(new object[] { "PNG (무손실)", "JPEG (용량 절약)" });
            _cmbFormat.SelectedIndex = 0;
            _cmbFormat.Location = new Point(130, 139);
            _cmbFormat.Size = new Size(286, 25);
            _settingsPanel.Controls.Add(_cmbFormat);

            // Row 6: 클릭 대상 / 마커 (수동 모드 전용)
            Label lblClick = AddLabel(_settingsPanel, "클릭 대상", 14, 174);
            _cmbClickFilter = new ComboBox();
            _cmbClickFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbClickFilter.Items.AddRange(new object[] { "왼쪽 클릭만", "왼쪽 + 오른쪽 클릭" });
            _cmbClickFilter.SelectedIndex = 0;
            _cmbClickFilter.Location = new Point(130, 171);
            _cmbClickFilter.Size = new Size(140, 25);
            _cmbClickFilter.Enabled = false;
            _settingsPanel.Controls.Add(_cmbClickFilter);

            _chkMarker = new CheckBox();
            _chkMarker.Text = "클릭 위치 마커";
            _chkMarker.Checked = false;
            _chkMarker.Location = new Point(282, 172);
            _chkMarker.Size = new Size(134, 24);
            _chkMarker.Enabled = false;
            _settingsPanel.Controls.Add(_chkMarker);

            // Row 7: 완료 후 압축 및 삭제 (핵심 옵션)
            _chkZipAndClean = new CheckBox();
            _chkZipAndClean.Text = "완료 시 ZIP 압축 생성 후 원본 이미지 파일 자동 삭제";
            _chkZipAndClean.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
            _chkZipAndClean.ForeColor = Color.FromArgb(37, 99, 235);
            _chkZipAndClean.Checked = true;
            _chkZipAndClean.Location = new Point(14, 214);
            _chkZipAndClean.Size = new Size(402, 24);
            _settingsPanel.Controls.Add(_chkZipAndClean);

            // 4. Hotkey Info & Path & Status
            _lblHotkeyInfo = new Label();
            _lblHotkeyInfo.Text = "※ 단축키 [F8]: 대상 뷰어 화면을 보면서 즉시 시작 / 중지 가능";
            _lblHotkeyInfo.Font = new Font("Malgun Gothic", 9f, FontStyle.Bold);
            _lblHotkeyInfo.ForeColor = Color.FromArgb(30, 58, 138);
            _lblHotkeyInfo.Location = new Point(18, 340);
            _lblHotkeyInfo.Size = new Size(434, 20);
            Controls.Add(_lblHotkeyInfo);

            _lblPath = new Label();
            _lblPath.Text = string.Format("저장 경로: {0}", _currentSessionDir);
            _lblPath.ForeColor = Color.FromArgb(71, 85, 105);
            _lblPath.AutoEllipsis = true;
            _lblPath.Location = new Point(18, 368);
            _lblPath.Size = new Size(434, 20);
            Controls.Add(_lblPath);

            _lblLastFile = new Label();
            _lblLastFile.Text = "최근 저장: 없음";
            _lblLastFile.ForeColor = Color.FromArgb(100, 116, 139);
            _lblLastFile.AutoEllipsis = true;
            _lblLastFile.Location = new Point(18, 392);
            _lblLastFile.Size = new Size(434, 20);
            Controls.Add(_lblLastFile);

            // 5. Action Buttons
            _btnToggle = new Button();
            _btnToggle.Text = "자동 캡처 시작 (F8)";
            _btnToggle.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            _btnToggle.BackColor = Color.FromArgb(37, 99, 235);
            _btnToggle.ForeColor = Color.White;
            _btnToggle.FlatStyle = FlatStyle.Flat;
            _btnToggle.FlatAppearance.BorderSize = 0;
            _btnToggle.Location = new Point(18, 424);
            _btnToggle.Size = new Size(270, 44);
            _btnToggle.Click += (s, e) => ToggleOperation();
            Controls.Add(_btnToggle);

            _btnOpenFolder = new Button();
            _btnOpenFolder.Text = "저장 폴더 열기";
            _btnOpenFolder.BackColor = Color.White;
            _btnOpenFolder.ForeColor = Color.FromArgb(30, 41, 59);
            _btnOpenFolder.FlatStyle = FlatStyle.Flat;
            _btnOpenFolder.Location = new Point(298, 424);
            _btnOpenFolder.Size = new Size(154, 44);
            _btnOpenFolder.Click += OnOpenFolderClick;
            Controls.Add(_btnOpenFolder);
        }

        private static Label AddLabel(Control parent, string text, int x, int y)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.ForeColor = Color.FromArgb(51, 65, 85);
            lbl.Location = new Point(x, y);
            lbl.Size = new Size(110, 22);
            parent.Controls.Add(lbl);
            return lbl;
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            bool isAuto = _rbAutoMode.Checked;
            _numPages.Enabled = isAuto;
            _numDelay.Enabled = isAuto;
            _numCountdown.Enabled = isAuto;
            _lblPages.ForeColor = isAuto ? Color.FromArgb(51, 65, 85) : Color.FromArgb(148, 163, 184);
            _lblDelay.ForeColor = isAuto ? Color.FromArgb(51, 65, 85) : Color.FromArgb(148, 163, 184);
            _lblCountdown.ForeColor = isAuto ? Color.FromArgb(51, 65, 85) : Color.FromArgb(148, 163, 184);

            _cmbClickFilter.Enabled = !isAuto;
            _chkMarker.Enabled = !isAuto;

            if (isAuto)
            {
                _rbAutoMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
                _rbAutoMode.ForeColor = Color.FromArgb(30, 41, 59);
                _rbManualMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular);
                _rbManualMode.ForeColor = Color.FromArgb(71, 85, 105);
                _btnToggle.Text = "자동 캡처 시작 (F8)";
                _lblCount.Text = string.Format("진행: 0 / {0} 페이지", _numPages.Value);
            }
            else
            {
                _rbManualMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold);
                _rbManualMode.ForeColor = Color.FromArgb(30, 41, 59);
                _rbAutoMode.Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular);
                _rbAutoMode.ForeColor = Color.FromArgb(71, 85, 105);
                _btnToggle.Text = "기록 시작 (F8)";
                _lblCount.Text = "저장된 캡처: 0장";
            }
        }

        private void ToggleOperation()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)ToggleOperation);
                return;
            }

            if (_isAutoRunning)
            {
                StopAutoCapture();
            }
            else if (_isRecording)
            {
                StopManualRecording();
            }
            else
            {
                if (_rbAutoMode.Checked)
                {
                    StartAutoCapture();
                }
                else
                {
                    StartManualRecording();
                }
            }
        }

        #region Auto Capture Mode (우측 화살표 넘김 + 딜레이 + 캡처)

        private void StartAutoCapture()
        {
            try
            {
                string sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _currentSessionDir = Path.Combine(_baseDirectory, "captures", sessionName);
                Directory.CreateDirectory(_currentSessionDir);

                _captureCount = 0;
                _lastZipPath = null;
                _cancelRequested = false;
                _isAutoRunning = true;

                int totalPages = (int)_numPages.Value;
                int delayMs = (int)(_numDelay.Value * 1000m);
                int countdownSec = (int)_numCountdown.Value;
                CaptureScope scope = (CaptureScope)_cmbScope.SelectedIndex;
                SaveImageFormat format = (SaveImageFormat)_cmbFormat.SelectedIndex;
                bool zipAndClean = _chkZipAndClean.Checked;

                UpdateUiForActiveState(true, true);

                _autoCaptureThread = new Thread(() =>
                {
                    AutoCaptureLoop(totalPages, delayMs, countdownSec, scope, format, zipAndClean);
                });
                _autoCaptureThread.IsBackground = true;
                _autoCaptureThread.Start();
            }
            catch (Exception ex)
            {
                StopAutoCapture();
                MessageBox.Show(
                    this,
                    string.Format("자동 캡처를 시작할 수 없습니다.\n{0}", ex.Message),
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopAutoCapture()
        {
            _cancelRequested = true;
            if (_autoCaptureThread != null && _autoCaptureThread.IsAlive)
            {
                _autoCaptureThread.Join(500);
            }
            _autoCaptureThread = null;
            _isAutoRunning = false;

            UpdateUiForActiveState(false, true);
        }

        private void AutoCaptureLoop(
            int totalPages,
            int delayMs,
            int countdownSec,
            CaptureScope scope,
            SaveImageFormat format,
            bool zipAndClean)
        {
            // 1. 카운트다운 (창 선택 대기)
            for (int c = countdownSec; c > 0; c--)
            {
                if (_cancelRequested)
                {
                    FinishAutoSession(zipAndClean);
                    return;
                }

                int remaining = c;
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        _lblStatus.Text = string.Format("● {0}초 후 시작 (캡처할 창 활성화)", remaining);
                        _lblStatus.ForeColor = Color.FromArgb(234, 88, 12);
                    }
                }));

                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(100);
                    if (_cancelRequested)
                    {
                        FinishAutoSession(zipAndClean);
                        return;
                    }
                }
            }

            // 2. 반복 넘김 & 캡처
            string ext = format == SaveImageFormat.Jpeg ? "jpg" : "png";
            int count = 0;

            for (int page = 1; page <= totalPages; page++)
            {
                if (_cancelRequested)
                {
                    break;
                }

                int curPage = page;
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        _lblStatus.Text = string.Format("● 넘김 및 로딩 대기 중... ({0}/{1})", curPage, totalPages);
                        _lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                    }
                }));

                // (1) 우측 화살표 키 전송
                NativeMethods.SendRightArrow();

                // (2) 이미지 로딩 대기 시간 (50ms 단위 취소 체크)
                int waited = 0;
                while (waited < delayMs)
                {
                    if (_cancelRequested)
                    {
                        break;
                    }
                    int step = Math.Min(50, delayMs - waited);
                    Thread.Sleep(step);
                    waited += step;
                }

                if (_cancelRequested)
                {
                    break;
                }

                // (3) 화면 캡처
                Rectangle bounds = ScreenCapturer.GetTargetBounds(scope);
                string fileName = string.Format("{0:D4}.{1}", curPage, ext);
                string filePath = Path.Combine(_currentSessionDir, fileName);

                ScreenCapturer.CaptureAndSave(bounds, Point.Empty, false, format, filePath);
                count++;
                _captureCount = count;

                // (4) UI 갱신
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        int percent = (curPage * 100) / totalPages;
                        _lblCount.Text = string.Format("진행: {0} / {1} 페이지 ({2}%)", curPage, totalPages, percent);
                        _lblLastFile.Text = string.Format("최근 저장: {0}", fileName);
                    }
                }));
            }

            FinishAutoSession(zipAndClean);
        }

        private void FinishAutoSession(bool zipAndClean)
        {
            if (zipAndClean && _captureCount > 0)
            {
                CompressAndCleanSession(_currentSessionDir);
            }
            else
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        _lblStatus.Text = _cancelRequested ? "● 사용자 중지됨" : "● 캡처 완료";
                        _lblStatus.ForeColor = _cancelRequested ? Color.FromArgb(234, 88, 12) : Color.FromArgb(16, 185, 129);
                    }
                }));
            }

            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed)
                {
                    _isAutoRunning = false;
                    _cancelRequested = false;
                    UpdateUiForActiveState(false, true);
                }
            }));
        }

        #endregion

        #region Manual Click Mode (수동 클릭 감지)

        private void StartManualRecording()
        {
            try
            {
                string sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _currentSessionDir = Path.Combine(_baseDirectory, "captures", sessionName);
                Directory.CreateDirectory(_currentSessionDir);

                _captureCount = 0;
                _lastZipPath = null;
                _queue = new BlockingCollection<CaptureJob>();
                _workerThread = new Thread(ManualWorkerLoop);
                _workerThread.IsBackground = true;
                _workerThread.Start();

                _clickHook.Install();
                _isRecording = true;

                UpdateUiForActiveState(true, false);
            }
            catch (Exception ex)
            {
                StopManualRecording();
                MessageBox.Show(
                    this,
                    string.Format("기록을 시작할 수 없습니다.\n{0}", ex.Message),
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopManualRecording()
        {
            _isRecording = false;
            _clickHook.Uninstall();

            if (_queue != null)
            {
                _queue.CompleteAdding();
            }

            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(1500);
            }

            if (_queue != null)
            {
                _queue.Dispose();
                _queue = null;
            }
            _workerThread = null;

            if (_chkZipAndClean.Checked && _captureCount > 0)
            {
                ThreadPool.QueueUserWorkItem(s => CompressAndCleanSession(_currentSessionDir));
            }

            UpdateUiForActiveState(false, false);
        }

        private void OnMouseClicked(object sender, MouseClickEventArgs e)
        {
            if (!_isRecording || _queue == null || _queue.IsAddingCompleted)
            {
                return;
            }

            if (e.Button == MouseClickType.Middle)
            {
                return;
            }

            if (e.Button == MouseClickType.Right && _cmbClickFilter.SelectedIndex == 0)
            {
                return;
            }

            NativeMethods.POINT pt = new NativeMethods.POINT { x = e.Location.X, y = e.Location.Y };
            IntPtr hwndUnderCursor = NativeMethods.WindowFromPoint(pt);
            IntPtr rootHwnd = hwndUnderCursor != IntPtr.Zero
                ? NativeMethods.GetAncestor(hwndUnderCursor, NativeMethods.GA_ROOT)
                : IntPtr.Zero;

            if (rootHwnd == Handle)
            {
                return;
            }

            int seq = Interlocked.Increment(ref _captureCount);
            CaptureScope scope = (CaptureScope)_cmbScope.SelectedIndex;
            SaveImageFormat format = (SaveImageFormat)_cmbFormat.SelectedIndex;
            string ext = format == SaveImageFormat.Jpeg ? "jpg" : "png";
            string fileName = string.Format("{0:D4}_{1}_{2}.{3}",
                seq,
                e.Timestamp.ToString("HHmmss_fff"),
                e.Button == MouseClickType.Right ? "R" : "L",
                ext);

            CaptureJob job = new CaptureJob
            {
                Sequence = seq,
                Timestamp = e.Timestamp,
                ClickPoint = e.Location,
                Button = e.Button,
                Scope = scope,
                DrawMarker = _chkMarker.Checked,
                Format = format,
                OutputPath = Path.Combine(_currentSessionDir, fileName),
                Bounds = ScreenCapturer.GetTargetBounds(scope)
            };

            try
            {
                _queue.Add(job);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ManualWorkerLoop()
        {
            BlockingCollection<CaptureJob> localQueue = _queue;
            if (localQueue == null)
            {
                return;
            }

            foreach (CaptureJob job in localQueue.GetConsumingEnumerable())
            {
                try
                {
                    ScreenCapturer.CaptureAndSave(
                        job.Bounds,
                        job.ClickPoint,
                        job.DrawMarker,
                        job.Format,
                        job.OutputPath);

                    string fileName = Path.GetFileName(job.OutputPath);
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed)
                        {
                            _lblCount.Text = string.Format("저장된 캡처: {0}장", job.Sequence);
                            _lblLastFile.Text = string.Format("최근 저장: {0}", fileName);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    string errMsg = ex.Message;
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed)
                        {
                            _lblLastFile.Text = string.Format("저장 실패: {0}", errMsg);
                        }
                    }));
                }
            }
        }

        #endregion

        #region ZIP Compression & Cleanup

        private void CompressAndCleanSession(string sessionDir)
        {
            if (string.IsNullOrEmpty(sessionDir) || !Directory.Exists(sessionDir))
            {
                return;
            }

            string[] files = Directory.GetFiles(sessionDir);
            if (files.Length == 0)
            {
                try { Directory.Delete(sessionDir, true); } catch { }
                return;
            }

            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed)
                {
                    _lblStatus.Text = "● ZIP 압축 중...";
                    _lblStatus.ForeColor = Color.FromArgb(37, 99, 235);
                    _lblLastFile.Text = "캡처 파일 압축 및 정리 중...";
                }
            }));

            string parentDir = Path.GetDirectoryName(sessionDir);
            string dirName = Path.GetFileName(sessionDir);
            string zipPath = Path.Combine(parentDir, dirName + ".zip");

            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                System.IO.Compression.ZipFile.CreateFromDirectory(
                    sessionDir,
                    zipPath,
                    System.IO.Compression.CompressionLevel.Optimal,
                    false);

                // 원본 이미지 폴더 및 파일 삭제
                Directory.Delete(sessionDir, true);
                _lastZipPath = zipPath;

                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        _lblStatus.Text = "● 압축 완료";
                        _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        _lblLastFile.Text = string.Format("압축 파일: {0} ({1}장)", Path.GetFileName(zipPath), files.Length);
                        _lblPath.Text = string.Format("저장 완료: {0}", zipPath);
                    }
                }));
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed)
                    {
                        _lblStatus.Text = "● 압축 오류";
                        _lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
                        _lblLastFile.Text = string.Format("압축 실패: {0}", errMsg);
                    }
                }));
            }
        }

        #endregion

        private void UpdateUiForActiveState(bool active, bool isAuto)
        {
            _rbAutoMode.Enabled = !active;
            _rbManualMode.Enabled = !active;
            _settingsPanel.Enabled = !active;

            if (active)
            {
                _btnToggle.Text = "중지 (F8)";
                _btnToggle.BackColor = Color.FromArgb(220, 38, 38);
                _lblPath.Text = string.Format("저장 폴더: {0}", _currentSessionDir);

                if (!isAuto)
                {
                    _lblStatus.Text = "● 기록 중 (클릭 대기)";
                    _lblStatus.ForeColor = Color.FromArgb(220, 38, 38);
                    _lblCount.Text = "저장된 캡처: 0장";
                    _lblLastFile.Text = "최근 저장: 대기 중...";
                }
            }
            else
            {
                _btnToggle.BackColor = Color.FromArgb(37, 99, 235);
                _btnToggle.Text = _rbAutoMode.Checked ? "자동 캡처 시작 (F8)" : "기록 시작 (F8)";

                if (_captureCount == 0 && string.IsNullOrEmpty(_lastZipPath))
                {
                    _lblStatus.Text = "● 대기 중";
                    _lblStatus.ForeColor = Color.FromArgb(100, 116, 139);
                }
            }
        }

        private void OnOpenFolderClick(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastZipPath) && File.Exists(_lastZipPath))
                {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", _lastZipPath));
                    return;
                }

                string targetDir = Directory.Exists(_currentSessionDir)
                    ? _currentSessionDir
                    : Path.Combine(_baseDirectory, "captures");

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                Process.Start("explorer.exe", targetDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "폴더를 열 수 없습니다: " + ex.Message, "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterGlobalHotkey();

            if (_isAutoRunning)
            {
                StopAutoCapture();
            }
            if (_isRecording)
            {
                StopManualRecording();
            }

            _clickHook.Dispose();
            base.OnFormClosing(e);
        }
    }
}
