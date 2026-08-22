using Microsoft.Win32;
using NAudio.Vorbis;
using NAudio.Wave;
using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ScrollBar;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace OSAKA
{
    public static class VisualTreeHelpers
    {
        public static IEnumerable<DependencyObject> Descendants(this DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;

                foreach (var descendant in child.Descendants())
                {
                    yield return descendant;
                }
            }
        }
    }

    public enum ClockDisplayMode
    {
        DateOnly,
        ClockOnly,
        Both
    }

    public class NotificationItem : INotifyPropertyChanged
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Header { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public SolidColorBrush BackgroundBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public DateTime ExpiryTime { get; set; }
        public DispatcherTimer? Timer { get; set; }

        private Thickness _currentMargin = new Thickness(0, -120, 0, 5);
        public Thickness CurrentMargin
        {
            get => _currentMargin;
            set
            {
                _currentMargin = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentMargin)));
            }
        }

        private double _currentOpacity = 0.0;
        public double CurrentOpacity
        {
            get => _currentOpacity;
            set
            {
                _currentOpacity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOpacity)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void AnimateEnter()
        {
            double startTop = -120;
            double endTop = 5;
            double startOpacity = 0;
            double endOpacity = 1;

            int steps = 30;
            int currentStep = 0;
            DispatcherTimer dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500.0 / steps) };
            dt.Tick += (s, e) =>
            {
                currentStep++;
                double t = (double)currentStep / steps;
                double tEaseOut = 1 - Math.Pow(1 - t, 3);

                CurrentMargin = new Thickness(0, startTop + (endTop - startTop) * tEaseOut, 0, 5);
                CurrentOpacity = startOpacity + (endOpacity - startOpacity) * tEaseOut;

                if (currentStep >= steps)
                {
                    CurrentMargin = new Thickness(0, endTop, 0, 5);
                    CurrentOpacity = endOpacity;
                    dt.Stop();
                }
            };
            dt.Start();
        }

        public void AnimateLeave(Action onComplete)
        {
            double startTop = CurrentMargin.Top;
            double endTop = -120;
            double startOpacity = CurrentOpacity;
            double endOpacity = 0;

            int steps = 30;
            int currentStep = 0;
            DispatcherTimer dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500.0 / steps) };
            dt.Tick += (s, e) =>
            {
                currentStep++;
                double t = (double)currentStep / steps;
                double tEaseIn = t * t * t;

                CurrentMargin = new Thickness(0, startTop + (endTop - startTop) * tEaseIn, 0, 5);
                CurrentOpacity = startOpacity + (endOpacity - startOpacity) * tEaseIn;

                if (currentStep >= steps)
                {
                    dt.Stop();
                    onComplete?.Invoke();
                }
            };
            dt.Start();
        }
    }

    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime DueAt { get; set; }
        public bool IsDone { get; set; }
        public bool Notified { get; set; }
        public bool IsCollapsed { get; set; }
    }

    public class FolderData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "未分類";
        public int Order { get; set; }
    }

    public class MemoData
    {
        public string Title { get; set; } = "無題";
        public string Text { get; set; } = string.Empty;
        public bool IsCollapsed { get; set; }
        public int Order { get; set; }
        public string FolderId { get; set; } = string.Empty;
    }

    public class ListMemoItemData
    {
        public string Text { get; set; } = "項目";
        public bool IsChecked1 { get; set; }
        public bool IsChecked2 { get; set; }
    }

    public class ListMemoData
    {
        public string Title { get; set; } = "リスト";
        public bool IsCollapsed { get; set; }
        public int Order { get; set; }
        public string FolderId { get; set; } = string.Empty;
        public List<ListMemoItemData> Items { get; set; } = new();
    }

    public class MemoDataFile
    {
        public List<MemoData> TextMemos { get; set; } = new();
        public List<ListMemoData> ListMemos { get; set; } = new();
        public List<FolderData> TextFolders { get; set; } = new();
        public List<FolderData> ListFolders { get; set; } = new();
    }

    public class TimestampFolderDataFile
    {
        public List<FolderData> Folders { get; set; } = new();
    }

    // タイムスタンプ本体とフォルダを同じJSONにも保存する。
    // 旧形式（TimestampBox[]）も LoadTimestamps() で引き続き読み込める。
    public class TimestampDataFile
    {
        public List<FolderData> Folders { get; set; } = new();
        public List<TimestampBox> Boxes { get; set; } = new();
    }

    public class TimestampItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Time { get; set; } = "00:00:00";
        public string Body { get; set; } = string.Empty;
        public bool IsChecked { get; set; }

        // このタイムスタンプ自身の出典。
        // LiveUrl / RecordingFileName のどちらか一方だけを設定する。
        public string LiveUrl { get; set; } = string.Empty;
        public string RecordingFileName { get; set; } = string.Empty;
    }

    public class TimestampBox
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "タイムスタンプ";

        // 録画ファイル
        public string RecordingFileName { get; set; } = string.Empty;

        // このタイムスタンプを作成した配信URL
        public string LiveUrl { get; set; } = string.Empty;

        public bool IsCollapsed { get; set; }

        // ショートカットでタイムスタンプを追加する対象BOX。
        // true にできるBOXは常に1つだけ。
        public bool IsShortcutTarget { get; set; }

        public bool SortDescending { get; set; } = true;
        // タイムスタンプの並び順: time_asc / time_desc / added_desc
        public string SortMode { get; set; } = "added_desc";
        public int Order { get; set; }
        public string FolderId { get; set; } = string.Empty;
        public List<TimestampItem> Items { get; set; } = new();
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _obsMuteTimer;
        private readonly DispatcherTimer _taskDueTimer;
        private readonly SemaphoreSlim _obsLock = new(1, 1);
        private Forms.NotifyIcon? _notifyIcon;
        private Cursor? _urotaNormalCursor;
        private Cursor? _urotaHandCursor;
        private Cursor? _urotaTextCursor;
        private bool _useUrotaCursor;
        private bool _cursorHookInstalled;
        private bool _isBackgroundMode;
        private bool _isWindowedMode;
        private bool _fixedWindow1920;
        private bool _soundEffectsEnabled = true;
        private string? _lastRecordingElapsed;
        private YouTubeChatPoller? _poller;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _autoLiveCts;
        private OBSWebsocket _obs;
        private readonly SemaphoreSlim _obsConnectionLock = new(1, 1);
        private CancellationTokenSource? _obsReconnectCts;
        private Task? _obsReconnectTask;

        private int _obsReconnectDelayMs = 1000;
        private const int ObsReconnectMaxDelayMs = 10000;

        private DateTime _lastObsConnectionSuccess = DateTime.MinValue;
        private bool _isObsConnected = false;
        private bool _isChatPaused = false;
        private bool _isChatHidden = false;
        private bool _isAutoConnectedLive = false;
        private bool _isScheduledLive = false;
        private bool _isObsPollRunning = false;
        private string? _currentLiveUrl;
        private DateTimeOffset? _liveStartedAt;
        private string _currentServerIp = "localhost";
        private int _currentServerPort = 5000;
        private string _obsMicInputName = "マイク";
        private IWavePlayer? _bgmPlayer;
        private WaveStream? _bgmReader;
        private float _bgmVolume = 0.3f;
        private Action? _pendingConfirmAction;
        private FrameworkElement? _draggingFloatingWindow;
        private Point _floatingDragMouseStart;
        private Point _floatingDragElementStart;
        private Border? _dragSourceCard;
        private ClockDisplayMode _clockDisplayMode = ClockDisplayMode.Both;
        // タイムスタンプの出典表示: false=ⓘクリック、true=時刻の右に常時表示
        private bool _timestampSourceAlwaysVisible = false;
        private const double DesignWidth = 1920.0;
        private const double DesignHeight = 1080.0;
        private readonly List<TaskItem> _tasks = new();
        private DateTime _taskCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private readonly List<TimestampItem> _timestamps = new();
        private readonly List<TimestampBox> _timestampBoxes = new();
        private bool _timestampsLoaded = false;
        private bool _timestampLoadFailed = false;
        private string? _activeTimestampBoxId;
        private List<TimestampBox>? _timestampEditorBoxes;
        private string _timestampEditorTitle = "タイムスタンプ";
        private Window? _timestampEditorWindow;
        private bool _timestampEditorReturnToMain;
        private Border? _floatingTimestampBoxPopup;
        private TimestampBox? _floatingTimestampBoxSource;
        private Window? _floatingTimestampBoxWindow;
        private readonly HashSet<string> _poppedOutTimestampBoxIds = new();
        private readonly DispatcherTimer _hanshinScoreTimer;
        private bool _isHanshinScoreUpdating;
        private string _lastHanshinScore = "阪神 試合速報";
        private DateTime _obsApiReadyAt = DateTime.MinValue;
        private static readonly string AppSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OSAKA", "settings.json");
        private Border? _draggingFloatingMemo;
        private Point _floatingMemoMouseStart;
        private double _floatingMemoStartLeft;
        private double _floatingMemoStartTop;
        private readonly HashSet<Border> _integratingMemos = new();
        private Border? _floatingAllMemoPopup;
        private Panel? _floatingAllMemoSourcePanel;
        private StackPanel? _floatingAllMemoContentPanel;
        private Window? _floatingAllMemoWindow;
        private bool _isDraggingAllMemoPopup;
        private Point _allMemoDragMouseStart;
        private double _allMemoDragStartLeft;
        private double _allMemoDragStartTop;

        private Border? _draggingFloatingTimestampBox;
        private Point _floatingTimestampBoxMouseStart;
        private double _floatingTimestampBoxStartLeft;
        private double _floatingTimestampBoxStartTop;

        private Border? _floatingAllTimestampPopup;
        private List<TimestampBox>? _floatingAllTimestampBoxes;

        private bool _isDraggingAllTimestampPopup;
        private FrameworkElement? _floatingAllTimestampDragHeader;
        private Point _allTimestampDragMouseStart;
        private double _allTimestampDragStartLeft;
        private double _allTimestampDragStartTop;
        private string? _currentRecordingFileName;
        private DateTime? _recordingStartedAt;
        private bool _isDraggingTimestampEditorPopup;
        private Point _timestampEditorPopupDragStartMouse;
        private double _timestampEditorPopupStartX;
        private double _timestampEditorPopupStartY;
        private bool _wasRecording;

        private enum ListMemoFilterMode
        {
            All,
            Checked1,
            Checked2,
            CheckedBoth,
            CheckedOnly1,
            CheckedOnly2
        }

        private ListMemoFilterMode _listMemoFilterMode = ListMemoFilterMode.All;
        private string _textMemoSearchText = string.Empty;
        private string _listMemoSearchText = string.Empty;
        private string _timestampSearchText = string.Empty;

        // フォルダ
        private readonly List<FolderData> _textFolders = new();
        private readonly List<FolderData> _listFolders = new();
        private readonly List<FolderData> _timestampFolders = new();
        private string _activeTextFolderId = string.Empty;
        private string _activeListFolderId = string.Empty;
        private string _activeTimestampFolderId = string.Empty;

        // タイムスタンプの出典フィルター。空なら全て表示。
        private readonly HashSet<string> _timestampFilterLiveUrls = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _timestampFilterRecordingFiles = new(StringComparer.OrdinalIgnoreCase);
        private Button? _timestampFilterButton;
        private bool _timestampFilterButtonInitialized;
        private readonly Dictionary<Border, string> _memoFolderIds = new();
        private bool _memosLoaded = false;
        private bool _isLoadingMemos = false;
        // メモの読み込みに失敗した状態で、起動時の初期メモ作成や終了時保存が
        // 既存のmemos.jsonを空の状態で上書きしないようにする。
        private bool _memoFileWasMissingOnLoad = false;
        private Popup? _folderPopup;
        private string _folderPopupType = string.Empty;

        // メモショートカット用のグローバルホットキー。
        private const int MemoShortcutHotKeyId = 0x4F53;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private string _memoShortcutKey = "Ctrl+Shift+M";
        private bool _memoShortcutRegistered;
        private Action? _floatingAllTimestampPopupRefreshAction;
        private Action? _timestampEditorWindowRefreshAction;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public ObservableCollection<NotificationItem> Notifications { get; } = new ObservableCollection<NotificationItem>();

        public MainWindow()
        {
            EnsureUserDataDirectory();

            InitializeComponent();
            UpdateTimestampSourceDisplayModeButton();
            UpdateFolderNameLabels();
            _isBackgroundMode = App.IsBackgroundStartup;
            if (_isBackgroundMode)
            {
                Opacity = 0;
                ShowInTaskbar = false;
                WindowState = WindowState.Minimized;
            }

            InitializeTrayIcon();
            LoadUrotaCursors();
            NotificationList.ItemsSource = Notifications;

            _obs = new OBSWebsocket();

            _obs.Connected += (s, e) =>
            {
                _isObsConnected = true;
                _lastObsConnectionSuccess = DateTime.Now;
                _obsReconnectDelayMs = 1000;

                // WebSocket接続直後は、少し待ってからOBS APIを使用する
                _obsApiReadyAt = DateTime.UtcNow.AddMilliseconds(500);

                System.Diagnostics.Debug.WriteLine(
                    "OBS WebSocket接続成功");
            };


            _obs.Disconnected += (s, e) =>
            {
                _isObsConnected = false;
                _obsMicInputName = string.Empty;
                _obsApiReadyAt = DateTime.MaxValue;

                System.Diagnostics.Debug.WriteLine("OBS WebSocket切断");

                // OBSから状態を取得できないので、
                // 古いミュート状態を画面に残さない
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetMuteMovieVisible(false);
                    SetRecordingElapsedText(null);
                }));

            };



            LocalServer.OnSpecialChat += (type, author, message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (type == "superchat")
                    {
                        // ★ONのときだけ表示
                        if (ChkEnableSuperChatNotification.IsChecked == true)
                        {
                            int amount = ExtractAmount(message);
                            Color bgColor = GetSuperChatColor(amount);
                            TimeSpan duration = GetSuperChatDuration(amount);

                            ShowEventNotification(
                                "Super Chat",
                                $"{author}: {message}",
                                bgColor,
                                duration,
                                ChkNotificationSoundEnabled.IsChecked == true ? GetAudioPath("coin05.mp3"): null);
                        }
                    }
                    else if (type == "member")
                    {
                        if (ChkEnableMemberNotification.IsChecked == true)
                        {
                            int giftCount = ExtractGiftCount(message);

                            string header;
                            string displayMessage;
                            string soundFileName;

                            if (giftCount > 0)
                            {
                                // メンバーシップギフト通知の設定
                                header = "Member Gift!";
                                displayMessage = $"{author}さんが {giftCount} 個ギフトしました！";
                                soundFileName = "fanfare.wav"; // ★ギフト用の効果音ファイル名に変更
                            }
                            else
                            {
                                // 通常のメンバー加入・継続通知の設定
                                header = "New Member!";
                                displayMessage = $"{author}: {message}";
                                soundFileName = "1up3.ogg"; // ★通常メンバーシップ用の効果音
                            }

                            ShowEventNotification(
                                header,
                                displayMessage,
                                Color.FromRgb(15, 157, 88),
                                TimeSpan.FromMinutes(1),
                                ChkNotificationSoundEnabled.IsChecked == true ? GetAudioPath(soundFileName) : null,
                                volume: 0.8f);
                        }
                    }
                });
            };

            // 時計の初期化とタイマー設定
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // 阪神スコア更新タイマー
            _hanshinScoreTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };

            _hanshinScoreTimer.Tick += async (s, e) =>
            {
                await UpdateHanshinScoreAsync();
            };

            _hanshinScoreTimer.Start();

            _obsMuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _obsMuteTimer.Tick += ObsMuteTimer_Tick;

            _taskDueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _taskDueTimer.Tick += TaskDueTimer_Tick;



            // 初回表示用
            UpdateClock();
            _ = UpdateHanshinScoreAsync();
            LoadAppSettings();
            LoadMemos();
            LoadTasks();
            LoadTimestampFolders();
            LoadTimestamps();
            RenderTimestamps();
            ApplyClockDisplayMode();
            ApplyCursorMode();
            
            this.Loaded += Window_Loaded;
            this.Closing += MainWindow_Closing;
            this.Closed += Window_Closed;
            this.SizeChanged += MainWindow_SizeChanged;
            this.SourceInitialized += MainWindow_SourceInitialized;
        }



        private void InitializeTrayIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "logo.ico");
                var icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application;
                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "OSAKA",
                    Visible = true,
                    ContextMenuStrip = new Forms.ContextMenuStrip()
                };
                _notifyIcon.ContextMenuStrip.Items.Add("表示", null, (_, _) => Dispatcher.Invoke(ShowMainWindowFromTray));
                _notifyIcon.ContextMenuStrip.Items.Add("終了", null, (_, _) => Dispatcher.Invoke(ShutdownFromTray));
                _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindowFromTray);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray icon failed: {ex.Message}");
            }
        }

        public void ShowMainWindowFromExternalRequest()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(ShowMainWindowFromExternalRequest),
                    System.Windows.Threading.DispatcherPriority.Normal);

                return;
            }

            _isBackgroundMode = false;

            ShowInTaskbar = true;
            Opacity = 1;

            // まず非表示状態を解除
            Show();

            // 保存されている通常のウィンドウモードを適用
            ApplyWindowMode();

            // 最小化状態を確実に解除
            WindowState = WindowState.Normal;

            // Windows側が起動処理後にMinimizedを再適用する場合があるため、
            // UI処理が一段落した後にもNormalを設定する
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_isBackgroundMode)
                        return;

                    ShowInTaskbar = true;
                    Opacity = 1;
                    WindowState = WindowState.Normal;

                    Activate();

                    // 確実に前面へ
                    Topmost = true;
                    Topmost = false;
                    Activate();
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void ShowMainWindowFromTray()
        {
            ShowMainWindowFromExternalRequest();
        }

        private void ShutdownFromTray()
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            Application.Current.Shutdown();
        }

        private void ShowDesktopNotification(string title, string message)
        {
            try
            {
                _notifyIcon?.ShowBalloonTip(10000, title, message, Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Desktop notification failed: {ex.Message}");
            }
        }

        private void LoadUrotaCursors()
        {
            _urotaNormalCursor = LoadCursorFromFile("うろたカーソル通常.ani");
            _urotaHandCursor = LoadCursorFromFile("うろたカーソルリンク選択.ani");
            _urotaTextCursor = LoadCursorFromFile("うろたカーソルテキスト.ani");
        }

        private Cursor? LoadCursorFromFile(string fileName)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cursor", fileName);
                return File.Exists(path) ? new Cursor(path) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cursor load failed ({fileName}): {ex.Message}");
                return null;
            }
        }

        private void ApplyCursorMode()
        {
            EnsureCursorHook();
            ClearCursorOverrides();

            if (!_useUrotaCursor || _urotaNormalCursor == null)
            {
                Cursor = null;
                Mouse.OverrideCursor = null;
                return;
            }

            Cursor = _urotaNormalCursor;
            Mouse.OverrideCursor = _urotaNormalCursor;
        }

        private void EnsureCursorHook()
        {
            if (_cursorHookInstalled)
            {
                return;
            }

            AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(UrotaCursorPreviewMouseMove), true);
            MouseLeave += (_, _) =>
            {
                if (_useUrotaCursor)
                {
                    Mouse.OverrideCursor = _urotaNormalCursor;
                }
            };
            _cursorHookInstalled = true;
        }

        private void ClearCursorOverrides()
        {
            ClearValue(CursorProperty);
            foreach (var element in this.Descendants().OfType<FrameworkElement>())
            {
                if (ReferenceEquals(element.Cursor, _urotaNormalCursor) ||
                    ReferenceEquals(element.Cursor, _urotaHandCursor) ||
                    ReferenceEquals(element.Cursor, _urotaTextCursor))
                {
                    element.ClearValue(CursorProperty);
                }
            }
        }

        private void UrotaCursorPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_useUrotaCursor || _urotaNormalCursor == null)
            {
                return;
            }

            Mouse.OverrideCursor = ResolveUrotaCursor(e.OriginalSource as DependencyObject);
        }

        private Cursor ResolveUrotaCursor(DependencyObject? source)
        {
            for (var current = source; current != null; current = GetCursorParent(current))
            {
                if (current is TextBoxBase or PasswordBox)
                {
                    return _urotaTextCursor ?? Cursors.IBeam;
                }

                if (current is ButtonBase or Slider or ComboBox or Hyperlink)
                {
                    return _urotaHandCursor ?? Cursors.Hand;
                }

                if (current is FrameworkElement { Cursor: var cursor } && cursor == Cursors.Hand)
                {
                    return _urotaHandCursor ?? Cursors.Hand;
                }
            }

            return _urotaNormalCursor ?? Cursors.Arrow;
        }

        private static DependencyObject? GetCursorParent(DependencyObject current)
        {
            if (current is Visual or Visual3D)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent != null)
                {
                    return parent;
                }
            }

            return LogicalTreeHelper.GetParent(current);
        }

        private void SetCursorModeRadioSelection()
        {
            RbUrotaCursor.IsChecked = _useUrotaCursor;
            RbDefaultCursor.IsChecked = !_useUrotaCursor;
        }

        private void CursorModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _useUrotaCursor = RbUrotaCursor?.IsChecked == true;
            ApplyCursorMode();
        }

        private void ApplyWindowMode()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            WindowRoot.Stretch = Stretch.Uniform;

            if (_isBackgroundMode)
            {
                return;
            }

            if (_isWindowedMode)
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = _fixedWindow1920 ? ResizeMode.CanMinimize : ResizeMode.CanResizeWithGrip;
                if (_fixedWindow1920)
                {
                    Width = 1920;
                    Height = 903;

                }
                else if (Width <= 0 || Height <= 0 || WindowState == WindowState.Maximized)
                {
                    Width = 1280;
                    Height = 720;
                }
                Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left);
                Top = Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2 + SystemParameters.WorkArea.Top);
                return;
            }

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            RegisterMemoShortcut();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == MemoShortcutHotKeyId)
            {
                Dispatcher.BeginInvoke(new Action(HandleMemoShortcut));
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void TxtMemoShortcutKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.None || key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == ModifierKeys.None)
            {
                e.Handled = true;
                return;
            }

            _memoShortcutKey = BuildShortcutText(modifiers, key);
            TxtMemoShortcutKey.Text = _memoShortcutKey;
            RegisterMemoShortcut();
            SaveAppSettings();
            e.Handled = true;
        }

        private static string BuildShortcutText(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private bool TryParseMemoShortcut(out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            string text = string.IsNullOrWhiteSpace(_memoShortcutKey) ? "Ctrl+Shift+M" : _memoShortcutKey;
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return false;
            if (!Enum.TryParse<Key>(parts[^1], true, out var key)) return false;

            foreach (var part in parts.Take(parts.Length - 1))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= MOD_CONTROL; break;
                    case "alt": modifiers |= MOD_ALT; break;
                    case "shift": modifiers |= MOD_SHIFT; break;
                    case "win":
                    case "windows": modifiers |= MOD_WIN; break;
                    default: return false;
                }
            }

            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return modifiers != 0 && vk != 0;
        }

        private void RegisterMemoShortcut()
        {
            if (!IsInitialized) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            UnregisterHotKey(hwnd, MemoShortcutHotKeyId);
            _memoShortcutRegistered = false;
            if (!TryParseMemoShortcut(out uint modifiers, out uint vk)) return;

            _memoShortcutRegistered = RegisterHotKey(hwnd, MemoShortcutHotKeyId, modifiers | MOD_NOREPEAT, vk);
            if (!_memoShortcutRegistered)
                System.Diagnostics.Debug.WriteLine($"メモショートカットの登録に失敗しました: {_memoShortcutKey}");
        }

        private void UnregisterMemoShortcut()
        {
            if (!_memoShortcutRegistered) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, MemoShortcutHotKeyId);
            _memoShortcutRegistered = false;
        }

        private bool IsMemoOrAllTimestampPopupOpen()
        {
            return MemoPopup.Visibility == Visibility.Visible ||
                   TimestampEditorPopup.Visibility == Visibility.Visible ||
                   _floatingAllTimestampPopup != null ||
                   _timestampEditorWindow != null;
        }

        private static string NormalizeLiveUrlForComparison(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var value = url.Trim();

            // URLの末尾の / や空白による「同じ配信なのに別URL」判定を防ぐ。
            value = value.TrimEnd('/');

            return value;
        }

        private void HandleMemoShortcut()
        {
            if (!IsMemoOrAllTimestampPopupOpen()) return;

            EnsureTimestampBox();

            // ★が付いたBOXがあれば、ショートカットからの追加先として最優先する。
            // 配信/録画やBOXの並び順に関係なく、このBOXへ直接追加する。
            var shortcutTargetBox = _timestampBoxes.FirstOrDefault(b => b.IsShortcutTarget);
            if (shortcutTargetBox != null)
            {
                AddTimestampItemToBox(shortcutTargetBox);
                SaveTimestamps();
                RefreshTimestampPopupViews();
                RenderTimestamps();
                return;
            }

            // 「現在の配信」を基準にする。
            // ラジオボタンの状態だけに依存せず、実際に現在の配信URLが取得できているかも確認する。
            bool isLiveTimestamp =
                RbTimestampLive.IsChecked == true &&
                !string.IsNullOrWhiteSpace(_currentLiveUrl) &&
                _liveStartedAt != null;

            var topBox = GetTopTimestampBoxForActiveFolder();

            string currentLiveUrl = NormalizeLiveUrlForComparison(_currentLiveUrl);
            string topLiveUrl = NormalizeLiveUrlForComparison(topBox.LiveUrl);

            bool topHasRecording = !string.IsNullOrWhiteSpace(topBox.RecordingFileName);
            bool topHasLiveUrl = !string.IsNullOrWhiteSpace(topLiveUrl);
            bool sameLiveUrl =
                isLiveTimestamp &&
                !string.IsNullOrEmpty(currentLiveUrl) &&
                !string.IsNullOrEmpty(topLiveUrl) &&
                string.Equals(topLiveUrl, currentLiveUrl, StringComparison.OrdinalIgnoreCase);

            // ========================================================
            // 配信ショートカット
            // ========================================================
            if (isLiveTimestamp)
            {
                // 一番上のBOXが録画BOXなら、新しい配信用BOXを作る。
                // 配信BOXの場合は「現在の配信URLと同じか」を必ず比較する。
                // URLが違う配信なら、同じBOXを再利用せず新しいBOXを作る。
                // 出典未設定のBOXだけは、現在の配信のBOXとして再利用する。
                if (!topHasRecording && (!topHasLiveUrl || sameLiveUrl))
                {
                    AddTimestampItemToBox(topBox);
                    SaveTimestamps();
                    RefreshTimestampPopupViews();
                    RenderTimestamps();
                    return;
                }

                // 録画BOX、または別の配信URLのBOXなら新しい配信用BOXを作る。
                var newLiveBox = new TimestampBox
                {
                    Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0,
                    RecordingFileName = string.Empty,
                    LiveUrl = currentLiveUrl
                };

                foreach (var box in _timestampBoxes)
                    box.Order++;

                _timestampBoxes.Insert(0, newLiveBox);
                NormalizeTimestampBoxOrder();

                AddTimestampItemToBox(newLiveBox);

                SaveTimestamps();
                RefreshTimestampPopupViews();
                RenderTimestamps();
                return;
            }

            // ========================================================
            // 録画ショートカット
            // ========================================================
            bool topMatchesCurrentRecording =
                topHasRecording &&
                !string.IsNullOrWhiteSpace(_currentRecordingFileName) &&
                string.Equals(
                    topBox.RecordingFileName.Trim(),
                    _currentRecordingFileName.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            // 配信URLが設定されたBOXには録画タイムスタンプを追加しない。
            // 一番上が配信BOXなら、新しい録画BOXを作る。
            if (topHasLiveUrl)
            {
                var newRecordingBox = new TimestampBox
                {
                    Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0,
                    RecordingFileName = _currentRecordingFileName ?? string.Empty,
                    LiveUrl = string.Empty
                };

                foreach (var box in _timestampBoxes)
                    box.Order++;

                _timestampBoxes.Insert(0, newRecordingBox);
                NormalizeTimestampBoxOrder();
                AddTimestampItemToBox(newRecordingBox);
            }
            // 録画ファイルが設定されていないBOXは、録画タイムスタンプ用として再利用できる。
            // ただし配信URL付きBOXは上の分岐で除外済み。
            else if (!topHasRecording || topMatchesCurrentRecording)
            {
                AddTimestampItemToBox(topBox);
            }
            else
            {
                // 別の録画BOXなら新しい録画BOXを作る。
                var newRecordingBox = new TimestampBox
                {
                    Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0,
                    RecordingFileName = _currentRecordingFileName ?? string.Empty,
                    LiveUrl = string.Empty
                };

                foreach (var box in _timestampBoxes)
                    box.Order++;

                _timestampBoxes.Insert(0, newRecordingBox);
                NormalizeTimestampBoxOrder();
                AddTimestampItemToBox(newRecordingBox);
            }

            SaveTimestamps();
            RefreshTimestampPopupViews();
            RenderTimestamps();
        }

        private void AddTimestampItemToBox(TimestampBox box)
        {
            _activeTimestampBoxId = box.Id;

            // 出典は「この瞬間のタイムスタンプ作成元」だけを設定する。
            // 重要：BOXに既に入っているLiveUrl/RecordingFileNameを
            // 新しいTimestampItemへ引き継がない。
            // これにより、録画も配信もしていない状態で★BOXへ追加しても、
            // 同じBOX内の別タイムスタンプのⓘ情報が勝手にコピーされない。
            string liveUrl = string.Empty;
            string recordingFileName = string.Empty;

            // 実際に現在の配信が接続中なら、その配信URLを付ける。
            bool hasActiveLive =
                RbTimestampLive.IsChecked == true &&
                !string.IsNullOrWhiteSpace(_currentLiveUrl) &&
                _liveStartedAt != null;

            // 実際にOBSが録画中なら、現在の録画ファイル名を付ける。
            // _currentRecordingFileName が残っていても、OBSが録画中でなければ
            // 出典として使用しない。
            bool hasActiveRecording =
                !string.IsNullOrWhiteSpace(_currentRecordingFileName) &&
                GetObsRecordingText() != null;

            if (hasActiveLive)
            {
                liveUrl = _currentLiveUrl!.Trim();
            }
            else if (hasActiveRecording)
            {
                recordingFileName = _currentRecordingFileName!.Trim();
            }

            // 出典は必ずどちらか一方だけにする。
            if (!string.IsNullOrWhiteSpace(liveUrl))
                recordingFileName = string.Empty;
            else if (!string.IsNullOrWhiteSpace(recordingFileName))
                liveUrl = string.Empty;

            box.Items.Insert(0, new TimestampItem
            {
                Time = GetCurrentTimestampText(),
                Body = string.Empty,
                IsChecked = false,
                LiveUrl = liveUrl,
                RecordingFileName = recordingFileName
            });

            box.IsCollapsed = false;
        }

        private void RefreshTimestampPopupViews()
        {
            if (TimestampEditorPopup.Visibility == Visibility.Visible)
                RefreshTimestampEditorInMainWindow();
            _floatingAllTimestampPopupRefreshAction?.Invoke();
            _timestampEditorWindowRefreshAction?.Invoke();
        }

        private static void ApplySizingAspectRatio(int edge, ref ResizeRect rect)
        {
            const double ratio = 16.0 / 9.0;
            var width = Math.Max(640, rect.Right - rect.Left);
            var height = Math.Max(360, rect.Bottom - rect.Top);

            switch (edge)
            {
                case 1:
                case 2:
                    width = (int)Math.Round(height * ratio);
                    if (edge == 1) rect.Left = rect.Right - width;
                    else rect.Right = rect.Left + width;
                    break;
                case 3:
                    height = (int)Math.Round(width / ratio);
                    rect.Top = rect.Bottom - height;
                    break;
                case 4:
                case 5:
                    height = (int)Math.Round(width / ratio);
                    rect.Top = rect.Bottom - height;
                    break;
                case 6:
                case 7:
                case 8:
                    height = (int)Math.Round(width / ratio);
                    rect.Bottom = rect.Top + height;
                    break;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ResizeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }



        private void SetWindowModeRadioSelection()
        {
            RbWindowedMode.IsChecked = _isWindowedMode && !_fixedWindow1920;
            RbWindowed1920Mode.IsChecked = _isWindowedMode && _fixedWindow1920;
            RbFullscreenMode.IsChecked = !_isWindowedMode;
        }

        private void WindowModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _fixedWindow1920 = RbWindowed1920Mode?.IsChecked == true;
            _isWindowedMode = RbWindowedMode?.IsChecked == true || _fixedWindow1920;
            ApplyWindowMode();
        }

        private void TimestampSourceDisplayModeButton_Click(object sender, RoutedEventArgs e)
        {
            _timestampSourceAlwaysVisible = !_timestampSourceAlwaysVisible;
            UpdateTimestampSourceDisplayModeButton();
            RefreshTimestampPopupViews();
            RenderTimestamps();
            SaveAppSettings();
        }

        private void UpdateTimestampSourceDisplayModeButton()
        {
            if (TimestampSourceDisplayModeButton == null)
                return;

            TimestampSourceDisplayModeButton.Content =
                _timestampSourceAlwaysVisible
                    ? "ⓘ 出典:常に表示"
                    : "ⓘ 出典:クリック";

            TimestampSourceDisplayModeButton.ToolTip =
                _timestampSourceAlwaysVisible
                    ? "クリック表示に切り替え"
                    : "常時表示に切り替え";
        }

        // 16:9固定デザインのため、余白補正レイアウト処理は省略しています。

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateClock();
        }

        private void UpdateClock()
        {
            // 時間のフォーマットはここで変更可能（例: "HH:mm"など）
            var now = DateTime.Now;
            DigitalClock.Text = now.ToString("HH:mm:ss");
            
            // 日本語の曜日を取得
            string[] dayOfWeekNames = { "日", "月", "火", "水", "木", "金", "土" };
            string dayOfWeek = dayOfWeekNames[(int)now.DayOfWeek];

            DigitalDate.Text = $"{now.Year}年{now.Month}月{now.Day}日({dayOfWeek})";
            UpdateLiveElapsedClock();
        }

        private async Task UpdateHanshinScoreAsync()
        {
            if (_isHanshinScoreUpdating)
                return;

            _isHanshinScoreUpdating = true;

            try
            {
                string score = await GetHanshinScoreAsync();

                string[] parts = score.Split('-');

                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out int hanshinScore) &&
                    int.TryParse(parts[1].Trim(), out int opponentScore))
                {
                    HanshinScoreHome.Text = hanshinScore.ToString();
                    HanshinScoreAway.Text = opponentScore.ToString();

                    // 勝敗に応じた阪神スコアの文字色変更
                    if (hanshinScore > opponentScore)
                    {
                        // 勝っている時：赤色
                        HanshinScoreHome.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cf4141"));
                    }
                    else if (hanshinScore < opponentScore)
                    {
                        // 負けている時：青色
                        HanshinScoreHome.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3752f4"));
                    }
                    else
                    {
                        // 同点（または初期状態）：デフォルト色 (#aeb8e2)
                        HanshinScoreHome.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#aeb8e2"));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"阪神スコア取得失敗: {ex.Message}"
                );

                // 通信失敗時は前回のスコアを維持
                if (!string.IsNullOrEmpty(_lastHanshinScore))
                {
                    string[] parts = _lastHanshinScore.Split('-');

                    if (parts.Length == 2)
                    {
                        HanshinScoreHome.Text = parts[0].Trim();
                        HanshinScoreAway.Text = parts[1].Trim();
                    }
                }
            }
            finally
            {
                _isHanshinScoreUpdating = false;
            }
        }

        private async Task<string> GetHanshinScoreAsync()
        {
            const string url =
                "https://score.hanshintigers.jp/game/score/progress/index.html";

            using var client = new HttpClient();

            client.Timeout = TimeSpan.FromSeconds(10);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/150.0.0.0 Safari/537.36"
            );

            string html = await client.GetStringAsync(url);

            // HTMLタグを削除
            string text = Regex.Replace(
                html,
                "<[^>]+>",
                " "
            );

            // HTML特殊文字を通常文字に戻す
            text = System.Net.WebUtility.HtmlDecode(text);

            // 空白を整理
            text = Regex.Replace(
                text,
                @"\s+",
                " "
            );

            /*
             * 阪神公式速報のスコアは、
             *
             * DB4-2Ｔ
             * Ｔ0-1DB
             *
             * のような形式。
             *
             * Ｔ = 阪神
             */

            MatchCollection matches = Regex.Matches(
                text,
                @"(?:Ｔ|T)(\d+)-(\d+)([^\d\s-]+)"
                + @"|"
                + @"([^\d\s-]+)(\d+)-(\d+)(?:Ｔ|T)"
            );

            if (matches.Count == 0)
            {
                return "阪神 試合なし";
            }

            // 試合経過の最後に登場したスコアを使用
            Match lastMatch = matches[matches.Count - 1];

            int hanshinScore;
            int opponentScore;

            if (lastMatch.Groups[1].Success)
            {
                // Ｔ0-1DB
                hanshinScore = int.Parse(lastMatch.Groups[1].Value);
                opponentScore = int.Parse(lastMatch.Groups[2].Value);
            }
            else
            {
                // DB4-2Ｔ
                opponentScore = int.Parse(lastMatch.Groups[5].Value);
                hanshinScore = int.Parse(lastMatch.Groups[6].Value);
            }

            return $"{hanshinScore} - {opponentScore}";
        }

        private void UpdateLiveElapsedClock()
        {
            if (_liveStartedAt == null)
            {
                LiveElapsedClock.Text = "";
                LiveElapsedClock.Visibility = Visibility.Collapsed;
                SetLiveOn(false);
                UpdateTimestampSourceAvailability();
                return;
            }

            var elapsed = DateTimeOffset.Now - _liveStartedAt.Value;

            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            LiveElapsedClock.Text = elapsed.TotalHours >= 100
                ? $"{(int)elapsed.TotalHours:000}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            LiveElapsedClock.Visibility = Visibility.Visible;

            SetLiveOn(true);

            UpdateTimestampSourceAvailability();
        }

        private void BtnShowDateOnly_Click(object sender, RoutedEventArgs e)
        {
            _clockDisplayMode = ClockDisplayMode.DateOnly;
            ApplyClockDisplayMode();
        }

        private void BtnShowClockOnly_Click(object sender, RoutedEventArgs e)
        {
            _clockDisplayMode = ClockDisplayMode.ClockOnly;
            ApplyClockDisplayMode();
        }

        private void BtnShowDateAndClock_Click(object sender, RoutedEventArgs e)
        {
            _clockDisplayMode = ClockDisplayMode.Both;
            ApplyClockDisplayMode();
        }

        private void ApplyClockDisplayMode()
        {
            switch (_clockDisplayMode)
            {
                case ClockDisplayMode.DateOnly:
                    DigitalDate.Visibility = Visibility.Visible;
                    DigitalClock.Visibility = Visibility.Collapsed;
                    DigitalDate.Margin = new Thickness(0, 80, 60, 0);
                    DigitalDate.FontSize = 45;
                    break;
                case ClockDisplayMode.ClockOnly:
                    DigitalDate.Visibility = Visibility.Collapsed;
                    DigitalClock.Visibility = Visibility.Visible;
                    DigitalClock.Margin = new Thickness(0, 80, 70, 0);
                    break;
                default:
                    DigitalDate.Visibility = Visibility.Visible;
                    DigitalClock.Visibility = Visibility.Visible;
                    DigitalDate.Margin = new Thickness(0, 60, 160, 0);
                    DigitalDate.FontSize = 30;
                    DigitalClock.Margin = new Thickness(0, 100, 70, 0);
                    break;
            }
        }

        private void BackgroundMedia_MediaEnded(object sender, RoutedEventArgs e) => RestartMedia(BackgroundMedia);
        private void BackgroundMediaAlt_MediaEnded(object sender, RoutedEventArgs e) => RestartMedia(BackgroundMediaAlt);
        private void MuteMedia_MediaEnded(object sender, RoutedEventArgs e) => RestartMedia(MuteMedia);
        private void MuteMediaAlt_MediaEnded(object sender, RoutedEventArgs e) => RestartMedia(MuteMediaAlt);

        private static void RestartMedia(MediaElement media)
        {
            media.Position = TimeSpan.FromMilliseconds(1);
            media.Play();
        }

        private void BtnPopOutTextMemos_Click(object sender, RoutedEventArgs e)
        {
            ShowAllMemoPopupInMainWindow($"テキストメモ  [{GetFolderName("text", _activeTextFolderId)}]", TextMemosPanel);
        }

        private void BtnPopOutListMemos_Click(object sender, RoutedEventArgs e)
        {
            ShowAllMemoPopupInMainWindow($"チェックリストメモ  [{GetFolderName("list", _activeListFolderId)}]", ListMemosPanel);
        }



        private bool IsMemoInActiveFolder(Border memo, Panel sourcePanel)
        {
            string type = ReferenceEquals(sourcePanel, TextMemosPanel) ? "text" : "list";
            string folderId = _memoFolderIds.TryGetValue(memo, out var id) ? id : string.Empty;
            return IsFolderVisible(type, folderId);
        }

        private void ShowAllMemoPopupInMainWindow(string title, Panel sourcePanel)
        {
            if (_floatingAllMemoPopup != null)
                return;

            _floatingAllMemoSourcePanel = sourcePanel;

            var contentPanel = new StackPanel();
            _floatingAllMemoContentPanel = contentPanel;

            // 現在選択中のフォルダーに属するカードだけをポップアップへ移す。
            // 選択中フォルダー以外のカードは元パネルに残す。
            var children = sourcePanel.Children.Cast<UIElement>()
                .Where(child => child is Border border &&
                                IsMemoInActiveFolder(border, sourcePanel))
                .ToList();

            contentPanel.Children.Clear();

            foreach (var child in children)
            {
                sourcePanel.Children.Remove(child);
                contentPanel.Children.Add(child);
            }

            contentPanel.UpdateLayout();


            // ★追加：メイン内ポップアップでも個別ボタンを隠す
            SetIndividualPopoutButtonsVisible(contentPanel, false);

            

            var header = CreateAllMemoHeader(title, inMainWindow: true, contentPanel);

            var root = new DockPanel();

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = contentPanel
            });

            _floatingAllMemoPopup = new Border
            {
                Width = 460,
                Height = 640,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = root
            };

            _floatingAllMemoPopup.MouseLeftButtonDown += FloatingAllMemoPopup_MouseLeftButtonDown;
            _floatingAllMemoPopup.MouseMove += FloatingAllMemoPopup_MouseMove;
            _floatingAllMemoPopup.MouseLeftButtonUp += FloatingAllMemoPopup_MouseLeftButtonUp;

            Canvas.SetLeft(_floatingAllMemoPopup, 80);
            Canvas.SetTop(_floatingAllMemoPopup, 80);

            FloatingAllMemoCanvas.Children.Add(_floatingAllMemoPopup);
        }

        private void FloatingAllMemoPopup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_floatingAllMemoPopup == null)
                return;

            // メモカード内の操作は外側ポップアップの移動にしない。
            // 特にドラッグハンドルからの DragDrop と競合すると、
            // 並び替え後の表示更新が不安定になる。
            if (e.OriginalSource is DependencyObject source)
            {
                DependencyObject? current = source;
                while (current != null && !ReferenceEquals(current, _floatingAllMemoPopup))
                {
                    if (current is Border card &&
                        (ReferenceEquals(card.Tag, TextMemosPanel) ||
                         ReferenceEquals(card.Tag, ListMemosPanel)))
                    {
                        e.Handled = true;
                        return;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }

            _isDraggingAllMemoPopup = true;
            _allMemoDragMouseStart = e.GetPosition(FloatingAllMemoCanvas);
            _allMemoDragStartLeft = Canvas.GetLeft(_floatingAllMemoPopup);
            _allMemoDragStartTop = Canvas.GetTop(_floatingAllMemoPopup);

            _floatingAllMemoPopup.CaptureMouse();
            e.Handled = true;
        }

        private void FloatingAllMemoPopup_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingAllMemoPopup || _floatingAllMemoPopup == null)
                return;

            Point current = e.GetPosition(FloatingAllMemoCanvas);

            double newLeft = _allMemoDragStartLeft + (current.X - _allMemoDragMouseStart.X);
            double newTop = _allMemoDragStartTop + (current.Y - _allMemoDragMouseStart.Y);

            Canvas.SetLeft(_floatingAllMemoPopup, newLeft);
            Canvas.SetTop(_floatingAllMemoPopup, newTop);
        }

        private void FloatingAllMemoPopup_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_floatingAllMemoPopup == null)
                return;

            _isDraggingAllMemoPopup = false;
            _floatingAllMemoPopup.ReleaseMouseCapture();
        }

        private Border CreateAllMemoHeader(string title, bool inMainWindow, StackPanel contentPanel)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (inMainWindow)
            {
                var detach = CreateHeaderButton("↗", "別ウィンドウに切り離し");
                detach.Click += (s, e) => ShowAllMemoPopupAsWindow(title, contentPanel);
                panel.Children.Add(detach);
            }
            else
            {
                var integrate = CreateHeaderButton("↙", "メインウィンドウに統合");
                integrate.Click += (s, e) => IntegrateAllMemoPopupToMain(title, contentPanel);
                panel.Children.Add(integrate);
            }

            var close = CreateHeaderButton("□", "ポップアップ解除");
            close.Click += (s, e) => CloseAllMemoPopup(contentPanel);
            panel.Children.Add(close);

            var addButton = CreateHeaderButton("＋", "メモを追加");

            addButton.Click += (s, e) =>
            {
                if (_floatingAllMemoSourcePanel == null)
                    return;

                Border newMemo;

                // テキストメモかチェックリストメモか判定
                if (_floatingAllMemoSourcePanel == TextMemosPanel)
                    newMemo = CreateMemoContainer(ownerPanel: TextMemosPanel);
                else
                    newMemo = CreateListMemoContainer(ownerPanel: ListMemosPanel);

                string memoType = _floatingAllMemoSourcePanel == TextMemosPanel ? "text" : "list";
                _memoFolderIds[newMemo] = string.IsNullOrEmpty(GetActiveFolderId(memoType))
                    ? GetUncategorizedFolderId(memoType)
                    : GetActiveFolderId(memoType);
                AddFolderSelectorToMemoCard(newMemo, memoType);
                contentPanel.Children.Insert(0,newMemo);
                SaveMemos();
            };

            panel.Children.Add(addButton);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            Grid.SetColumn(panel, 1);
            grid.Children.Add(panel);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Padding = new Thickness(8),
                Child = grid
            };
        }

        private void SetIndividualPopoutButtonsVisible(StackPanel contentPanel, bool visible)
        {
            foreach (var border in contentPanel.Children.OfType<Border>())
            {
                var popBtn = border.Descendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

                if (popBtn != null)
                    popBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private Button CreateHeaderButton(string text, string tooltip)
        {
            return new Button
            {
                Content = text,
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private void ShowAllMemoPopupAsWindow(string title, StackPanel contentPanel)
        {
            // ★ 個別ポップアップボタンを隠す
            SetIndividualPopoutButtonsVisible(contentPanel, false);

            if (_floatingAllMemoPopup != null)
            {
                FloatingAllMemoCanvas.Children.Remove(_floatingAllMemoPopup);
                _floatingAllMemoPopup = null;
            }

            if (_floatingAllMemoPopup != null)
            {
                FloatingAllMemoCanvas.Children.Remove(_floatingAllMemoPopup);
                _floatingAllMemoPopup = null;
            }

            var root = new DockPanel();

            var header = CreateAllMemoHeader(title, inMainWindow: false, contentPanel);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = contentPanel
            });

            _floatingAllMemoWindow = new Window
            {
                Title = title,
                Width = 460,
                Height = 640,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                Topmost = ChkPopOutTopmost?.IsChecked ?? true,
                Content = root
            };

            _floatingAllMemoWindow.Show();
        }

        private void IntegrateAllMemoPopupToMain(string title, StackPanel contentPanel)
        {
            // 別ウィンドウの contentPanel が、現在メモカードの論理親になっている。
            // WPFでは、子要素を別のPanelへ移す前に必ず現在の親から切り離す必要がある。
            // ここで Clear() せず sourcePanel.Children.Add(child) を実行すると、
            // 「指定された要素は、既に別の要素の論理子です」が発生する。
            var sourcePanel = _floatingAllMemoSourcePanel;
            if (sourcePanel == null || _floatingAllMemoWindow == null)
                return;

            // 現在表示されている順番をそのまま取得する。
            var children = contentPanel.Children.Cast<UIElement>().ToList();

            // ★最重要：先に contentPanel から完全に切り離す。
            contentPanel.Children.Clear();

            // 元パネル側にも残骸を残さない。
            sourcePanel.Children.Clear();

            // これで各カードのLogical/Visual ParentがcontentPanelから外れたので、
            // 安全に元パネルへ戻せる。順番もchildrenの順番をそのまま維持する。
            foreach (var child in children)
            {
                sourcePanel.Children.Add(child);
            }
            sourcePanel.UpdateLayout();

            // 統合後は通常のメモパネルを保存対象にする。
            _floatingAllMemoContentPanel = null;
            SaveMemos();

            // 別ウィンドウを閉じる。contentPanel自体は空なので、
            // 閉じる際にカードを二重に所有することはない。
            var window = _floatingAllMemoWindow;
            _floatingAllMemoWindow = null;
            window.Close();

            // ここで改めてメインウィンドウ用contentPanelを作り、
            // sourcePanelから現在の順番のカードを移動する。
            ShowAllMemoPopupInMainWindow(title, sourcePanel);
        }

        private void CloseAllMemoPopup(StackPanel contentPanel)
        {
            SetIndividualPopoutButtonsVisible(contentPanel, true);

            var children = contentPanel.Children.Cast<UIElement>().ToList();
            contentPanel.Children.Clear();

            foreach (var border in contentPanel.Children.OfType<Border>())
            {
                var popBtn = border.Descendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

                if (popBtn != null)
                    popBtn.Visibility = Visibility.Visible;
            }

            foreach (var child in children)
                _floatingAllMemoSourcePanel?.Children.Add(child);

            _floatingAllMemoContentPanel = null;
            SaveMemos();
            _floatingAllMemoSourcePanel = null;

            if (_floatingAllMemoPopup != null)
            {
                FloatingAllMemoCanvas.Children.Remove(_floatingAllMemoPopup);
                _floatingAllMemoPopup = null;
            }

            if (_floatingAllMemoWindow != null)
            {
                _floatingAllMemoWindow.Close();
                _floatingAllMemoWindow = null;
            }
        }

        private void BtnIndividualPopOut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            if (FindMemoContainer(btn) is not Border memo)
                return;

            // 個別メモのポップアップを開く場合は、
            // 配信中でも確認ダイアログを表示しない。
            // 確認が必要なのはメモパネル本体を開くときだけ。
            // アプリ内フローティング
            if (memo.Parent == FloatingMemoCanvas)
            {
                ShowMemoAsExternalWindow(memo);
                return;
            }

            // それ以外 → アプリ内フローティング
            ShowMemoAsInternalWindow(memo);
        }

        private void ShowMemoAsExternalWindow(Border memo)
        {
            // 現在の親から切り離す
            if (memo.Parent is Panel panel)
                panel.Children.Remove(memo);
            else if (memo.Parent is ContentControl contentControl)
                contentControl.Content = null;
            else if (memo.Parent is Decorator decorator)
                decorator.Child = null;

            var title = memo.Descendants()
                .OfType<TextBox>()
                .FirstOrDefault()
                ?.Text ?? "メモ";

            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 600,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = ChkPopOutTopmost?.IsChecked ?? true
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Margin = new Thickness(10)
            };

            scroll.Content = memo;
            win.Content = scroll;

            // ボタンは ToolTip や Content ではなく Uid で識別する。
            // これにより「□」が2つある状態でも必ず正しいボタンを操作できる。
            var popoutButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

            var internalCloseButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoInternalCloseButton");

            // 別ウィンドウでは内部ポップアップ解除ボタンを隠す。
            if (internalCloseButton != null)
                internalCloseButton.Visibility = Visibility.Collapsed;

            if (popoutButton != null)
            {
                popoutButton.Click -= BtnIndividualPopOut_Click;
                popoutButton.Click -= ExternalMemoCloseButton_Click;
                popoutButton.Click += ExternalMemoCloseButton_Click;

                // 別ウィンドウを閉じるボタン
                popoutButton.Content = "□";
                popoutButton.ToolTip = "別ウィンドウを閉じる";
                popoutButton.Tag = win;

                // 「↙」統合ボタンを1個だけ追加
                if (popoutButton.Parent is StackPanel buttonStack)
                {
                    var integrateButton = buttonStack.Children
                        .OfType<Button>()
                        .FirstOrDefault(b => b.Uid == "MemoIntegrateButton");

                    if (integrateButton == null)
                    {
                        integrateButton = new Button
                        {
                            Uid = "MemoIntegrateButton",
                            Content = "↙",
                            Width = 24,
                            Height = 24,
                            Background = Brushes.Transparent,
                            Foreground = Brushes.White,
                            BorderThickness = new Thickness(0),
                            Cursor = Cursors.Hand,
                            ToolTip = "アプリ本体のポップアップに統合",
                            Margin = new Thickness(0, 0, 5, 0),
                            Tag = win
                        };
                        integrateButton.Click += ExternalMemoIntegrateButton_Click;
                        buttonStack.Children.Insert(0, integrateButton);
                    }
                    else
                    {
                        integrateButton.Visibility = Visibility.Visible;
                        integrateButton.Tag = win;
                        integrateButton.Click -= ExternalMemoIntegrateButton_Click;
                        integrateButton.Click += ExternalMemoIntegrateButton_Click;
                    }
                }
            }

            win.Closed += (s, e) =>
            {
                // ↙による統合の場合、ShowMemoAsInternalWindow が
                // すでに全ボタン状態を設定済みなので何もしない。
                if (_integratingMemos.Remove(memo))
                    return;

                // 通常の「別ウィンドウを閉じる」処理。
                if (ReferenceEquals(scroll.Content, memo))
                    scroll.Content = null;

                var sourcePanel = memo.Tag as Panel;
                if (sourcePanel != null && !sourcePanel.Children.Contains(memo))
                {
                    memo.Margin = new Thickness(0, 5, 0, 5);
                    memo.ClearValue(Canvas.LeftProperty);
                    memo.ClearValue(Canvas.TopProperty);
                    sourcePanel.Children.Add(memo);
                }

                // 通常状態へ完全に戻す。
                var internalClose = memo.Descendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Uid == "MemoInternalCloseButton");

                if (internalClose != null)
                    internalClose.Visibility = Visibility.Collapsed;

                var button = memo.Descendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

                if (button != null)
                {
                    button.Click -= ExternalMemoCloseButton_Click;
                    button.Click -= BtnIndividualPopOut_Click;
                    button.Click += BtnIndividualPopOut_Click;

                    button.Content = "□";
                    button.ToolTip = "このメモを個別にウィンドウ表示";
                    button.Tag = null;
                }

                // ↙ボタンを必ず削除
                var integrate = memo.Descendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Uid == "MemoIntegrateButton");

                if (integrate?.Parent is StackPanel stack)
                {
                    integrate.Click -= ExternalMemoIntegrateButton_Click;
                    stack.Children.Remove(integrate);
                }
            };

            win.Show();
        }


        private void ExternalMemoIntegrateButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var memo = FindMemoContainer(button);
            if (memo == null)
                return;

            // 二重クリック・二重統合を防止
            if (_integratingMemos.Contains(memo))
                return;

            _integratingMemos.Add(memo);

            // ShowMemoAsInternalWindow 内で現在の Window を取得し、
            // ボタンを「↗」「□」へ戻してから Window を閉じる。
            ShowMemoAsInternalWindow(memo);
        }


        private void ExternalMemoCloseButton_Click(object? sender,RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is Window win)
            {
                win.Close();
            }
        }

        private void ExternalMemoJoinButton_Click(object? sender,RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is Window win)
            {
                win.Close();
            }
        }

        private void PopOutWindow(string title, Panel sourcePanel, UIElement? singleTarget = null)
        { 


            bool isTopmost = ChkPopOutTopmost?.IsChecked ?? true;
            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 600,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = isTopmost
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ===== ヘッダー =====
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Padding = new Thickness(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // 別ウィンドウ切り離しボタン
            var detachButton = new Button
            {
                Content = "↗",
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "別ウィンドウとして切り離し"
            };

            // 同じウィンドウ内に統合ボタン
            var integrateButton = new Button
            {
                Content = "↙",
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "メインウィンドウに統合"
            };

            // ポップアップ解除ボタン
            var closeButton = new Button
            {
                Content = "□",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "ポップアップを閉じる"
            };

            // 「メインウィンドウに統合」= このポップアップを閉じる
            integrateButton.Click += (s, e) => win.Close();

            // 「ポップアップ解除」も閉じる
            closeButton.Click += (s, e) => win.Close();

            // 「別ウィンドウ切り離し」
            // 全メモポップアップをさらに独立したウィンドウへ複製表示
            detachButton.Click += (s, e) =>
            {
                var detached = new Window
                {
                    Title = title + " (切り離し)",
                    Width = win.Width,
                    Height = win.Height,
                    Background = win.Background,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = win.Topmost
                };

                var detachedScroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = "このウィンドウは切り離し表示です。\n必要ならここを実装して複製表示してください。",
                        Foreground = Brushes.White,
                        Margin = new Thickness(20)
                    }
                };

                detached.Content = detachedScroll;
                detached.Show();
            };

            buttonPanel.Children.Add(detachButton);
            buttonPanel.Children.Add(integrateButton);
            buttonPanel.Children.Add(closeButton);

            headerGrid.Children.Add(titleText);
            Grid.SetColumn(buttonPanel, 1);
            headerGrid.Children.Add(buttonPanel);

            header.Child = headerGrid;

            Grid.SetRow(header, 0);
            grid.Children.Add(header);


            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 5, 0, 0) };
            var content = new StackPanel { Margin = new Thickness(10) };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);




            // Move items (or single item)
            List<UIElement> targets;
            if (singleTarget != null)
            {
                targets = new List<UIElement> { singleTarget };
                sourcePanel.Children.Remove(singleTarget);
            }
            else
            {
                targets = sourcePanel.Children.Cast<UIElement>().ToList();
                sourcePanel.Children.Clear();
            }

            foreach (var child in targets)
            {
                if (child is Border b)
                {
                    var popBtn = b.Descendants().OfType<Button>().FirstOrDefault(bt => bt.ToolTip?.ToString() == "このメモを個別にウィンドウ表示");
                    if (popBtn != null) popBtn.Visibility = Visibility.Collapsed;

                    // 削除ボタン
                    var deleteBtn = b.Descendants().OfType<Button>()
                        .FirstOrDefault(bt => bt.Content?.ToString() == "×");

                    if (deleteBtn != null)
                    {
                        deleteBtn.Click += (s, e) =>
                        {
                            // Shiftキーが押されている時だけ削除
                            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                            {
                                content.Children.Remove(b);
                            }
                        };
                    }
                    content.Children.Add(child);
                }

                scroll.Content = content;
                win.Content = grid;

                win.Closing += (s, ev) =>
                {
                    var returnChildren = content.Children.Cast<UIElement>().ToList();
                    content.Children.Clear();
                    foreach (var child in returnChildren)
                    {
                        if (child is Border b)
                        {
                            var popBtn = b.Descendants().OfType<Button>().FirstOrDefault(bt => bt.ToolTip?.ToString() == "このメモを個別にウィンドウ表示");
                            if (popBtn != null) popBtn.Visibility = Visibility.Visible;
                        }
                        sourcePanel.Children.Add(child);
                    }
                };

                win.Show();
            }
        }




        

        // テキストメモと完全に同じ個別ポップアップをリストメモにも適用
        private void BtnIndividualListMemoPopOut_Click(object sender, RoutedEventArgs e)
        {
            BtnIndividualPopOut_Click(sender, e);
        }

        // タイムスタンプ一覧もテキストメモと同じ全体ポップアップ処理を使用
        private void BtnPopOutTimestampBoxes_Click(object sender, RoutedEventArgs e)
        {
            OpenTimestampEditorInMainWindow("タイムスタンプ", _timestampBoxes);
        }

private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveAppSettings();
            StartAutoLiveMonitor();
            SettingsPopup.Visibility = Visibility.Collapsed;
            if (ObsSettingsDetail.Visibility == Visibility.Collapsed)
            {
                ChatOverlayArea.Visibility = Visibility.Visible;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.Visibility = Visibility.Visible;
            ChatOverlayArea.Visibility = Visibility.Hidden;
        }


        private void BtnHowTo_Click(object sender, RoutedEventArgs e)
        {
            var window = new HowToWindow
            {
                Owner = this
            };

            window.Show();
        }
        



        private void BtnPopOutTimestamps_Click(object sender, RoutedEventArgs e)
            => OpenTimestampEditorInMainWindow("タイムスタンプ", _timestampBoxes);

        private void BtnCloseMemoPanel_Click(object sender, RoutedEventArgs e)
        {
            MemoPopup.Visibility = Visibility.Collapsed;
        }

        private static readonly Regex TimestampTimeRegex =
    new Regex(@"^([01]\d|2[0-3]):([0-5]\d):([0-5]\d)$");

        private static readonly Regex TaskTimeRegex =
    new Regex(@"^([01]\d|2[0-3]):([0-5]\d)$");

        private bool _isUpdatingTimestampTimeBox;

        private bool _isTimestampArrowUpdating;

        private void TimestampTimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            // 数字以外は入力不可
            if (!Regex.IsMatch(e.Text, @"^\d$"))
            {
                e.Handled = true;
                return;
            }

            // 末尾では数字を入力しても何も起こさない。
            if (tb.CaretIndex >= 8 && tb.SelectionLength == 0)
            {
                e.Handled = true;
                return;
            }

            // HH:mm:ss の数字部分だけを左から順に上書きする。
            // コロンは自動的に飛ばす。
            int _caret = tb.CaretIndex;

            // 末尾にフォーカスがある場合は、そのまま秒の1の位を上書きする。
            if (_caret > 8)
                _caret = 8;

            if (_caret == 2 || _caret == 5)
                _caret++;

            if (_caret > 7)
                _caret = 0;

            string text = tb.Text;
            if (text.Length != 8)
                text = "00:00:00";

            char[] chars = text.ToCharArray();
            chars[_caret] = e.Text[0];

            int nextCaret = _caret + 1;
            if (nextCaret == 2 || nextCaret == 5)
                nextCaret++;

            // 末尾まで入力した後も先頭へ戻さず、末尾に留める。
            if (nextCaret > 8)
                nextCaret = 8;

            _isUpdatingTimestampTimeBox = true;
            tb.Text = new string(chars);
            tb.CaretIndex = nextCaret;
            _isUpdatingTimestampTimeBox = false;

            e.Handled = true;
        }

        private void TimestampTimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTimestampTimeBox || _isTimestampArrowUpdating)
                return;

            if (sender is not TextBox tb)
                return;

            _isUpdatingTimestampTimeBox = true;

            // 数字だけ取り出す
            string digits = new string(tb.Text.Where(char.IsDigit).ToArray());

            // 最大6桁（HHMMSS）
            if (digits.Length > 6)
                digits = digits.Substring(digits.Length - 6);

            // 右詰めで6桁にする
            digits = digits.PadLeft(6, '0');

            string hh = digits.Substring(0, 2);
            string mm = digits.Substring(2, 2);
            string ss = digits.Substring(4, 2);

            string formatted = $"{hh}:{mm}:{ss}";

            if (tb.Text != formatted)
            {
                tb.Text = formatted;
                tb.CaretIndex = tb.Text.Length; // 常に末尾
            }

            _isUpdatingTimestampTimeBox = false;
        }

        private void TimestampTimeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            string[] parts = tb.Text.Split(':');

            if (parts.Length != 3)
            {
                tb.Text = "00:00:00";
                return;
            }

            int hour = int.TryParse(parts[0], out var h) ? h : 0;
            int minute = int.TryParse(parts[1], out var m) ? m : 0;
            int second = int.TryParse(parts[2], out var s) ? s : 0;

            // 秒補正（93 → 53）
            if (second > 59)
            {
                second = 50 + (second % 10);
            }

            // 分補正（87 → 57）
            if (minute > 59)
            {
                minute = 50 + (minute % 10);
            }

            tb.Text = $"{hour:00}:{minute:00}:{second:00}";
        }

        private void TimestampTimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            // Backspace は固定長の HH:mm:ss を崩さず、直前の数字を 0 にして
            // フォーカスをその位置に残す。TextChanged による末尾移動も発生させない。
            if (e.Key == Key.Back)
            {
                int caret = tb.CaretIndex;
                if (caret > 0)
                {
                    int target = caret - 1;
                    if (target == 2 || target == 5)
                        target--;

                    if (target >= 0 && target < 8 && tb.Text.Length == 8)
                    {
                        char[] chars = tb.Text.ToCharArray();
                        chars[target] = '0';

                        _isTimestampArrowUpdating = true;
                        tb.Text = new string(chars);
                        tb.CaretIndex = target;
                        _isTimestampArrowUpdating = false;
                    }
                }

                e.Handled = true;
                return;
            }

            if (e.Key != Key.Up && e.Key != Key.Down)
                return;

            e.Handled = true;

            int delta = e.Key == Key.Up ? 1 : -1;

            string text = tb.Text;
            if (text.Length != 8)
                return;

            int hh = int.Parse(text.Substring(0, 2));
            int mm = int.Parse(text.Substring(3, 2));
            int ss = int.Parse(text.Substring(6, 2));

            int _caret = tb.CaretIndex;
            int newCaret = _caret;

            // HH -------------------------------------------------
            if (_caret == 0)
            {
                // 左桁
                int tens = (hh / 10 + delta + 10) % 10;
                hh = tens * 10 + hh % 10;
                newCaret = 0;
            }
            else if (_caret == 1)
            {
                // 右桁
                int tens = (hh / 10 + delta + 10) % 10;
                hh = tens * 10 + hh % 10;
                newCaret = 1;
            }
            else if (_caret == 2)
            {
                // HH: の直前 → HH全体
                hh = (hh + delta + 100) % 100;
                newCaret = 2;
            }

            // mm -------------------------------------------------
            else if (_caret == 3)
            {
                // 10の位
                hh = (hh + delta + 100) % 100;
                newCaret = 2;
            }
            else if (_caret == 4)
            {
                // 10の位
                int tens = (mm / 10 + delta + 6) % 6;
                mm = tens * 10 + mm % 10;
                newCaret = 4;
            }
            else if (_caret == 5)
            {
                // mm: の直前 → mm全体
                mm = (mm + delta + 60) % 60;
                newCaret = 5;
            }

            // ss -------------------------------------------------
            else if (_caret == 6)
            {
                // mmの1の位
                mm = (mm + delta + 60) % 60;
                newCaret = 5;
            }
            else if (_caret == 7)
            {
                // 10の位
                int tens = (ss / 10 + delta + 6) % 6;
                ss = tens * 10 + ss % 10;
                newCaret = 7;
            }
            else if (_caret == 8)
            {
                // 末尾 → 秒の1の位を増減
                int ones = (ss % 10 + delta + 10) % 10;
                ss = ss / 10 * 10 + ones;
                newCaret = 8;
            }

            _isTimestampArrowUpdating = true;

            tb.Text = $"{hh:00}:{mm:00}:{ss:00}";
            tb.CaretIndex = newCaret;

            _isTimestampArrowUpdating = false;
        }

        private static void FocusTimestampTimeBoxFromLeftHitArea(DockPanel host, TextBox timeBox, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            // 時刻表示そのものは中央寄せのまま、左側だけ約50px広くクリックできるようにする。
            Point p = e.GetPosition(timeBox);
            if (p.X >= -50 && p.X < 0)
            {
                timeBox.Focus();
                timeBox.CaretIndex = 0;
                e.Handled = true;
            }
        }

        private void TaskTimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            string newText = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                    .Insert(tb.SelectionStart, e.Text);

            // 最大5文字(HH:mm)
            if (newText.Length > 5)
            {
                e.Handled = true;
                return;
            }

            // 数字と:以外は禁止
            if (!Regex.IsMatch(newText, @"^[0-9:]*$"))
            {
                e.Handled = true;
            }
        }

        private bool _isUpdatingTimeBox;

        private void TaskTimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTimeBox)
                return;

            if (sender is not TextBox tb)
                return;

            _isUpdatingTimeBox = true;

            // 数字だけ取り出す
            string digits = new string(tb.Text.Where(char.IsDigit).ToArray());

            // 最大4桁
            if (digits.Length > 4)
                digits = digits.Substring(0, 4);

            // 足りない桁は0で埋める
            digits = digits.PadRight(4, '0');

            int hour = int.Parse(digits.Substring(0, 2));
            int minute = int.Parse(digits.Substring(2, 2));

            // 範囲に丸める
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);

            string formatted = $"{hour:00}:{minute:00}";

            if (tb.Text != formatted)
            {
                int caret = tb.CaretIndex;

                tb.Text = formatted;

                // キャレット位置を維持
                tb.CaretIndex = Math.Min(caret, tb.Text.Length);
            }

            _isUpdatingTimeBox = false;
        }

        private void TaskTimeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            // 00:00～23:59以外なら元に戻す
            if (!TaskTimeRegex.IsMatch(tb.Text))
            {
                tb.Text = DateTime.Now.ToString("HH:mm");
            }
        }

        private void SwapBackgroundMedia()
        {
            RestartMedia(BackgroundMedia);
        }

        private void SwapMuteMedia()
        {
            RestartMedia(MuteMedia);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FitWindowToDesktop();

            // メモはコンストラクタ側で1回だけ読み込む。
            BackgroundMedia.Position = TimeSpan.FromMilliseconds(1);
            BackgroundMedia.Play();
            BackgroundMediaAlt.Visibility = Visibility.Collapsed;
            BackgroundMediaAlt.Position = TimeSpan.FromMilliseconds(1);

            MuteMedia.Position = TimeSpan.FromMilliseconds(1);
            MuteMedia.Visibility = Visibility.Collapsed;
            MuteMediaAlt.Visibility = Visibility.Collapsed;
            MuteMediaAlt.Position = TimeSpan.FromMilliseconds(1);
            ConnectObsWebSocket();
            await StartLocalServerAsync();
            StartAutoLiveMonitor();
            _obsMuteTimer.Start();
            _taskDueTimer.Start();
            CheckDueTasks();
            // 初回起動で保存ファイルがまだ存在しない場合だけ初期メモを作る。
            // 既存memos.jsonの読み込みに失敗した場合は、絶対に空の初期メモで
            // 既存データを上書きしない。
            if (_memosLoaded && _memoFileWasMissingOnLoad)
            {
                bool createdDefaultMemo = false;

                if (TextMemosPanel.Children.Count == 0)
                {
                    var memo = CreateMemoContainer(ownerPanel: TextMemosPanel);
                    _memoFolderIds[memo] = GetUncategorizedFolderId("text");
                    TextMemosPanel.Children.Add(memo);
                    createdDefaultMemo = true;
                }

                if (ListMemosPanel.Children.Count == 0)
                {
                    var listMemo = CreateListMemoContainer(ownerPanel: ListMemosPanel);
                    _memoFolderIds[listMemo] = GetUncategorizedFolderId("list");
                    ListMemosPanel.Children.Add(listMemo);
                    createdDefaultMemo = true;
                }

                if (createdDefaultMemo)
                    SaveMemos();

                _memoFileWasMissingOnLoad = false;
            }
            if (Environment.GetCommandLineArgs().Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            {
                HideToBackground();
            }
        }

        private void HideToBackground()
        {
            _isBackgroundMode = true;
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Opacity = 0;
            Hide();
        }

        private void FitWindowToDesktop()
        {
            ApplyWindowMode();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 「Windowsと同時に起動」がONなら × ボタンでも終了せずバックグラウンドへ
            if (ChkStartWithWindows.IsChecked == true && !_isBackgroundMode)
            {
                e.Cancel = true; // 終了をキャンセル
                SaveAppSettings();
                HideToBackground();
                ShowDesktopNotification(
                    "OSAKA",
                    "バックグラウンドで動作中です。タスク期限は通知されます。");
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            UnregisterMemoShortcut();
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            SaveAppSettings();
            if (_memosLoaded)
                SaveMemos();
            SaveTasks();
            if (_timestampsLoaded && !_timestampLoadFailed)
                SaveTimestamps();

            _obsMuteTimer.Stop();
            _taskDueTimer.Stop();
            StopAutoLiveMonitor();
            StopChatPolling();

            _obsReconnectCts?.Cancel();
            _obsReconnectCts?.Dispose();
            _obsReconnectCts = null;

            _hanshinScoreTimer.Stop();

            try
            {
                _obs.Disconnect();
            }
            catch
            {
            }

            _ = LocalServer.StopAsync();
        }

        private async Task StartLocalServerAsync()
        {
            try
            {
                if (_currentServerPort == 4455 && NormalizeObsWebSocketUrl(_obsWebSocketUrl).EndsWith(":4455", StringComparison.OrdinalIgnoreCase))
                {
                    _currentServerPort = 5000;
                    TxtServerPort.Text = "5000";
                }

                await LocalServer.StopAsync();
                await LocalServer.StartAsync(_currentServerIp, _currentServerPort);
                await ChatWebView.EnsureCoreWebView2Async();
                ChatWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                ChatWebView.CoreWebView2.Navigate($"http://{_currentServerIp}:{_currentServerPort}/chat.html?t={DateTime.Now.Ticks}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Local server start failed: {ex.Message}");
            }
        }

        private void BtnShowMemos_Click(object sender, RoutedEventArgs e)
        {
            OpenMemoWithLiveConfirmation(() => 
            {
                MemoPopup.Visibility = Visibility.Visible;
            });
        }

        private void BtnShowTasks_Click(object sender, RoutedEventArgs e)
        {
            TaskPopup.Visibility = TaskPopup.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnCloseTaskPanel_Click(object sender, RoutedEventArgs e)
        {
            TaskPopup.Visibility = Visibility.Collapsed;
        }

        private void OpenMemoWithLiveConfirmation(Action openAction)
        {
            // 配信中かどうか
            bool isLive = _liveStartedAt != null;

            if (isLive)
            {
                ShowConfirmPopup(
                    "現在、配信中です。\nメモを開きますか？",
                    openAction
                );

                return;
            }

            // 配信中でなければそのまま開く
            openAction?.Invoke();
        }



        private void BtnToggleChatVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isChatHidden = !_isChatHidden;
            UpdateChatOverlayVisibility();
            CommentOffIcon.Visibility = _isChatHidden ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnConnectChat_Click(object sender, RoutedEventArgs e)
        {
            // 手動ライブURL入力は廃止し、自動接続用チャンネルURLだけを使用する。
            var channelUrl = TxtChannelUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                return;
            }

            var liveInfo = await TryFindLiveInfoAsync(channelUrl, CancellationToken.None);

            if (liveInfo != null && !string.IsNullOrWhiteSpace(liveInfo.Url))
            {
                StartChatPolling(
                    liveInfo.Url,
                    isAutoConnected: false,
                    liveInfo.StartedAt,
                    liveInfo.IsScheduled);
            }
            else
            {
                ShowEventNotification("Live Not Found", "チャンネルでライブ配信が見つかりませんでした", Color.FromRgb(80, 80, 80), TimeSpan.FromSeconds(8), GetAudioPath("se_itemget_009.wav"), 0.5f);
            }
        }

        private void BtnDisconnectChat_Click(object sender, RoutedEventArgs e)
        {
            StopChatPolling();
            SetLiveOn(false);
        }

        private void StartChatPolling(string liveUrl, bool isAutoConnected, DateTimeOffset? liveStartedAt = null, bool isScheduled = false)
        {
            StopChatPolling();
            _currentLiveUrl = liveUrl;
            _isAutoConnectedLive = isAutoConnected;
            _isScheduledLive = isScheduled;
            _isChatHidden = false;
            UpdateChatOverlayVisibility();
            _liveStartedAt = isScheduled ? null : (liveStartedAt ?? DateTimeOffset.Now);
            _cts = new CancellationTokenSource();
            _poller = new YouTubeChatPoller();
            BtnConnectChat.IsEnabled = false;
            BtnDisconnectChat.IsEnabled = true;
            ChatOverlayArea.Visibility = Visibility.Visible;
            CommentOffIcon.Visibility = Visibility.Collapsed;
            SetLiveOn(!isScheduled);
            UpdateLiveElapsedClock();
            _ = LocalServer.BroadcastMessage("OSAKA", "コメント接続を開始しました");
            _ = Task.Run(() => _poller.StartPollingAsync(liveUrl, _cts.Token));
        }

        private void StopChatPolling()
        {
            _poller?.Stop();
            _poller = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _currentLiveUrl = null;
            _isAutoConnectedLive = false;
            _isScheduledLive = false;
            _liveStartedAt = null;
            BtnConnectChat.IsEnabled = true;
            BtnDisconnectChat.IsEnabled = false;
            UpdateLiveElapsedClock();
        }

        private void SetLiveOn(bool isLive)
        {
            bool show = isLive && _liveStartedAt != null && DateTimeOffset.Now - _liveStartedAt.Value >= TimeSpan.FromSeconds(1);
            
            LiveOnIcon.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StartAutoLiveMonitor()
        {
            StopAutoLiveMonitor();
            if (ChkAutoConnect?.IsChecked != true || string.IsNullOrWhiteSpace(GetAutoLiveChannelUrl()))
            {
                return;
            }

            _autoLiveCts = new CancellationTokenSource();
            _ = MonitorChannelLiveAsync(_autoLiveCts.Token);
        }

        private void StopAutoLiveMonitor()
        {
            _autoLiveCts?.Cancel();
            _autoLiveCts?.Dispose();
            _autoLiveCts = null;
        }

        private async Task MonitorChannelLiveAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var channelUrl = await Dispatcher.InvokeAsync(GetAutoLiveChannelUrl);
                    var liveInfo = await TryFindLiveInfoAsync(channelUrl, ct);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var liveUrl = liveInfo?.Url;
                        if (_currentLiveUrl != null && !_isAutoConnectedLive)
                        {
                            return;
                        }

                        if (!string.IsNullOrWhiteSpace(liveUrl))
                        {
                            SetLiveOn(!liveInfo!.IsScheduled);
                            if (_currentLiveUrl != liveUrl || _isScheduledLive != liveInfo.IsScheduled)
                            {
                                StartChatPolling(
                                    liveUrl,
                                    isAutoConnected: true,
                                    liveInfo.StartedAt,
                                    liveInfo.IsScheduled);
                            }
                        }
                        else
                        {
                            SetLiveOn(false);
                            if (_isAutoConnectedLive)
                            {
                                StopChatPolling();
                            }
                        }
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Live monitor failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                }
                catch (OperationCanceledException) { }
            }
        }

        private static async Task<string?> TryFindLiveUrlAsync(string channelUrl, CancellationToken ct)
        {
            return (await TryFindLiveInfoAsync(channelUrl, ct))?.Url;
        }

        private sealed record LiveInfo(string Url, DateTimeOffset? StartedAt, bool IsScheduled);

        private static async Task<LiveInfo?> TryFindLiveInfoAsync(string channelUrl, CancellationToken ct)
        {
            var url = NormalizeChannelUrl(channelUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            using var client = CreateYouTubeHttpClient();
            if (IsWatchUrl(url))
            {
                var watchHtml = await client.GetStringAsync(url, ct);

                if (IsLiveNowHtml(watchHtml))
                {
                    return new LiveInfo(url, ExtractLiveStartTimeFromHtml(watchHtml), IsScheduled: false);
                }

                if (TryExtractScheduledStartTime(watchHtml, out _))
                {
                    return new LiveInfo(url, null, IsScheduled: true);
                }

                return null;
            }

            using var response = await client.GetAsync(url, ct);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString();
            var html = await response.Content.ReadAsStringAsync(ct);

            if (!string.IsNullOrWhiteSpace(finalUrl) && IsWatchUrl(finalUrl))
            {
                if (IsLiveNowHtml(html))
                {
                    return new LiveInfo(finalUrl, ExtractLiveStartTimeFromHtml(html), IsScheduled: false);
                }

                if (TryExtractScheduledStartTime(html, out _))
                {
                    return new LiveInfo(finalUrl, null, IsScheduled: true);
                }

                return null;
            }

            var liveUrl = ExtractLiveUrlFromHtml(html);
            if (!string.IsNullOrWhiteSpace(liveUrl))
            {
                return new LiveInfo(liveUrl, await TryGetLiveStartTimeAsync(liveUrl, ct), IsScheduled: false);
            }

            // 公開予定配信は /live ではなく /streams 側に掲載される場合がある。
            var scheduledUrl = ExtractScheduledLiveUrlFromHtml(html);
            if (!string.IsNullOrWhiteSpace(scheduledUrl))
            {
                return new LiveInfo(scheduledUrl, null, IsScheduled: true);
            }

            // /live で見つからない場合は /streams をフォールバックとして検索する。
            var streamsUrl = BuildStreamsUrl(channelUrl);
            if (!string.IsNullOrWhiteSpace(streamsUrl) && !string.Equals(streamsUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var streamsHtml = await client.GetStringAsync(streamsUrl, ct);
                    var streamsLiveUrl = ExtractLiveUrlFromHtml(streamsHtml);
                    if (!string.IsNullOrWhiteSpace(streamsLiveUrl))
                    {
                        return new LiveInfo(streamsLiveUrl, await TryGetLiveStartTimeAsync(streamsLiveUrl, ct), IsScheduled: false);
                    }

                    var streamsScheduledUrl = ExtractScheduledLiveUrlFromHtml(streamsHtml);
                    if (!string.IsNullOrWhiteSpace(streamsScheduledUrl))
                    {
                        return new LiveInfo(streamsScheduledUrl, null, IsScheduled: true);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // /streams の取得失敗時は従来どおり null を返す。
                }
            }

            return null;
        }

        private static bool IsLiveNowHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            // YouTube のライブ配信ページに含まれるフラグ
            return Regex.IsMatch(
                html,
                @"""isLiveNow""\s*:\s*true",
                RegexOptions.IgnoreCase);
        }

        private static HttpClient CreateYouTubeHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7");
            return client;
        }

        private static async Task<DateTimeOffset?> TryGetLiveStartTimeAsync(string liveUrl, CancellationToken ct)
        {
            try
            {
                using var client = CreateYouTubeHttpClient();
                var html = await client.GetStringAsync(liveUrl, ct);
                return ExtractLiveStartTimeFromHtml(html);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractScheduledLiveUrlFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var decoded = System.Net.WebUtility.HtmlDecode(html);

            // YouTube の表示形式変更に備え、videoId と scheduledStartTime の
            // 順序・距離が多少変わっても拾えるようにする。
            var patterns = new[]
            {
                @"""videoId""\s*:\s*""(?<id>[^""\\]+)""(?:(?!""videoId"").){0,12000}?""scheduledStartTime""\s*:\s*""(?<time>[^""\\]+)""",
                @"""scheduledStartTime""\s*:\s*""(?<time>[^""\\]+)""(?:(?!""scheduledStartTime"").){0,12000}?""videoId""\s*:\s*""(?<id>[^""\\]+)""",
                @"""videoId""\s*:\s*""(?<id>[^""\\]+)""(?:(?!""videoId"").){0,12000}?""publishDate""",
                @"""videoId""\s*:\s*""(?<id>[^""\\]+)""[^""\\]{0,3000}?(?:Premiere|プレミア公開|UPCOMING|公開予定)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(decoded, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["id"].Value))
                    return $"https://www.youtube.com/watch?v={match.Groups["id"].Value}";
            }

            // HTML 内に直接 /watch?v=ID が埋め込まれているケース。
            var watchMatches = Regex.Matches(
                decoded,
                @"/watch\?v=(?<id>[A-Za-z0-9_-]{6,})",
                RegexOptions.IgnoreCase);

            foreach (Match match in watchMatches)
            {
                var id = match.Groups["id"].Value;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var nearbyStart = Math.Max(0, match.Index - 1500);
                var nearbyLength = Math.Min(decoded.Length - nearbyStart, 5000);
                var nearby = decoded.Substring(nearbyStart, nearbyLength);
                if (Regex.IsMatch(nearby, @"scheduledStartTime|Premiere|\bUPCOMING\b|公開予定", RegexOptions.IgnoreCase))
                    return $"https://www.youtube.com/watch?v={id}";
            }

            return null;
        }

        private static bool TryExtractScheduledStartTime(string html, out DateTimeOffset? scheduledStartTime)
        {
            scheduledStartTime = null;
            if (string.IsNullOrWhiteSpace(html)) return false;

            var decoded = System.Net.WebUtility.HtmlDecode(html);
            var match = Regex.Match(decoded, @"""scheduledStartTime""\s*:\s*""(?<timestamp>[^""\\]+)""", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            if (DateTimeOffset.TryParse(match.Groups["timestamp"].Value, out var parsed))
            {
                scheduledStartTime = parsed;
                return true;
            }

            if (long.TryParse(match.Groups["timestamp"].Value, out var unixSeconds))
            {
                scheduledStartTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                return true;
            }

            return true;
        }

        private static string? ExtractLiveUrlFromHtml(string html)
        {
            var decoded = System.Net.WebUtility.HtmlDecode(html);
            var patterns = new[]
            {
                @"""watchEndpoint"":\{""videoId"":""(?<id>[^""]+)""\}[^{}]{0,1200}""thumbnailOverlayTimeStatusRenderer"":\{""text"":\{""runs"":\[\{""text"":""LIVE",
                @"""videoId"":""(?<id>[^""]+)""[^{}]{0,1600}""style"":""LIVE""",
                @"""videoId"":""(?<id>[^""]+)""[^{}]{0,1600}LIVE",
                @"""url"":""/watch\?v=(?<id>[^""\\&]+)[^""]*""[^{}]{0,1600}LIVE"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(decoded, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return $"https://www.youtube.com/watch?v={match.Groups["id"].Value}";
                }
            }

            return null;
        }

        private static DateTimeOffset? ExtractLiveStartTimeFromHtml(string html)
        {
            var decoded = System.Net.WebUtility.HtmlDecode(html);
            var match = Regex.Match(decoded, @"""startTimestamp"":""(?<timestamp>[^""]+)""", RegexOptions.IgnoreCase);
            if (match.Success && DateTimeOffset.TryParse(match.Groups["timestamp"].Value, out var startedAt))
            {
                return startedAt;
            }

            match = Regex.Match(decoded, @"""actualStartTime"":""(?<unix>\d+)""", RegexOptions.IgnoreCase);
            if (match.Success && long.TryParse(match.Groups["unix"].Value, out var unixSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }

            return null;
        }

        private static string? BuildStreamsUrl(string channelUrl)
        {
            try
            {
                var trimmed = channelUrl.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return null;

                if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    trimmed = $"https://www.youtube.com/{trimmed.TrimStart('/')}";

                if (IsWatchUrl(trimmed))
                    return null;

                return trimmed.TrimEnd('/') + "/streams";
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeChannelUrl(string channelUrl)
        {
            var trimmed = channelUrl.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "";
            }

            if (!trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = $"https://www.youtube.com/{trimmed.TrimStart('/')}";
            }

            if (IsWatchUrl(trimmed))
            {
                return trimmed;
            }

            return trimmed.TrimEnd('/') + "/live";
        }

        private string GetAutoLiveChannelUrl()
        {
            // 自動接続先はチャンネルURL入力欄だけを使用する。
            return TxtChannelUrl?.Text?.Trim() ?? "";
        }

        private static bool IsWatchUrl(string url)
        {
            return url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase)
                || url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);
        }

        private async void BtnRestartServer_Click(object sender, RoutedEventArgs e)
        {
            SaveAppSettings();
            await StartLocalServerAsync();
            StartAutoLiveMonitor();
        }

        private void BtnRestartApp_Click(object sender, RoutedEventArgs e)
        {
            ShowConfirmPopup("アプリを再起動しますか？", () =>
            {
                SaveAppSettings();
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                    Application.Current.Shutdown();
                }
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChkStartWithWindows.IsChecked == true)
            {
                SaveAppSettings();
                HideToBackground();
                ShowDesktopNotification("OSAKA", "バックグラウンドで動作中です。タスク期限は通知されます。");
                return;
            }

            ShowConfirmPopup("アプリを閉じますか？", () =>
            {
                SaveAppSettings();
                Application.Current.Shutdown();
            });
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private async void BtnEditObs_Click(object sender, RoutedEventArgs e)
        {
            ObsSettingsDetail.Visibility = Visibility.Visible;
            UpdateChatOverlayVisibility();

            try
            {
                await ObsPreviewWebView.EnsureCoreWebView2Async();
                ObsPreviewWebView.CoreWebView2.Navigate(
                    $"http://{_currentServerIp}:{_currentServerPort}/obs.html");
            }
            catch { }
        }

        private void BtnCloseObsDetail_Click(object sender, RoutedEventArgs e)
        {
            ObsSettingsDetail.Visibility = Visibility.Collapsed;
        }

        private async void ObsSettingsChanged(object sender, EventArgs e)
        {
            if (!IsLoaded) return;
            await LocalServer.BroadcastCommand("updateSettings", new
            {
                showUser = ChkShowUser.IsChecked ?? true,
                width = (int)SliderObsWidth.Value,
                color = TxtObsColor.Text
            });
        }

        private DateTime _lastObsConnectAttempt = DateTime.MinValue;

        private async void BtnObsMuteToggle_Click(
    object sender,
    RoutedEventArgs e)
        {
            try
            {
                BtnObsMuteToggle.IsEnabled = false;

                await Task.Run(() =>
                {
                    if (!_obsLock.Wait(3000))
                        return;

                    try
                    {
                        if (!EnsureObsConnected(
                            isUserInitiated: true))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "ミュート操作: OBS接続失敗"
                            );

                            return;
                        }

                        // 接続直後のOBS側準備を少し待つ
                        Thread.Sleep(150);

                        ToggleObsMicMuteLocked();

                        System.Diagnostics.Debug.WriteLine(
                            "OBSマイクミュート切替成功"
                        );
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"OBS mute operation failed: {ex.Message}"
                        );
                    }
                    finally
                    {
                        _obsLock.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"OBS mute failed: {ex.Message}"
                );
            }
            finally
            {
                BtnObsMuteToggle.IsEnabled = true;
            }
        }


        private async void BtnPauseChat_Click(object sender, RoutedEventArgs e)
        {
            _isChatPaused = !_isChatPaused;
            CommentPauseIcon.Visibility = _isChatPaused ? Visibility.Visible : Visibility.Collapsed;
            await LocalServer.BroadcastCommand("togglePause", new { isPaused = _isChatPaused });
        }

        private void BtnAddListMemo_Click(object sender, RoutedEventArgs e)
        {
            var memo = CreateListMemoContainer(ownerPanel: ListMemosPanel);
            _memoFolderIds[memo] = string.IsNullOrEmpty(_activeListFolderId)
                ? GetUncategorizedFolderId("list")
                : _activeListFolderId;
            ListMemosPanel.Children.Insert(0, memo);
            ApplyMemoSearch();
            SaveMemos();
        }

        private void BtnAddCheckListItem_Click(object sender, RoutedEventArgs e)
        {
            ListMemosPanel.Children.Insert(0, CreateListMemoContainer());
            SaveMemos();
        }

        private void BtnCloseListMemo_Click(object sender, RoutedEventArgs e)
        {
            // Shiftキーを押していなければ何もしない
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                return;
            }

            if (sender is not Button button)
            {
                return;
            }

            var container = FindMemoContainer(button);
            if (container == null)
            {
                return;
            }

            if (ListMemosPanel.Children.Count <= 1)
            {
                // 最後の1件は削除せず、デフォルト状態に戻す
                ResetListMemoContainer(container);
            }
            else
            {
                ListMemosPanel.Children.Remove(container);
            }

            SaveMemos();
        }

        private void ResetListMemoContainer(Border container)
        {
            var titleBox = container
                .Descendants()
                .OfType<TextBox>()
                .FirstOrDefault();

            if (titleBox != null)
            {
                titleBox.Text = "リスト";
            }

            var itemsPanel = container
                .Descendants()
                .OfType<StackPanel>()
                .FirstOrDefault(panel =>
                    panel.Children.OfType<CheckBox>().Any());

            if (itemsPanel != null)
            {
                itemsPanel.Children.Clear();

                var row = CreateListItemRow(() => { });
                itemsPanel.Children.Add(row);
            }

            // 虫眼鏡の絞り込みを解除
            var searchButton = container
                .Descendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.Content?.ToString() == "🔍");

            if (searchButton != null)
            {
                searchButton.Foreground = Brushes.White;
            }

            SaveMemos();
        }

        private void ClearListMemoContent(Border border)
        {
            if (border.Child is not Panel root) return;

            var header = root.Children.OfType<Grid>().FirstOrDefault();
            if (header == null) return;

            var titleBox = header.Children.OfType<TextBox>().FirstOrDefault();

            if (titleBox != null) titleBox.Text = "";

            var itemsPanel = root.Children.OfType<StackPanel>().FirstOrDefault();

            if (itemsPanel != null)
            {
                itemsPanel.Children.Clear();
            }

            SaveMemos();
        }

        private void BtnAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_bgmPlayer?.PlaybackState == PlaybackState.Playing)
            {
                _bgmPlayer.Pause();
                PauseIcon.Visibility = Visibility.Collapsed;
            }
            else if (_bgmPlayer?.PlaybackState == PlaybackState.Paused)
            {
                _bgmPlayer.Play();
                PauseIcon.Visibility = Visibility.Visible;
            }
            else
            {
                PlayBGM(GetAudioPath("508.mp3"), _bgmVolume);
            }
        }

        private void BtnVolumeUp_Click(object sender, RoutedEventArgs e)
        {
            _bgmVolume = Math.Min(1.0f, _bgmVolume + 0.1f);
            if (_bgmPlayer != null) _bgmPlayer.Volume = _bgmVolume;
        }

        private void BtnVolumeDown_Click(object sender, RoutedEventArgs e)
        {
            _bgmVolume = Math.Max(0.0f, _bgmVolume - 0.1f);
            if (_bgmPlayer != null) _bgmPlayer.Volume = _bgmVolume;
        }

        private void PlayBGM(string filePath, float volume)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                _bgmPlayer?.Stop();
                _bgmReader?.Dispose();
                _bgmPlayer?.Dispose();
                _bgmReader = filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? new VorbisWaveReader(filePath) : new AudioFileReader(filePath);
                _bgmPlayer = new WaveOutEvent();
                _bgmPlayer.Init(_bgmReader);
                _bgmPlayer.Volume = volume;
                _bgmPlayer.PlaybackStopped += (s, e) =>
                {
                    if (_bgmReader != null)
                    {
                        _bgmReader.Position = 0;
                        _bgmPlayer?.Play();
                    }
                };
                _bgmPlayer.Play();
                PauseIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BGM failed: {ex.Message}");
            }
        }

        private void TestSC200_Click(object sender, RoutedEventArgs e) => ShowEventNotification("Super Chat: ￥200", "UserA: test", Color.FromRgb(0, 229, 255), TimeSpan.FromMinutes(1), GetAudioPath("coin05.mp3"));
        private void TestSC2000_Click(object sender, RoutedEventArgs e) => ShowEventNotification("Super Chat: ￥2,000", "UserB: test", Color.FromRgb(255, 102, 0), TimeSpan.FromMinutes(10), GetAudioPath("coin05.mp3"));
        private void TestMemberJoin_Click(object sender, RoutedEventArgs e) => ShowEventNotification("New Member!", "UserC さんがメンバーになりました！", Color.FromRgb(15, 157, 88), TimeSpan.FromMinutes(1), GetAudioPath("1up3.ogg"));
        private void TestMemberGift_Click(object sender, RoutedEventArgs e) => ShowEventNotification("Membership Gift", "UserD さんがギフトしました！", Color.FromRgb(15, 157, 88), TimeSpan.FromMinutes(1), GetAudioPath("1up3.ogg"));

        private Border? FindMemoContainer(DependencyObject? start)
        {
            DependencyObject? current = start;
            while (current != null)
            {
                if (current is Border border)
                {
                    return border;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // --- Basic missing implementations needed to fix syntax errors introduced previously ---
        private void ConnectObsWebSocket()
        {
            StartObsReconnectLoop();
        }

        private void StartObsReconnectLoop()
        {
            if (_obsReconnectTask != null &&
                !_obsReconnectTask.IsCompleted)
            {
                return;
            }

            _obsReconnectCts?.Cancel();
            _obsReconnectCts?.Dispose();

            _obsReconnectCts = new CancellationTokenSource();

            var token = _obsReconnectCts.Token;

            _obsReconnectTask = Task.Run(
                () => ObsReconnectLoopAsync(token),
                token
            );
        }

        private async Task ObsReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_obs.IsConnected)
                    {
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    bool connected = await TryConnectObsAsync(ct);

                    if (connected)
                    {
                        _obsReconnectDelayMs = 1000;

                        System.Diagnostics.Debug.WriteLine(
                            "OBSへの再接続に成功しました。"
                        );

                        await Task.Delay(2000, ct);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"OBS未接続。{_obsReconnectDelayMs}ms後に再試行します。"
                        );

                        await Task.Delay(
                            _obsReconnectDelayMs,
                            ct
                        );

                        _obsReconnectDelayMs = Math.Min(
                            _obsReconnectDelayMs * 2,
                            ObsReconnectMaxDelayMs
                        );
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"OBS再接続ループエラー: {ex.Message}"
                    );

                    try
                    {
                        await Task.Delay(
                            _obsReconnectDelayMs,
                            ct
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    _obsReconnectDelayMs = Math.Min(
                        _obsReconnectDelayMs * 2,
                        ObsReconnectMaxDelayMs
                    );
                }
            }
        }


        private async Task<bool> TryConnectObsAsync(CancellationToken ct)
        {
            if (_obs.IsConnected)
                return true;

            if (!await _obsConnectionLock.WaitAsync(1000, ct))
                return false;

            try
            {
                if (_obs.IsConnected)
                    return true;

                string url = NormalizeObsWebSocketUrl(_obsWebSocketUrl);
                string password = _obsPassword ?? "";

                Debug.WriteLine($"OBS WebSocket接続開始: {url}");

                try
                {
                    _obs.ConnectAsync(url, password);
                }
                catch (Exception ex)
                {
                    _isObsConnected = false;

                    Debug.WriteLine(
                        $"OBS ConnectAsync開始失敗: " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    return false;
                }

                var timeoutAt = DateTime.UtcNow.AddSeconds(10);

                while (!_obs.IsConnected)
                {
                    ct.ThrowIfCancellationRequested();

                    if (DateTime.UtcNow >= timeoutAt)
                    {
                        _isObsConnected = false;

                        Debug.WriteLine(
                            "OBS WebSocket接続タイムアウト");

                        return false;
                    }

                    await Task.Delay(100, ct);
                }

                _isObsConnected = true;
                _lastObsConnectionSuccess = DateTime.Now;
                _obsReconnectDelayMs = 1000;

                Debug.WriteLine(
                    "OBS WebSocket接続成功");

                return true;
            }
            catch (OperationCanceledException)
            {
                _isObsConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                _isObsConnected = false;

                Debug.WriteLine(
                    $"OBS WebSocket接続失敗: " +
                    $"{ex.GetType().Name}: {ex.Message}");

                return false;
            }
            finally
            {
                _obsConnectionLock.Release();
            }
        }




        private async void ObsMuteTimer_Tick(object? sender,EventArgs e)
        {
            if (_isObsPollRunning)
                return;

            if (!_obs.IsConnected)
            {
                StartObsReconnectLoop();
                return;
            }

            if (DateTime.UtcNow < _obsApiReadyAt)
            {
                // OBS APIが利用可能になるまで待機
                return;
            }

            _isObsPollRunning = true;

            try
            {
                var result = await Task.Run(() =>
                {
                    if (!_obsLock.Wait(300))
                    {
                        return (
                            HasState: false,
                            IsMuted: (bool?)null,
                            RecordingText: (string?)null
                        );
                    }

                    try
                    {
                        if (!_obs.IsConnected)
                        {
                            return (
                                HasState: false,
                                IsMuted: (bool?)null,
                                RecordingText: (string?)null
                            );
                        }

                        var isMuted = GetObsMicMuteLocked();

                        if (!isMuted.HasValue)
                        {
                            return (
                                HasState: false,
                                IsMuted: (bool?)null,
                                RecordingText: (string?)null
                            );
                        }

                        var recordingText = GetObsRecordingText();

                        return (
                            HasState: true,
                            IsMuted: isMuted,
                            RecordingText: recordingText
                        );
                    }
                    finally
                    {
                        _obsLock.Release();
                    }
                });

                if (!result.HasState)
                    return;

                if (result.IsMuted.HasValue)
                {
                    SetMuteMovieVisible(result.IsMuted.Value);
                }

                SetRecordingElapsedText(result.RecordingText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"OBS mute monitor failed: {ex.Message}"
                );

                StartObsReconnectLoop();
            }
            finally
            {
                _isObsPollRunning = false;
            }
        }

        private bool EnsureObsConnected(bool isUserInitiated = false)
        {
            try
            {
                if (_obs.IsConnected)
                    return true;

                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(12)
                    );

                return TryConnectObsAsync(cts.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "EnsureObsConnected: 接続待機がキャンセルされました。");

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"EnsureObsConnected failed: " +
                    $"{ex.GetType().Name}: {ex.Message}");

                return false;
            }
        }



        private bool? GetObsMicMuteLocked()
        {
            try
            {
                if (!_obs.IsConnected)
                    return null;



                var inputName = ResolveObsMicInputNameLocked();


                if (!_obs.IsConnected)
                    return null;

                var result = _obs.GetInputMute(inputName);

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GetObsMicMuteLocked error: {ex.GetType().Name}: {ex.Message}"
                );

                return null;
            }
        }

        private void ToggleObsMicMuteLocked()
        {
            var inputName = ResolveObsMicInputNameLocked();
            _obs.ToggleInputMute(inputName);
        }

        private string ResolveObsMicInputNameLocked()
        {
            if (!_obs.IsConnected)
                throw new InvalidOperationException("OBS WebSocket未接続");

            // 既に設定されている入力名を確認
            if (!string.IsNullOrWhiteSpace(_obsMicInputName))
            {
                try
                {
                    var mute = _obs.GetInputMute(_obsMicInputName);



                    return _obsMicInputName;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"OBSマイク入力確認失敗: {_obsMicInputName}, " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    _obsMicInputName = string.Empty;
                }
            }

            if (!_obs.IsConnected)
                throw new InvalidOperationException("OBS WebSocket切断");

            var inputs = _obs.GetInputList();

            if (inputs == null)
                throw new InvalidOperationException(
                    "OBSから入力一覧を取得できませんでした。");


            var resolved = inputs
                .OrderByDescending(input =>
                {
                    var name =
                        (input.InputName ?? string.Empty)
                        .ToLowerInvariant();

                    if (name.Contains("mic") ||
                        name.Contains("microphone") ||
                        name.Contains("マイク"))
                    {
                        return 2;
                    }

                    var kind =
                        ((input.UnversionedKind ?? input.InputKind) ?? string.Empty)
                        .ToLowerInvariant();

                    return kind.Contains("input_capture") ? 1 : 0;
                })
                .Select(input => input.InputName)
                .FirstOrDefault(name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                        return false;

                    if (!_obs.IsConnected)
                        return false;

                    try
                    {
                        _obs.GetInputMute(name);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (string.IsNullOrWhiteSpace(resolved))
            {
                Debug.WriteLine("OBSマイク入力を検出できませんでした。");

                throw new InvalidOperationException(
                    "OBSのマイク入力が見つかりません。");
            }

            _obsMicInputName = resolved;

            Debug.WriteLine(
                $"OBSマイク入力決定: [{_obsMicInputName}]");

            return resolved;
        }










        private string? GetObsRecordingText()
        {
            try
            {
                if (!_obs.IsConnected)
                {
                    _currentRecordingFileName = null;
                    return null;
                }

                var status = _obs.GetRecordStatus();

                var isRecording = TryGetBoolProperty(
                    status,
                    "IsActive",
                    "OutputActive",
                    "RecordActive",
                    "Recording",
                    "IsRecording"
                );

                // アプリ起動時に、すでにOBSが録画中だった場合も
                // 「録画開始の瞬間」を見失わないようにする。
                if (isRecording && !_wasRecording)
                {
                    _recordingStartedAt = DateTime.Now;
                }

                _wasRecording = isRecording;

                if (!isRecording) 
                {
                    _wasRecording = false;
                    _currentRecordingFileName = null;
                    return null;
                }

                UpdateCurrentRecordingFileName();

                // OBSの録画ファイルパスを取得。
                // アプリを録画開始後に起動した場合、RecordStatusに
                // OutputPathが返らないことがあるため、後段の
                // UpdateCurrentRecordingFileName()で録画フォルダから
                // 現在の録画ファイルを推定する。
                var outputPath = TryGetStringProperty(
                    status,
                    "OutputPath",
                    "RecordPath",
                    "RecordingPath"
                );

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    _currentRecordingFileName = Path.GetFileName(outputPath);
                }

                var timecode = TryGetStringProperty(
                    status,
                    "Timecode",
                    "OutputTimecode",
                    "OutputDuration",
                    "Duration",
                    "RecordTimecode"
                );

                timecode = TrimFractionalSeconds(timecode);

                return string.IsNullOrWhiteSpace(timecode)
                    ? "REC"
                    : $"REC {timecode}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"OBS record status failed: {ex.Message}"
                );

                _currentRecordingFileName = null;

                try
                {
                    _obs.Disconnect();
                }
                catch { }

                return null;
            }
        }

        private void UpdateCurrentRecordingFileName()
        {
            try
            {
                if (!_recordingStartedAt.HasValue)
                    return;

                var directory = _obs.GetRecordDirectory();

                if (string.IsNullOrWhiteSpace(directory) ||
                    !Directory.Exists(directory))
                {
                    return;
                }

                var allFiles = Directory
                    .GetFiles(directory)
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTime)
                    .ToList();

                // 通常は録画開始時刻以降に作成されたファイルを優先。
                var candidates = allFiles
                    .Where(file =>
                        file.CreationTime >= _recordingStartedAt.Value.AddSeconds(-5))
                    .ToList();

                if (candidates.Count > 0)
                {
                    _currentRecordingFileName = candidates[0].Name;
                    return;
                }

                // アプリを録画開始後に起動した場合は、
                // _recordingStartedAt が「アプリを起動した時刻」になってしまうため、
                // CreationTimeだけでは現在録画中のファイルを見つけられない。
                // その場合は録画フォルダ内で最後に更新されたファイルを使用する。
                // 録画中のファイルは通常、書き込みのたびにLastWriteTimeが更新される。
                if (allFiles.Count > 0)
                {
                    _currentRecordingFileName = allFiles[0].Name;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"録画ファイル取得失敗: {ex.Message}"
                );
            }
        }

        private static bool TryGetBoolProperty(object target, params string[] names)
        {
            var type = target.GetType();
            foreach (var name in names)
            {
                var prop = type.GetProperty(name);
                if (prop?.GetValue(target) is bool value)
                {
                    return value;
                }
            }
            return false;
        }

        private static string? TryGetStringProperty(object target, params string[] names)
        {
            var type = target.GetType();
            foreach (var name in names)
            {
                var value = type.GetProperty(name)?.GetValue(target);
                if (value != null)
                {
                    if (value is TimeSpan ts)
                    {
                        return ts.TotalHours >= 100
                            ? $"{(int)ts.TotalHours:000}:{ts.Minutes:00}:{ts.Seconds:00}"
                            : $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
                    }

                    if (value is long longValue && longValue > 0)
                    {
                        var longTime = TimeSpan.FromMilliseconds(longValue);
                        return longTime.TotalHours >= 100
                            ? $"{(int)longTime.TotalHours:000}:{longTime.Minutes:00}:{longTime.Seconds:00}"
                            : $"{(int)longTime.TotalHours:00}:{longTime.Minutes:00}:{longTime.Seconds:00}";
                    }

                    if (value is int intValue && intValue > 0)
                    {
                        var intTime = TimeSpan.FromMilliseconds(intValue);
                        return intTime.TotalHours >= 100
                            ? $"{(int)intTime.TotalHours:000}:{intTime.Minutes:00}:{intTime.Seconds:00}"
                            : $"{(int)intTime.TotalHours:00}:{intTime.Minutes:00}:{intTime.Seconds:00}";
                    }

                    return value.ToString();
                }
            }
            return null;
        }

        private static string? TrimFractionalSeconds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var dotIndex = value.IndexOf('.');
            return dotIndex > 0 ? value[..dotIndex] : value;
        }

        private void SetRecordingElapsedText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _lastRecordingElapsed = null;
                RecordingElapsedClock.Text = "";
                RecordingElapsedClock.Visibility = Visibility.Collapsed;
                UpdateTimestampSourceAvailability();
                return;
            }

            RecordingElapsedClock.Text = text;
            _lastRecordingElapsed = StripRecordingPrefix(text);
            RecordingElapsedClock.Visibility = Visibility.Collapsed;
            UpdateTimestampSourceAvailability();
        }

        private static string StripRecordingPrefix(string text)
        {
            var value = text.Trim();
            return value.StartsWith("REC ", StringComparison.OrdinalIgnoreCase) ? value[4..].Trim() : value;
        }

        private void UpdateTimestampSourceAvailability()
        {
            if (!IsInitialized)
            {
                return;
            }

            var isLive = _liveStartedAt != null;
            RbTimestampLive.IsEnabled = isLive;
            if (!isLive)
            {
                RbTimestampRecording.IsChecked = true;
            }
        }

        private void SetMuteMovieVisible(bool isMuted)
        {
            DotFallbackImage.Visibility = Visibility.Visible;
            BackgroundMedia.Visibility = Visibility.Visible;
            if (BackgroundMedia.CanPause)
            {
                BackgroundMedia.Play();
            }

            BackgroundMediaAlt.Visibility = Visibility.Collapsed;
            BackgroundMediaAlt.Stop();
            MuteMedia.Visibility = Visibility.Collapsed;
            MuteMedia.Stop();
            MuteMediaAlt.Visibility = Visibility.Collapsed;
            MuteMediaAlt.Stop();
            MuteFallbackImage.Visibility = Visibility.Collapsed;
            MuteOverlayImage.Visibility = isMuted ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowConfirmPopup(string message, Action onConfirm)
        {
            _pendingConfirmAction = onConfirm;
            ConfirmMessage.Text = message;
            ConfirmPopup.Visibility = Visibility.Visible;
        }

        private void ConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            var action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            ConfirmPopup.Visibility = Visibility.Collapsed;
            action?.Invoke();
        }

        private void ConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            _pendingConfirmAction = null;
            ConfirmPopup.Visibility = Visibility.Collapsed;
        }

        private static readonly string UserDataDirectory =
     Path.Combine(
         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
         "OSAKA");

        

        private string MemoBackupPath =>
            Path.Combine(UserDataDirectory, "memos.json");

        private string TasksPath =>
            Path.Combine(UserDataDirectory, "tasks.json");

        private string TimestampsPath =>
            Path.Combine(UserDataDirectory, "timestamps.json");

        private string TimestampFoldersPath =>
            Path.Combine(UserDataDirectory, "timestamp-folders.json");

        private void EnsureUserDataDirectory()
        {
            try
            {
                Directory.CreateDirectory(UserDataDirectory);

                MigrateUserDataFile("settings.json");
                MigrateUserDataFile("tasks.json");
                MigrateUserDataFile("timestamps.json");
                MigrateUserDataFile("timestamp-folders.json");
                MigrateUserDataFile("memos.json");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"User data directory creation failed: {ex}");
            }
        }

        private void MigrateUserDataFile(string fileName)
        {
            try
            {
                string oldPath =
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                string newPath =
                    Path.Combine(UserDataDirectory, fileName);

                if (!File.Exists(oldPath))
                {
                    return;
                }

                // 既にAppData側にデータがあるなら上書きしない
                if (File.Exists(newPath))
                {
                    return;
                }

                File.Copy(oldPath, newPath, false);

                Debug.WriteLine(
                    $"Migrated user data: {fileName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Migration failed ({fileName}): {ex}");
            }
        }
        private string _obsWebSocketUrl = "192.168.10.106:4455";
        private string _obsPassword = "";

        private string NormalizeObsWebSocketUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "127.0.0.1:4455";
            }

            var normalized = url.Trim();
            if (!normalized.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"ws://{normalized}";
            }

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsDefaultPort)
            {
                var builder = new UriBuilder(uri) { Port = 4455 };
                normalized = builder.Uri.ToString().TrimEnd('/');
            }

            return normalized;
        }

        private void LoadAppSettings()
        {
            try
            {
                if (!File.Exists(AppSettingsPath))
                {
                    return;
                }

                var json = File.ReadAllText(AppSettingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("ServerIp", out var serverIp)) _currentServerIp = serverIp.GetString() ?? _currentServerIp;
                if (root.TryGetProperty("ServerPort", out var serverPort) && serverPort.TryGetInt32(out var port)) _currentServerPort = port;
                if (_currentServerPort == 4455)
                {
                    _currentServerIp = "localhost";
                    _currentServerPort = 5000;
                }
                if (root.TryGetProperty("ChannelUrl", out var channelUrl)) TxtChannelUrl.Text = channelUrl.GetString() ?? "";
                if (root.TryGetProperty("AutoConnect", out var autoConnect)) ChkAutoConnect.IsChecked = autoConnect.GetBoolean();
                if (root.TryGetProperty("StartWithWindows", out var startWithWindows)) ChkStartWithWindows.IsChecked = startWithWindows.GetBoolean();
                if (root.TryGetProperty("MemoShortcutKey", out var memoShortcutKey))
                    _memoShortcutKey = memoShortcutKey.GetString() ?? _memoShortcutKey;
                TxtMemoShortcutKey.Text = _memoShortcutKey;
                if (root.TryGetProperty("UseUrotaCursor", out var useUrotaCursor)) _useUrotaCursor = useUrotaCursor.GetBoolean();
                if (root.TryGetProperty("WindowedMode", out var windowedMode)) _isWindowedMode = windowedMode.GetBoolean();
                if (root.TryGetProperty("FixedWindow1920", out var fixedWindow1920)) _fixedWindow1920 = fixedWindow1920.GetBoolean();
                if (root.TryGetProperty("TimestampSourceAlwaysVisible", out var timestampSourceAlwaysVisible))
                    _timestampSourceAlwaysVisible = timestampSourceAlwaysVisible.GetBoolean();
                UpdateTimestampSourceDisplayModeButton();
                if (root.TryGetProperty("SoundEffectsEnabled", out var soundEffectsEnabled)) _soundEffectsEnabled = soundEffectsEnabled.GetBoolean();
                if (root.TryGetProperty("ObsWebSocketUrl", out var obsUrl)) _obsWebSocketUrl = obsUrl.GetString() ?? _obsWebSocketUrl;
                
                if (root.TryGetProperty("ObsPassword", out var obsPassword)) _obsPassword = obsPassword.GetString() ?? _obsPassword;
                TxtObsWebSocketUrl.Text = _obsWebSocketUrl;
                TxtObsPassword.Password = _obsPassword;
                TxtServerPort.Text = _currentServerPort.ToString();
                ChkSoundEffectsEnabled.IsChecked = _soundEffectsEnabled;
                if (root.TryGetProperty("NotificationSoundEnabled", out var notificationSound))
                    ChkNotificationSoundEnabled.IsChecked = notificationSound.GetBoolean();
                if (root.TryGetProperty("EnableSuperChatNotification", out var enableSuperChat))
                    ChkEnableSuperChatNotification.IsChecked = enableSuperChat.GetBoolean();

                if (root.TryGetProperty("EnableMemberNotification", out var enableMember))
                    ChkEnableMemberNotification.IsChecked = enableMember.GetBoolean();
                ChkSoundEffectsEnabled_CheckedChanged(this, new RoutedEventArgs());
                SetWindowModeRadioSelection();
                SetCursorModeRadioSelection();
                ApplyWindowMode();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load settings failed: {ex.Message}");
            }
        }

        private void SaveAppSettings()
        {
            try
            {
                _currentServerIp = "localhost";
                if (int.TryParse(TxtServerPort.Text, out var port)) _currentServerPort = port;
                _obsWebSocketUrl = string.IsNullOrWhiteSpace(TxtObsWebSocketUrl.Text) ? "192.168.10.106:4455" : TxtObsWebSocketUrl.Text.Trim();
                _obsPassword = TxtObsPassword.Password ?? "";
                _soundEffectsEnabled = ChkSoundEffectsEnabled.IsChecked ?? true;
                if (_currentServerPort == 4455)
                {
                    _currentServerIp = "localhost";
                    _currentServerPort = 5000;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsPath)!);

                var settings = new
                {
                    ServerIp = _currentServerIp,
                    ServerPort = _currentServerPort,
                    ChannelUrl = TxtChannelUrl.Text,
                    AutoConnect = ChkAutoConnect.IsChecked ?? false,
                    StartWithWindows = ChkStartWithWindows.IsChecked ?? false,
                    MemoShortcutKey = _memoShortcutKey,
                    UseUrotaCursor = _useUrotaCursor,
                    WindowedMode = _isWindowedMode,
                    FixedWindow1920 = _fixedWindow1920,
                    TimestampSourceAlwaysVisible = _timestampSourceAlwaysVisible,
                    SoundEffectsEnabled = ChkSoundEffectsEnabled.IsChecked ?? true,
                    ObsWebSocketUrl = _obsWebSocketUrl,
                    ObsPassword = _obsPassword,
                    EnableSuperChatNotification = ChkEnableSuperChatNotification.IsChecked ?? true,
                    EnableMemberNotification = ChkEnableMemberNotification.IsChecked ?? true,
                    NotificationSoundEnabled = ChkNotificationSoundEnabled.IsChecked ?? true
                };
                File.WriteAllText(AppSettingsPath, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                UpdateStartupRegistration(ChkStartWithWindows.IsChecked ?? false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save settings failed: {ex.Message}");
            }
        }

        private void UpdateStartupRegistration(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null)
                {
                    return;
                }

                var exePath = Environment.ProcessPath;
                if (enabled && !string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("OSAKA", $"\"{exePath}\" --background");
                }
                else
                {
                    key.DeleteValue("OSAKA", throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup registration failed: {ex.Message}");
            }
        }

        private void TextMemoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _textMemoSearchText = TextMemoSearchBox?.Text?.Trim() ?? string.Empty;
            ApplyMemoSearch();
        }

        private void ListMemoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _listMemoSearchText = ListMemoSearchBox?.Text?.Trim() ?? string.Empty;
            ApplyMemoSearch();
        }

        private void TimestampSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _timestampSearchText = TimestampSearchBox?.Text?.Trim() ?? string.Empty;
            ApplyMemoSearch();
        }

        private void BtnClearTextMemoSearch_Click(object sender, RoutedEventArgs e)
        {
            TextMemoSearchBox?.Clear();
            TextMemoSearchBox?.Focus();
        }

        private void BtnClearListMemoSearch_Click(object sender, RoutedEventArgs e)
        {
            ListMemoSearchBox?.Clear();
            ListMemoSearchBox?.Focus();
        }

        private void BtnClearTimestampSearch_Click(object sender, RoutedEventArgs e)
        {
            TimestampSearchBox?.Clear();
            TimestampSearchBox?.Focus();
        }

        private static bool SearchMatches(string? text, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyMemoSearch()
        {
            bool textHasQuery = !string.IsNullOrWhiteSpace(_textMemoSearchText);
            bool listHasQuery = !string.IsNullOrWhiteSpace(_listMemoSearchText);

            foreach (var memo in TextMemosPanel.Children.OfType<Border>())
            {
                var boxes = memo.Descendants().OfType<TextBox>().ToList();
                bool matched = !textHasQuery || boxes.Any(b => SearchMatches(b.Text, _textMemoSearchText));
                bool folderVisible = IsFolderVisible("text", _memoFolderIds.TryGetValue(memo, out var id) ? id : string.Empty);
                memo.Visibility = matched && folderVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            foreach (var memo in ListMemosPanel.Children.OfType<Border>())
            {
                var boxes = memo.Descendants().OfType<TextBox>().ToList();
                bool titleMatched = boxes.Count > 0 && SearchMatches(boxes[0].Text, _listMemoSearchText);
                bool itemMatched = memo.Descendants().OfType<StackPanel>()
                    .Where(p => p.Children.OfType<CheckBox>().Count() >= 2)
                    .SelectMany(p => p.Children.OfType<TextBox>())
                    .Any(tb => SearchMatches(tb.Text, _listMemoSearchText));
                bool matched = !listHasQuery || titleMatched || itemMatched;
                bool folderVisible = IsFolderVisible("list", _memoFolderIds.TryGetValue(memo, out var id) ? id : string.Empty);
                memo.Visibility = matched && folderVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            RenderTimestamps();
        }


        private static void NormalizeFolderOrder(List<FolderData> folders)
        {
            for (int i = 0; i < folders.Count; i++)
                folders[i].Order = i;
        }

        private static void EnsureDefaultFolder(List<FolderData> folders)
        {
            if (folders.Count == 0)
            {
                folders.Add(new FolderData { Name = "未分類", Order = 0 });
            }
            NormalizeFolderOrder(folders);
        }

        private List<FolderData> GetFoldersForType(string type)
        {
            return type switch
            {
                "text" => _textFolders,
                "list" => _listFolders,
                "timestamp" => _timestampFolders,
                _ => _textFolders
            };
        }

        private string GetActiveFolderId(string type)
        {
            return type switch
            {
                "text" => _activeTextFolderId,
                "list" => _activeListFolderId,
                "timestamp" => _activeTimestampFolderId,
                _ => string.Empty
            };
        }

        private void SetActiveFolderId(string type, string id)
        {
            switch (type)
            {
                case "text": _activeTextFolderId = id; break;
                case "list": _activeListFolderId = id; break;
                case "timestamp": _activeTimestampFolderId = id; break;
            }

            UpdateFolderNameLabels();
            ApplyFolderVisibility();
            if (type == "timestamp")
                RenderTimestamps();
        }

        private string GetUncategorizedFolderId(string type)
        {
            var folders = GetFoldersForType(type);
            var folder = folders.FirstOrDefault(f => string.Equals(f.Name, "未分類", StringComparison.Ordinal));
            if (folder == null)
            {
                folder = new FolderData { Name = "未分類", Order = folders.Count };
                folders.Add(folder);
                NormalizeFolderOrder(folders);
                SaveFolderData(type);
            }
            return folder.Id;
        }

        private string GetFolderName(string type, string folderId)
        {
            if (string.IsNullOrEmpty(folderId))
                return "すべて";

            return GetFoldersForType(type).FirstOrDefault(f => f.Id == folderId)?.Name ?? "未分類";
        }

        private bool IsFolderVisible(string type, string folderId)
        {
            string active = GetActiveFolderId(type);
            return string.IsNullOrEmpty(active) || folderId == active;
        }

        private void SaveTimestampFolders()
        {
            try
            {
                EnsureUserDataDirectory();
                NormalizeFolderOrder(_timestampFolders);

                string json = JsonSerializer.Serialize(
                    new TimestampFolderDataFile { Folders = _timestampFolders },
                    new JsonSerializerOptions { WriteIndented = true });

                string tempPath = TimestampFoldersPath + ".tmp";
                string backupPath = TimestampFoldersPath + ".bak";

                File.WriteAllText(tempPath, json);

                if (File.Exists(TimestampFoldersPath))
                {
                    File.Copy(TimestampFoldersPath, backupPath, true);
                    File.Delete(TimestampFoldersPath);
                }

                File.Move(tempPath, TimestampFoldersPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save timestamp folders failed: {ex}");
            }
        }

        private void LoadTimestampFolders()
        {
            try
            {
                EnsureUserDataDirectory();
                _timestampFolders.Clear();

                // AppData側を優先。無ければ旧インストール先のファイルも探す。
                string sourcePath = TimestampFoldersPath;
                if (!File.Exists(sourcePath))
                {
                    string oldPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "timestamp-folders.json");
                    if (File.Exists(oldPath))
                    {
                        try
                        {
                            File.Copy(oldPath, TimestampFoldersPath, false);
                            sourcePath = TimestampFoldersPath;
                        }
                        catch
                        {
                            sourcePath = oldPath;
                        }
                    }
                }

                if (File.Exists(sourcePath))
                {
                    string json = File.ReadAllText(sourcePath);
                    TimestampFolderDataFile? data = null;
                    try
                    {
                        data = JsonSerializer.Deserialize<TimestampFolderDataFile>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Timestamp folder JSON load failed: {ex}");

                        string backupPath = sourcePath + ".bak";
                        if (File.Exists(backupPath))
                        {
                            try
                            {
                                data = JsonSerializer.Deserialize<TimestampFolderDataFile>(
                                    File.ReadAllText(backupPath));
                            }
                            catch (Exception backupEx)
                            {
                                Debug.WriteLine($"Timestamp folder backup load failed: {backupEx}");
                            }
                        }
                    }

                    if (data?.Folders != null)
                    {
                        foreach (var folder in data.Folders)
                        {
                            if (folder == null) continue;
                            if (string.IsNullOrWhiteSpace(folder.Id))
                                folder.Id = Guid.NewGuid().ToString();
                            if (string.IsNullOrWhiteSpace(folder.Name))
                                folder.Name = "未分類";
                            _timestampFolders.Add(folder);
                        }
                    }
                }

                EnsureDefaultFolder(_timestampFolders);
                NormalizeFolderOrder(_timestampFolders);

                // フォルダ情報が存在しなかった場合でも、必ず現在の状態を永続化する。
                SaveTimestampFolders();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load timestamp folders failed: {ex}");
                EnsureDefaultFolder(_timestampFolders);
                SaveTimestampFolders();
            }
        }

        private void ApplyFolderVisibility()
        {
            foreach (var memo in TextMemosPanel.Children.OfType<Border>())
            {
                string folderId = _memoFolderIds.TryGetValue(memo, out var id) ? id : string.Empty;
                memo.Visibility = IsFolderVisible("text", folderId)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            foreach (var memo in ListMemosPanel.Children.OfType<Border>())
            {
                string folderId = _memoFolderIds.TryGetValue(memo, out var id) ? id : string.Empty;
                memo.Visibility = IsFolderVisible("list", folderId)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void UpdateFolderNameLabels()
        {
            if (TextActiveFolderText != null)
                TextActiveFolderText.Text = $"[{GetFolderName("text", _activeTextFolderId)}]";

            if (ListActiveFolderText != null)
                ListActiveFolderText.Text = $"[{GetFolderName("list", _activeListFolderId)}]";

            if (TimestampActiveFolderText != null)
                TimestampActiveFolderText.Text = $"[{GetFolderName("timestamp", _activeTimestampFolderId)}]";

            UpdateTimestampFilterButton();
        }

        private Button CreateFolderButton(string type)
        {
            var button = CreateSmallButton("📁");
            button.ToolTip = "フォルダ";
            button.Click += (s, e) => ShowFolderPanel(button, type);
            return button;
        }

        private Button CreateFolderAssignButton(string type, Func<string> getFolderId, Action<string> setFolderId)
        {
            var button = CreateSmallButton("📁");
            button.ToolTip = "この項目のフォルダを変更";
            button.Click += (s, e) => ShowFolderAssignPopup(button, type, getFolderId, setFolderId);
            return button;
        }

        private void ShowFolderAssignPopup(
            UIElement target,
            string type,
            Func<string> getFolderId,
            Action<string> setFolderId)
        {
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                MinWidth = 190
            };

            panel.Children.Add(new TextBlock
            {
                Text = "保存先フォルダ",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 8, 10, 4)
            });

            // タイムスタンプでは「フォルダなし」と「未分類」が実質同じなので、
            // 保存先としては「未分類」だけを表示する。
            string selectedFolderId = getFolderId();
            if (type == "timestamp" && string.IsNullOrEmpty(selectedFolderId))
                selectedFolderId = GetUncategorizedFolderId("timestamp");

            void AddOption(string name, string id)
            {
                var b = new Button
                {
                    Content = (selectedFolderId == id ? "● " : "○ ") + name,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 6, 10, 6)
                };
                b.Click += (s, e) =>
                {
                    setFolderId(id);
                    popup.IsOpen = false;
                    if (type == "timestamp")
                        SaveTimestamps();
                    else
                        SaveMemos();
                    ApplyMemoSearch();
                    RenderTimestamps();
                };
                panel.Children.Add(b);
            }

            // タイムスタンプは必ずいずれかのフォルダに属する扱いにし、
            // 「フォルダなし」は表示しない。「未分類」を唯一の未指定先とする。
            if (type != "timestamp")
                AddOption("フォルダなし", string.Empty);

            foreach (var folder in GetFoldersForType(type).OrderBy(f => f.Order))
                AddOption(folder.Name, folder.Id);

            popup.Child = new Border
            {
                Background = panel.Background,
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                BorderThickness = new Thickness(1),
                Child = panel
            };
            popup.IsOpen = true;
        }

        private int GetTimestampCountForFolder(string folderId)
        {
            return _timestampBoxes
                .Where(b => b.FolderId == folderId)
                .Sum(b => b.Items?.Count ?? 0);
        }

        private string GetTimestampFolderDisplayName(FolderData folder)
        {
            if (string.Equals(folder.Name, "未分類", StringComparison.Ordinal))
                return $"{folder.Name}({GetTimestampCountForFolder(folder.Id)})";

            return $"{folder.Name}({GetTimestampCountForFolder(folder.Id)})";
        }

        private void ShowFolderPanel(UIElement target, string type)
        {
            _folderPopup?.SetCurrentValue(Popup.IsOpenProperty, false);

            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };
            _folderPopup = popup;
            _folderPopupType = type;

            var folders = GetFoldersForType(type);
            EnsureDefaultFolder(folders);

            var root = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                Width = 300
            };

            var title = new TextBlock
            {
                Text = $"{(type == "text" ? "メモ" : type == "list" ? "リストメモ" : "タイムスタンプ")} フォルダ",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(12, 10, 12, 6)
            };
            root.Children.Add(title);

            var allButton = new Button
            {
                Content = (_activeFolderIdFor(type).Length == 0 ? "● " : "○ ") + "すべて",
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 7, 12, 7)
            };
            allButton.Click += (s, e) =>
            {
                SetActiveFolderId(type, string.Empty);
                popup.IsOpen = false;
                ApplyMemoSearch();
                RenderTimestamps();
            };
            root.Children.Add(allButton);

            foreach (var folder in folders.OrderBy(f => f.Order).ToList())
            {
                var row = new Grid { Margin = new Thickness(8, 2, 8, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var folderDisplayName = type == "timestamp"
                    ? GetTimestampFolderDisplayName(folder)
                    : folder.Name;

                var select = new Button
                {
                    Content = (_activeFolderIdFor(type) == folder.Id ? "● " : "○ ") + folderDisplayName,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(4, 6, 4, 6)
                };
                select.Click += (s, e) =>
                {
                    SetActiveFolderId(type, folder.Id);
                    popup.IsOpen = false;
                    ApplyMemoSearch();
                    RenderTimestamps();
                };
                Grid.SetColumn(select, 0);
                row.Children.Add(select);

                var up = CreateSmallButton("↑");
                up.ToolTip = "上へ";
                up.Click += (s, e) =>
                {
                    int i = folders.IndexOf(folder);
                    if (i > 0)
                    {
                        folders.RemoveAt(i);
                        folders.Insert(i - 1, folder);
                        NormalizeFolderOrder(folders);
                        SaveFolderData(type);
                        popup.IsOpen = false;
                        ShowFolderPanel(target, type);
                    }
                };
                Grid.SetColumn(up, 1);
                row.Children.Add(up);

                var down = CreateSmallButton("↓");
                down.ToolTip = "下へ";
                down.Click += (s, e) =>
                {
                    int i = folders.IndexOf(folder);
                    if (i >= 0 && i < folders.Count - 1)
                    {
                        folders.RemoveAt(i);
                        folders.Insert(i + 1, folder);
                        NormalizeFolderOrder(folders);
                        SaveFolderData(type);
                        popup.IsOpen = false;
                        ShowFolderPanel(target, type);
                    }
                };
                Grid.SetColumn(down, 2);
                row.Children.Add(down);

                bool isUncategorized = string.Equals(folder.Name, "未分類", StringComparison.Ordinal);

                // 「未分類」は削除・名前変更を完全に禁止する。
                // ボタン自体を生成しないので、UI上にも表示されない。
                if (!isUncategorized)
                {
                    var rename = CreateSmallButton("✎");
                    rename.ToolTip = "名前変更";
                    rename.Click += (s, e) =>
                    {
                        var input = new TextBox
                        {
                            Text = folder.Name,
                            Foreground = Brushes.White,
                            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                            Margin = new Thickness(10)
                        };
                        var ok = new Button { Content = "決定", Margin = new Thickness(10, 0, 10, 10) };
                        var edit = new StackPanel { Width = 250 };
                        edit.Children.Add(input);
                        edit.Children.Add(ok);
                        var editPopup = new Popup
                        {
                            PlacementTarget = target,
                            Placement = PlacementMode.Bottom,
                            StaysOpen = false,
                            AllowsTransparency = true,
                            Child = new Border
                            {
                                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                                BorderThickness = new Thickness(1),
                                Child = edit
                            }
                        };
                        ok.Click += (_, _) =>
                        {
                            if (!string.IsNullOrWhiteSpace(input.Text))
                                folder.Name = input.Text.Trim();
                            SaveFolderData(type);
                            editPopup.IsOpen = false;
                            popup.IsOpen = false;
                            ShowFolderPanel(target, type);
                        };
                        editPopup.IsOpen = true;
                    };
                    Grid.SetColumn(rename, 3);
                    row.Children.Add(rename);

                    var remove = CreateSmallButton("×");
                    remove.Foreground = Brushes.Red;
                    remove.ToolTip = "フォルダ削除";
                    remove.Click += (s, e) =>
                    {
                        if (folders.Count <= 1)
                            return;

                        string replacementId = GetUncategorizedFolderId(type);
                        if (replacementId == folder.Id)
                            return;

                        if (type == "text")
                        {
                            foreach (var memo in _memoFolderIds.Keys.ToList())
                            {
                                if (ReferenceEquals(memo.Parent, TextMemosPanel) &&
                                    _memoFolderIds.TryGetValue(memo, out var id) && id == folder.Id)
                                    _memoFolderIds[memo] = replacementId;
                            }
                        }
                        else if (type == "list")
                        {
                            foreach (var memo in _memoFolderIds.Keys.ToList())
                            {
                                if (ReferenceEquals(memo.Parent, ListMemosPanel) &&
                                    _memoFolderIds.TryGetValue(memo, out var id) && id == folder.Id)
                                    _memoFolderIds[memo] = replacementId;
                            }
                        }
                        else
                        {
                            foreach (var box in _timestampBoxes)
                                if (box.FolderId == folder.Id)
                                    box.FolderId = replacementId;
                        }

                        folders.Remove(folder);
                        if (GetActiveFolderId(type) == folder.Id)
                            SetActiveFolderId(type, string.Empty);

                        NormalizeFolderOrder(folders);
                        SaveFolderData(type);
                        if (type == "timestamp")
                            SaveTimestamps();
                        popup.IsOpen = false;
                        ApplyMemoSearch();
                        RenderTimestamps();
                        ShowFolderPanel(target, type);
                    };
                    Grid.SetColumn(remove, 4);
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(remove);
                }
                root.Children.Add(row);
            }

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 10) };
            var add = new Button
            {
                Content = "＋ フォルダ追加",
                Background = new SolidColorBrush(Color.FromRgb(58, 63, 74)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 5, 8, 5)
            };
            add.Click += (s, e) =>
            {
                var folder = new FolderData { Name = $"フォルダ {folders.Count + 1}", Order = folders.Count };
                folders.Add(folder);
                SetActiveFolderId(type, folder.Id);
                SaveFolderData(type);
                popup.IsOpen = false;
                ShowFolderPanel(target, type);
                ApplyMemoSearch();
                RenderTimestamps();
            };
            bottom.Children.Add(add);

            var close = new Button
            {
                Content = "閉じる",
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(8, 5, 8, 5)
            };
            close.Click += (s, e) => popup.IsOpen = false;
            bottom.Children.Add(close);
            root.Children.Add(bottom);

            popup.Child = new Border
            {
                Background = root.Background,
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = root
            };
            popup.IsOpen = true;
        }

        private string _activeFolderIdFor(string type) => GetActiveFolderId(type);

        private void SaveFolderData(string type)
        {
            if (type == "timestamp")
            {
                // フォルダ単体のファイルと timestamps.json の両方へ保存する。
                // これでどちらか一方が失われても復元できる。
                SaveTimestampFolders();
                SaveTimestamps();
                return;
            }

            SaveMemos();
        }

        private void SaveMemos()
        {
            // 読み込み中、または読み込みに成功していない状態では絶対に保存しない。
            // 特にJSONが壊れている/読めない状態で起動したとき、空のUIを保存して
            // 正常だった既存データを消してしまうのを防ぐ。
            if (_isLoadingMemos || !_memosLoaded)
                return;

            try
            {
                EnsureUserDataDirectory();
                EnsureDefaultFolder(_textFolders);
                EnsureDefaultFolder(_listFolders);

                var data = new MemoDataFile
                {
                    TextFolders = _textFolders.ToList(),
                    ListFolders = _listFolders.ToList()
                };
                EnsureDefaultFolder(data.TextFolders);
                EnsureDefaultFolder(data.ListFolders);
                _textFolders.Clear(); _textFolders.AddRange(data.TextFolders);
                _listFolders.Clear(); _listFolders.AddRange(data.ListFolders);

                // -------------------------
                // テキストメモ
                // -------------------------
                int textOrder = 0;
                IEnumerable<Border> textMemoSaveCards =
                    TextMemosPanel.Children.OfType<Border>();
                if (ReferenceEquals(_floatingAllMemoSourcePanel, TextMemosPanel) &&
                    _floatingAllMemoContentPanel != null)
                {
                    textMemoSaveCards = textMemoSaveCards
                        .Concat(_floatingAllMemoContentPanel.Children.OfType<Border>());
                }

                foreach (var child in textMemoSaveCards)
                {
                    var boxes = child.Descendants()
                        .OfType<TextBox>()
                        .ToList();

                    if (boxes.Count == 0)
                        continue;

                    var title = boxes[0].Text;
                    var text = boxes.Count >= 2
                        ? boxes[1].Text
                        : string.Empty;

                    bool collapsed = false;

                    // メモ本文が非表示なら折りたたみ状態
                    if (child.Descendants()
                        .OfType<TextBox>()
                        .Skip(1)
                        .FirstOrDefault() is TextBox bodyBox)
                    {
                        collapsed = bodyBox.Visibility != Visibility.Visible;
                    }

                    data.TextMemos.Add(new MemoData
                    {
                        Title = title,
                        Text = text,
                        IsCollapsed = collapsed,
                        Order = textOrder++,
                        FolderId = _memoFolderIds.TryGetValue(child, out var textFolderId)
                            ? textFolderId
                            : _textFolders.FirstOrDefault()?.Id ?? string.Empty
                    });
                }

                // -------------------------
                // リストメモ
                // -------------------------
                int listOrder = 0;
                IEnumerable<Border> listMemoSaveCards =
                    ListMemosPanel.Children.OfType<Border>();
                if (ReferenceEquals(_floatingAllMemoSourcePanel, ListMemosPanel) &&
                    _floatingAllMemoContentPanel != null)
                {
                    listMemoSaveCards = listMemoSaveCards
                        .Concat(_floatingAllMemoContentPanel.Children.OfType<Border>());
                }

                foreach (var child in listMemoSaveCards)
                {
                    var boxes = child.Descendants()
                        .OfType<TextBox>()
                        .ToList();

                    if (boxes.Count == 0)
                        continue;

                    var listData = new ListMemoData
                    {
                        Title = boxes[0].Text,
                        Order = listOrder++,
                        FolderId = _memoFolderIds.TryGetValue(child, out var listFolderId)
                            ? listFolderId
                            : _listFolders.FirstOrDefault()?.Id ?? string.Empty
                    };

                    var itemRows = child.Descendants()
                        .OfType<StackPanel>()
                        .Where(p =>
                            p.Children.OfType<CheckBox>().Count() >= 2 &&
                            p.Children.OfType<TextBox>().Any())
                        .ToList();

                    foreach (var row in itemRows)
                    {
                        var checks = row.Children
                            .OfType<CheckBox>()
                            .ToList();

                        var textBox = row.Children
                            .OfType<TextBox>()
                            .FirstOrDefault();

                        if (checks.Count >= 2 && textBox != null)
                        {
                            listData.Items.Add(new ListMemoItemData
                            {
                                Text = textBox.Text,
                                IsChecked1 = checks[0].IsChecked == true,
                                IsChecked2 = checks[1].IsChecked == true
                            });
                        }
                    }

                    var itemsPanel = child.Descendants()
                        .OfType<StackPanel>()
                        .FirstOrDefault(p =>
                            p.Children.OfType<CheckBox>().Any());

                    listData.IsCollapsed =
                        itemsPanel?.Visibility != Visibility.Visible;

                    data.ListMemos.Add(listData);
                }

                var json = JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                // 直接memos.jsonへ書かず、一時ファイル→バックアップ→置換の順で保存。
                // アプリ終了やクラッシュの途中でJSONが空/壊れるのを防ぐ。
                string tempPath = MemoBackupPath + ".tmp";
                string backupPath = MemoBackupPath + ".bak";

                File.WriteAllText(tempPath, json);

                if (File.Exists(MemoBackupPath))
                {
                    try
                    {
                        File.Copy(MemoBackupPath, backupPath, true);
                    }
                    catch (Exception backupEx)
                    {
                        Debug.WriteLine($"Memo backup failed: {backupEx}");
                    }
                }

                File.Move(tempPath, MemoBackupPath, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save memos failed: {ex}");
            }
        }

        private void LoadMemos()
        {
            if (_memosLoaded)
                return;

            _isLoadingMemos = true;
            try
            {
                EnsureUserDataDirectory();

                string? json = null;
                bool loadedFromBackup = false;

                // まず通常ファイルを読む。壊れている場合はバックアップを試す。
                if (File.Exists(MemoBackupPath))
                {
                    try
                    {
                        json = File.ReadAllText(MemoBackupPath);
                        // JSONとしてここで一度検証しておく。
                        _ = JsonSerializer.Deserialize<MemoDataFile>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Primary memos.json load failed: {ex}");

                        string backupPath = MemoBackupPath + ".bak";
                        if (File.Exists(backupPath))
                        {
                            try
                            {
                                json = File.ReadAllText(backupPath);
                                _ = JsonSerializer.Deserialize<MemoDataFile>(json);
                                loadedFromBackup = true;
                                Debug.WriteLine("Loaded memos from backup.");
                            }
                            catch (Exception backupEx)
                            {
                                Debug.WriteLine($"Backup memos.json load failed: {backupEx}");
                                json = null;
                            }
                        }
                    }
                }
                else
                {
                    // 初回起動など、まだ保存ファイルが存在しない場合だけ
                    // Window_Loadedで初期メモを作成できるようにする。
                    _memoFileWasMissingOnLoad = true;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    if (File.Exists(MemoBackupPath))
                    {
                        // 既存ファイルがあるのに読み込めなかった場合は、
                        // 空データとして成功扱いにしない。
                        // これにより起動時/終了時の上書きを防止する。
                        _memosLoaded = false;
                        return;
                    }

                    _textFolders.Clear();
                    _listFolders.Clear();
                    EnsureDefaultFolder(_textFolders);
                    EnsureDefaultFolder(_listFolders);
                    _memoFolderIds.Clear();
                    TextMemosPanel.Children.Clear();
                    ListMemosPanel.Children.Clear();

                    _memosLoaded = true;
                    return;
                }

                var data = JsonSerializer.Deserialize<MemoDataFile>(json);
                if (data == null)
                {
                    _memosLoaded = false;
                    return;
                }

                _textFolders.Clear();
                _listFolders.Clear();
                _textFolders.AddRange(data.TextFolders ?? new List<FolderData>());
                _listFolders.AddRange(data.ListFolders ?? new List<FolderData>());
                EnsureDefaultFolder(_textFolders);
                EnsureDefaultFolder(_listFolders);
                _memoFolderIds.Clear();

                TextMemosPanel.Children.Clear();
                ListMemosPanel.Children.Clear();

                foreach (var memo in (data.TextMemos ?? new List<MemoData>()).OrderBy(m => m.Order))
                {
                    var border = CreateMemoContainer(
                        memo.Title,
                        memo.Text,
                        TextMemosPanel);

                    TextMemosPanel.Children.Add(border);
                    _memoFolderIds[border] =
                        string.IsNullOrWhiteSpace(memo.FolderId)
                            ? _textFolders[0].Id
                            : (_textFolders.Any(f => f.Id == memo.FolderId)
                                ? memo.FolderId
                                : _textFolders[0].Id);

                    AddFolderSelectorToMemoCard(border, "text");

                    if (memo.IsCollapsed)
                    {
                        var bodyBox = border.Descendants()
                            .OfType<TextBox>()
                            .Skip(1)
                            .FirstOrDefault();

                        var resizeGrip = border.Descendants()
                            .OfType<Grid>()
                            .FirstOrDefault(g => g.Cursor == Cursors.SizeNS);

                        if (bodyBox != null)
                            bodyBox.Visibility = Visibility.Collapsed;

                        if (resizeGrip != null)
                            resizeGrip.Visibility = Visibility.Collapsed;
                    }
                }

                foreach (var memo in (data.ListMemos ?? new List<ListMemoData>()).OrderBy(m => m.Order))
                {
                    var border = CreateListMemoContainer(
                        memo.Title,
                        ListMemosPanel);

                    ListMemosPanel.Children.Add(border);
                    _memoFolderIds[border] =
                        string.IsNullOrWhiteSpace(memo.FolderId)
                            ? _listFolders[0].Id
                            : (_listFolders.Any(f => f.Id == memo.FolderId)
                                ? memo.FolderId
                                : _listFolders[0].Id);

                    AddFolderSelectorToMemoCard(border, "list");

                    var itemsPanel = border.Descendants()
                        .OfType<StackPanel>()
                        .FirstOrDefault(p => p.Children.OfType<CheckBox>().Any());

                    if (itemsPanel != null)
                    {
                        itemsPanel.Children.Clear();

                        foreach (var item in memo.Items ?? new List<ListMemoItemData>())
                        {
                            var row = CreateListItemRow(() => { });

                            var checks = row.Children.OfType<CheckBox>().ToList();
                            var textBox = row.Children.OfType<TextBox>().FirstOrDefault();

                            if (checks.Count >= 2)
                            {
                                checks[0].IsChecked = item.IsChecked1;
                                checks[1].IsChecked = item.IsChecked2;
                            }

                            if (textBox != null)
                                textBox.Text = item.Text;

                            itemsPanel.Children.Add(row);
                        }

                        itemsPanel.Visibility =
                            memo.IsCollapsed
                                ? Visibility.Collapsed
                                : Visibility.Visible;
                    }
                }

                ApplyMemoSearch();
                _memosLoaded = true;

                // 壊れたprimaryから.bakで復旧した場合、復旧した内容を通常ファイルへ戻す。
                // SaveMemos()は_memosLoaded=trueかつ_isLoadingMemos=falseになってから呼ぶ。
                if (loadedFromBackup)
                {
                    _isLoadingMemos = false;
                    SaveMemos();
                    _isLoadingMemos = true;
                }
            }
            catch (Exception ex)
            {
                // 読み込み失敗時は _memosLoaded をtrueにしない。
                // Window_Loaded/Window_Closedの保存処理から既存データを守る。
                _memosLoaded = false;
                Debug.WriteLine($"Load memos failed: {ex}");
            }
            finally
            {
                _isLoadingMemos = false;
            }
        }


        private TextBox? GetTitleBox(Border container)
        {
            var header = container.Descendants()
                .OfType<Grid>()
                .FirstOrDefault(g =>
                    g.Children.OfType<StackPanel>()
                        .Any(p => p.Orientation == Orientation.Horizontal &&
                                  p.Children.OfType<Button>().Any()));

            if (header == null)
                return null;

            return header.Children.OfType<TextBox>().FirstOrDefault();
        }

        private void AddFolderSelectorToMemoCard(Border border, string type)
        {
            var header = border.Descendants()
                .OfType<Grid>()
                .FirstOrDefault(g =>
                    g.Children.OfType<StackPanel>()
                        .Any(p => p.Orientation == Orientation.Horizontal &&
                                  p.Children.OfType<Button>().Any()));
            var buttons = header?.Children.OfType<StackPanel>()
                .FirstOrDefault(p => p.Orientation == Orientation.Horizontal);
            if (buttons == null)
                return;

            if (buttons.Children.OfType<Button>().Any(b => b.Uid == "MemoFolderButton"))
                return;

            var folderButton = CreateFolderAssignButton(
                type,
                () => _memoFolderIds.TryGetValue(border, out var id) ? id : string.Empty,
                id =>
                {
                    _memoFolderIds[border] = id;
                    SaveMemos();
                    ApplyMemoSearch();
                });
            folderButton.Uid = "MemoFolderButton";
            buttons.Children.Insert(0, folderButton);
        }

        private void BtnOpenTextFolders_Click(object sender, RoutedEventArgs e)
            => ShowFolderPanel((Button)sender, "text");

        private void BtnOpenListFolders_Click(object sender, RoutedEventArgs e)
            => ShowFolderPanel((Button)sender, "list");

        private void BtnOpenTimestampFolders_Click(object sender, RoutedEventArgs e)
            => ShowFolderPanel((Button)sender, "timestamp");

        private void BtnAddTextMemo_Click(object? sender, RoutedEventArgs? e)
        {
            var memo = CreateMemoContainer(ownerPanel: TextMemosPanel);
            _memoFolderIds[memo] = string.IsNullOrEmpty(_activeTextFolderId)
                ? GetUncategorizedFolderId("text")
                : _activeTextFolderId;
            TextMemosPanel.Children.Insert(0, memo);
            ApplyMemoSearch();
            SaveMemos();
        }

        private void RemoveMemoOrClear(Border memoContainer, Panel panel)
        {
            if (panel.Children.Count <= 1)
            {
                // panelがリストメモなら、ResetListMemoContentを実行
                if (panel == ListMemosPanel)
                {
                    ResetListMemoContent(memoContainer);
                }
                else
                {
                    // 通常のメモならResetMemoContentを実行
                    ResetMemoContent(memoContainer);
                }

                SaveMemos();
                return;
            }

            panel.Children.Remove(memoContainer);
            SaveMemos();
        }

        private void ResetMemoContent(Border memoContainer)
        {
            var boxes = memoContainer.Descendants().OfType<TextBox>().ToList();

            if (boxes.Count > 0)
            {
                // タイトルを無題に戻す
                boxes[0].Text = "無題";
            }

            if (boxes.Count > 1)
            {
                // 本文を空に戻す
                boxes[1].Text = string.Empty;
            }
        }

        private void ResetListMemoContent(Border memoContainer)
        {
            if (memoContainer.Child is not StackPanel root)
                return;

            // ヘッダー
            var header = root.Children
                .OfType<Grid>()
                .FirstOrDefault();

            if (header != null)
            {
                // タイトルを「リスト」に戻す
                var titleBox = header.Children
                    .OfType<TextBox>()
                    .FirstOrDefault();

                if (titleBox != null)
                {
                    titleBox.Text = "リスト";
                }
            }

            // リスト項目部分
            var itemsPanel = root.Children
                .OfType<StackPanel>()
                .FirstOrDefault();

            if (itemsPanel != null)
            {
                // 既存の項目を全部削除
                itemsPanel.Children.Clear();

                // デフォルトの項目を1個作る
                var row = CreateListItemRow(() => { });
                itemsPanel.Children.Add(row);
            }
        }

        

        

        private void LoadTasks()
        {
            try
            {
                if (!File.Exists(TasksPath))
                {
                    return;
                }

                var json = File.ReadAllText(TasksPath);
                var loaded = JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
                _tasks.Clear();
                _tasks.AddRange(loaded);
                RenderTasks();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load tasks failed: {ex.Message}");
            }
        }

        private void SaveTasks()
        {
            try
            {
                File.WriteAllText(TasksPath, JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save tasks failed: {ex.Message}");
            }
        }

        private void BtnAddTask_Click(object sender, RoutedEventArgs e)
        {
            ShowAddTaskDialog(DateTime.Today);
        }

        private void BtnClearCompletedTasks_Click(object sender, RoutedEventArgs e)
        {
            if (_tasks.All(t => !t.IsDone))
            {
                return;
            }

            ShowConfirmPopup("完了済みタスクを全て削除しますか？", () =>
            {    
                // Shiftキーを押していなければ何もしない
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    return;
                }
                _tasks.RemoveAll(t => t.IsDone);
                SaveTasks();
                RenderTasks();
            });
        }

        private void RenderTasks()
        {
            TasksPanel.Children.Clear();

            var root = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var monthHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prevButton = new Button
            {
                Content = "‹", Width = 40, Height = 32,
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 22, Cursor = Cursors.Hand
            };
            prevButton.Click += (s, e) =>
            {
                _taskCalendarMonth = _taskCalendarMonth.AddMonths(-1);
                RenderTasks();
            };

            var monthText = new TextBlock
            {
                Text = $"{_taskCalendarMonth:yyyy年M月}",
                Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nextButton = new Button
            {
                Content = "›", Width = 40, Height = 32,
                Background = Brushes.Transparent, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontSize = 22, Cursor = Cursors.Hand
            };
            nextButton.Click += (s, e) =>
            {
                _taskCalendarMonth = _taskCalendarMonth.AddMonths(1);
                RenderTasks();
            };

            var todayButton = new Button
            {
                Content = "今日", Width = 52, Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(58, 63, 74)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0)
            };
            todayButton.Click += (s, e) =>
            {
                _taskCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                RenderTasks();
            };

            Grid.SetColumn(prevButton, 0);
            Grid.SetColumn(monthText, 1);
            monthHeader.Children.Add(prevButton);
            monthHeader.Children.Add(monthText);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(nextButton);
            right.Children.Add(todayButton);
            Grid.SetColumn(right, 2);
            monthHeader.Children.Add(right);

            Grid.SetRow(monthHeader, 0);
            root.Children.Add(monthHeader);

            var weekHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            for (int i = 0; i < 7; i++)
                weekHeader.ColumnDefinitions.Add(new ColumnDefinition());

            string[] weekdays = { "日", "月", "火", "水", "木", "金", "土" };
            for (int i = 0; i < 7; i++)
            {
                var t = new TextBlock
                {
                    Text = weekdays[i],
                    Foreground = i == 0 ? Brushes.LightCoral :
                                 i == 6 ? Brushes.LightSkyBlue : Brushes.LightGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14
                };
                Grid.SetColumn(t, i);
                weekHeader.Children.Add(t);
            }
            Grid.SetRow(weekHeader, 1);
            root.Children.Add(weekHeader);

            // 横幅を広くし、各日付マスを大きくする。
            var calendar = new Grid { MinWidth = 980 };
            for (int i = 0; i < 7; i++)
                calendar.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 132 });
            for (int i = 0; i < 6; i++)
                calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });

            DateTime first = _taskCalendarMonth;
            int offset = (int)first.DayOfWeek;
            DateTime today = DateTime.Today;

            for (int cell = 0; cell < 42; cell++)
            {
                DateTime date = first.AddDays(cell - offset);
                bool inMonth = date.Month == first.Month;
                bool isPast = date.Date < today;
                bool isToday = date.Date == today;

                var dayPanel = new StackPanel();

                dayPanel.Children.Add(new TextBlock
                {
                    Text = date.Day.ToString(),
                    Foreground = !inMonth ? Brushes.DimGray :
                                 date.DayOfWeek == DayOfWeek.Sunday ? Brushes.LightCoral :
                                 date.DayOfWeek == DayOfWeek.Saturday ? Brushes.LightSkyBlue :
                                 Brushes.White,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 14,
                    Margin = new Thickness(7, 5, 7, 4)
                });

                foreach (var task in _tasks.Where(t => t.DueAt.Date == date.Date)
                                           .OrderBy(t => t.IsDone)
                                           .ThenBy(t => t.DueAt))
                {
                    var taskBorder = new Border
                    {
                        Background = task.IsDone
                            ? new SolidColorBrush(Color.FromRgb(45, 45, 45))
                            : new SolidColorBrush(Color.FromRgb(58, 63, 74)),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(4, 2, 4, 2),
                        Padding = new Thickness(5, 4, 5, 4),
                        Cursor = Cursors.Hand,
                        Opacity = task.IsDone ? 0.55 : 1.0
                    };

                    taskBorder.Child = new TextBlock
                    {
                        Text = $"{task.DueAt:HH:mm}  {task.Title}",
                        Foreground = task.IsDone ? Brushes.LightGray : Brushes.White,
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = $"{task.Title}\n{task.Body}\n期限: {task.DueAt:yyyy/MM/dd HH:mm}\nダブルクリックで編集"
                    };

                    // ダブルクリックでは完了状態を切り替えず、編集画面を開く。
                    taskBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2)
                        {
                            ShowEditTaskDialog(task);
                            e.Handled = true;
                        }
                    };

                    dayPanel.Children.Add(taskBorder);
                }

                var dayBorder = new Border
                {
                    BorderBrush = isToday
                        ? new SolidColorBrush(Color.FromRgb(90, 140, 220))
                        : new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                    BorderThickness = new Thickness(isToday ? 2 : 1),
                    Background = isPast
                        ? new SolidColorBrush(Color.FromRgb(25, 25, 25))
                        : new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2),
                    Child = dayPanel,
                    Opacity = inMonth ? 1.0 : 0.55
                };

                // タスク部分ではなく空いている日付マスをダブルクリックして追加。
                dayBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2)
                        return;

                    if (date.Date < DateTime.Today)
                    {
                        e.Handled = true;
                        return;
                    }

                    ShowAddTaskDialog(date.Date);
                    e.Handled = true;
                };

                Grid.SetColumn(dayBorder, cell % 7);
                Grid.SetRow(dayBorder, cell / 7);
                calendar.Children.Add(dayBorder);
            }

            Grid.SetRow(calendar, 2);
            root.Children.Add(calendar);
            TasksPanel.Children.Add(root);
        }

        private void ShowAddTaskDialog(DateTime selectedDate)
        {
            ShowTaskEditDialog(null, selectedDate.Date);
        }

        private void ShowEditTaskDialog(TaskItem task)
        {
            ShowTaskEditDialog(task, task.DueAt.Date);
        }

        private void ShowTaskEditDialog(TaskItem? task, DateTime initialDate)
        {
            bool editing = task != null;
            DateTime selectedDueDate = editing ? task!.DueAt.Date : initialDate.Date;

            if (!editing && selectedDueDate < DateTime.Today)
                return;

            var window = new Window
            {
                Title = editing ? "タスクを編集" : $"{selectedDueDate:yyyy年M月d日} のタスクを追加",
                Width = 460,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleLabel = new TextBlock
            {
                Text = "タイトル",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(titleLabel, 0);
            root.Children.Add(titleLabel);

            var titleBox = new TextBox
            {
                Text = editing ? task!.Title : "",
                Height = 32,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(titleBox, 1);
            root.Children.Add(titleBox);

            var bodyLabel = new TextBlock
            {
                Text = "内容",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(bodyLabel, 2);
            root.Children.Add(bodyLabel);

            var bodyBox = new TextBox
            {
                Text = editing ? task!.Body : "",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(bodyBox, 3);
            root.Children.Add(bodyBox);

            var deadlinePanel = new StackPanel { Orientation = Orientation.Horizontal };

            deadlinePanel.Children.Add(new TextBlock
            {
                Text = "期限日",
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var dateButton = new Button
            {
                Content = selectedDueDate.ToString("yyyy/MM/dd"),
                Width = 125,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(58, 63, 74)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var timeBox = new TextBox
            {
                Text = editing ? task!.DueAt.ToString("HH:mm") : "18:00",
                Width = 72,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = Brushes.Gray,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                MaxLength = 5
            };

            // タイムスタンプと同じく、常に HH:mm の固定形式を維持する。
            timeBox.PreviewTextInput += TaskDeadlineTimeBox_PreviewTextInput;
            timeBox.TextChanged += TaskDeadlineTimeBox_TextChanged;
            timeBox.PreviewKeyDown += TaskDeadlineTimeBox_PreviewKeyDown;
            timeBox.LostFocus += TaskDeadlineTimeBox_LostFocus;

            dateButton.Click += (s, e) =>
            {
                ShowTaskDeadlineCalendar(
                    selectedDueDate,
                    date =>
                    {
                        selectedDueDate = date.Date;
                        dateButton.Content = selectedDueDate.ToString("yyyy/MM/dd");
                    });
            };

            deadlinePanel.Children.Add(dateButton);
            deadlinePanel.Children.Add(timeBox);
            Grid.SetRow(deadlinePanel, 4);
            root.Children.Add(deadlinePanel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            if (editing)
            {
                var deleteButton = new Button
                {
                    Content = "削除",
                    Width = 75,
                    Height = 30,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(90, 45, 45)),
                    Foreground = Brushes.White
                };
                deleteButton.Click += (s, e) =>
                {
                    _tasks.Remove(task!);
                    SaveTasks();
                    RenderTasks();
                    window.Close();
                };
                buttons.Children.Add(deleteButton);
            }

            var cancel = new Button
            {
                Content = "キャンセル",
                Width = 90,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancel.Click += (s, e) => window.Close();

            var save = new Button
            {
                Content = editing ? "保存" : "追加",
                Width = 90,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(58, 63, 74)),
                Foreground = Brushes.White
            };

            save.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(titleBox.Text))
                {
                    MessageBox.Show(window, "タイトルを入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string normalizedDeadlineTime = NormalizeTaskDeadlineTime(timeBox.Text);
                if (!TaskTimeRegex.IsMatch(normalizedDeadlineTime))
                {
                    MessageBox.Show(window, "期限の時刻を HH:mm 形式で入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                timeBox.Text = normalizedDeadlineTime;

                int deadlineHour = int.Parse(normalizedDeadlineTime.Substring(0, 2));
                int deadlineMinute = int.Parse(normalizedDeadlineTime.Substring(3, 2));
                var dueTime = new TimeSpan(deadlineHour, deadlineMinute, 0);

                var dueAt = selectedDueDate.Date.Add(dueTime);
                if (dueAt <= DateTime.Now)
                {
                    MessageBox.Show(window, "過去の日時を期限には指定できません。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (editing)
                {
                    task!.Title = titleBox.Text.Trim();
                    task.Body = bodyBox.Text;
                    if (task.DueAt != dueAt)
                        task.Notified = false;
                    task.DueAt = dueAt;
                }
                else
                {
                    _tasks.Insert(0, new TaskItem
                    {
                        Title = titleBox.Text.Trim(),
                        Body = bodyBox.Text,
                        DueAt = dueAt,
                        IsDone = false,
                        Notified = false
                    });
                }

                SaveTasks();
                _taskCalendarMonth = new DateTime(dueAt.Year, dueAt.Month, 1);
                RenderTasks();
                window.Close();
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);

            window.Content = root;
            window.Loaded += (s, e) => titleBox.Focus();
            window.ShowDialog();
        }

        // タスクの期限日を、カレンダー上でダブルクリックして指定する。
        // タスクの期限日を、横長のカレンダー上で選択する。
        private void ShowTaskDeadlineCalendar(DateTime initialDate, Action<DateTime> onSelected)
        {
            DateTime month = new DateTime(initialDate.Year, initialDate.Month, 1);
            DateTime selectedDate = initialDate.Date;

            var window = new Window
            {
                Title = "期限日を選択",
                Width = 1050,
                Height = 720,
                MinWidth = 900,
                MinHeight = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = true
            };

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = $"{month:yyyy年M月}",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // クリックイベントより先に宣言しておく。
            Action RenderDeadlineCalendar = null!;

            var prev = new Button
            {
                Content = "‹",
                Width = 52,
                Height = 38,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 28,
                Cursor = Cursors.Hand
            };
            prev.Click += (s, e) =>
            {
                month = month.AddMonths(-1);
                RenderDeadlineCalendar();
            };

            var next = new Button
            {
                Content = "›",
                Width = 52,
                Height = 38,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 28,
                Cursor = Cursors.Hand
            };
            next.Click += (s, e) =>
            {
                month = month.AddMonths(1);
                RenderDeadlineCalendar();
            };

            Grid.SetColumn(prev, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(next, 2);
            header.Children.Add(prev);
            header.Children.Add(title);
            header.Children.Add(next);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var hint = new TextBlock
            {
                Text = "期限にしたい日付をダブルクリックしてください（過去の日付は選択できません）",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            Grid.SetRow(hint, 1);
            root.Children.Add(hint);

            var calendarArea = new Grid();
            calendarArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            calendarArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(calendarArea, 2);
            root.Children.Add(calendarArea);

            var weekHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int i = 0; i < 7; i++)
                weekHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] weekdays = { "日", "月", "火", "水", "木", "金", "土" };
            for (int i = 0; i < 7; i++)
            {
                var weekday = new TextBlock
                {
                    Text = weekdays[i],
                    Foreground = i == 0 ? Brushes.LightCoral :
                                 i == 6 ? Brushes.LightSkyBlue :
                                 Brushes.LightGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15
                };
                Grid.SetColumn(weekday, i);
                weekHeader.Children.Add(weekday);
            }
            Grid.SetRow(weekHeader, 0);
            calendarArea.Children.Add(weekHeader);

            var calendarHost = new Grid();
            Grid.SetRow(calendarHost, 1);
            calendarArea.Children.Add(calendarHost);

            RenderDeadlineCalendar = () =>
            {
                title.Text = $"{month:yyyy年M月}";
                calendarHost.Children.Clear();

                var calendar = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                for (int i = 0; i < 7; i++)
                    calendar.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int i = 0; i < 6; i++)
                    calendar.RowDefinitions.Add(
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                DateTime today = DateTime.Today;
                int offset = (int)month.DayOfWeek;

                for (int cell = 0; cell < 42; cell++)
                {
                    DateTime date = month.AddDays(cell - offset);
                    bool inMonth = date.Month == month.Month;
                    bool past = date.Date < today;
                    bool selected = date.Date == selectedDate;

                    var border = new Border
                    {
                        Background = selected
                            ? new SolidColorBrush(Color.FromRgb(55, 75, 105))
                            : new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(3),
                        Opacity = inMonth ? 1.0 : 0.4,
                        Cursor = past ? Cursors.Arrow : Cursors.Hand,
                        MinHeight = 78
                    };

                    var dayText = new TextBlock
                    {
                        Text = date.Day.ToString(),
                        Foreground = past ? Brushes.DimGray :
                                     date.DayOfWeek == DayOfWeek.Sunday ? Brushes.LightCoral :
                                     date.DayOfWeek == DayOfWeek.Saturday ? Brushes.LightSkyBlue :
                                     Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 18,
                        FontWeight = selected ? FontWeights.Bold : FontWeights.Normal
                    };

                    border.Child = dayText;

                    border.MouseLeftButtonDown += (s, e) =>
                    {
                        if (past || !inMonth)
                            return;

                        selectedDate = date.Date;

                        // 1回クリックでは日付を選択するだけ。
                        // 2回目のクリックで確定してカレンダーを閉じる。
                        if (e.ClickCount >= 2)
                        {
                            onSelected(selectedDate);
                            window.DialogResult = true;
                        }

                        e.Handled = true;
                    };

                    Grid.SetColumn(border, cell % 7);
                    Grid.SetRow(border, cell / 7);
                    calendar.Children.Add(border);
                }

                calendarHost.Children.Add(calendar);
            };

            window.Content = root;

            window.Loaded += (s, e) =>
            {
                RenderDeadlineCalendar();
                window.Activate();
                window.Focus();
            };

            // Loaded 後に必ず表示されるようにする。
            window.ShowDialog();
        }

        private static string NormalizeTaskDeadlineTime(string? value)
        {
            string digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

            if (digits.Length > 4)
                digits = digits.Substring(0, 4);

            digits = digits.PadRight(4, '0');

            int hour = Math.Clamp(int.Parse(digits.Substring(0, 2)), 0, 23);
            int minute = Math.Clamp(int.Parse(digits.Substring(2, 2)), 0, 59);

            return $"{hour:00}:{minute:00}";
        }

        private void TaskDeadlineTimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!Regex.IsMatch(e.Text, @"^\d$"))
            {
                e.Handled = true;
                return;
            }

            if (tb.Text.Length != 5)
                tb.Text = NormalizeTaskDeadlineTime(tb.Text);

            int caret = tb.CaretIndex;

            // 選択範囲がある場合は、その範囲を数字入力で置き換える。
            if (tb.SelectionLength > 0)
            {
                int start = tb.SelectionStart;
                int end = Math.Min(5, start + tb.SelectionLength);

                char[] selectedChars = tb.Text.ToCharArray();
                int target = start;

                while (target < end && (target == 2 || target > 4))
                    target++;

                if (target >= 5)
                    target = 4;

                selectedChars[target] = e.Text[0];

                _isUpdatingTimeBox = true;
                tb.Text = new string(selectedChars);
                tb.CaretIndex = Math.Min(target + 1, 5);
                _isUpdatingTimeBox = false;
                e.Handled = true;
                return;
            }

            // HH:mm の数字部分だけを左から順に上書きする。
            if (caret == 2)
                caret++;

            if (caret > 4)
                caret = 4;

            char[] chars = tb.Text.ToCharArray();
            chars[caret] = e.Text[0];

            int nextCaret = caret + 1;
            if (nextCaret == 2)
                nextCaret++;
            if (nextCaret > 4)
                nextCaret = 4;

            _isUpdatingTimeBox = true;
            tb.Text = new string(chars);
            tb.CaretIndex = nextCaret;
            _isUpdatingTimeBox = false;

            e.Handled = true;
        }

        private void TaskDeadlineTimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTimeBox)
                return;

            if (sender is not TextBox tb)
                return;

            _isUpdatingTimeBox = true;

            string normalized = NormalizeTaskDeadlineTime(tb.Text);

            if (tb.Text != normalized)
            {
                int caret = tb.CaretIndex;
                tb.Text = normalized;
                tb.CaretIndex = Math.Min(Math.Max(caret, 0), tb.Text.Length);
            }

            _isUpdatingTimeBox = false;
        }

        private void TaskDeadlineTimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (tb.Text.Length != 5)
                tb.Text = NormalizeTaskDeadlineTime(tb.Text);

            if (e.Key == Key.Back)
            {
                int caret = tb.CaretIndex;

                if (caret > 0)
                {
                    int target = caret - 1;
                    if (target == 2)
                        target--;

                    char[] chars = tb.Text.ToCharArray();
                    chars[target] = '0';

                    _isUpdatingTimeBox = true;
                    tb.Text = new string(chars);
                    tb.CaretIndex = target;
                    _isUpdatingTimeBox = false;
                }

                e.Handled = true;
                return;
            }

            if (e.Key != Key.Up && e.Key != Key.Down)
                return;

            e.Handled = true;

            int delta = e.Key == Key.Up ? 1 : -1;
            int hour = int.Parse(tb.Text.Substring(0, 2));
            int minute = int.Parse(tb.Text.Substring(3, 2));
            int caretIndex = tb.CaretIndex;
            int newCaret = caretIndex;

            if (caretIndex == 0 || caretIndex == 1)
            {
                // 時の10/1の位を変更。
                int tens = hour / 10;
                int ones = hour % 10;

                if (caretIndex == 0)
                    tens = (tens + delta + 10) % 10;
                else
                    ones = (ones + delta + 10) % 10;

                hour = tens * 10 + ones;

                // 23時を超えないように補正。
                if (hour > 23)
                    hour = delta > 0 ? 0 : 23;

                newCaret = caretIndex;
            }
            else if (caretIndex == 2 || caretIndex == 3)
            {
                // 分の全体/10の位を変更。
                if (caretIndex == 2)
                {
                    hour = (hour + delta + 24) % 24;
                    newCaret = 2;
                }
                else
                {
                    int tens = minute / 10;
                    int ones = minute % 10;
                    tens = (tens + delta + 6) % 6;
                    minute = tens * 10 + ones;
                    newCaret = 3;
                }
            }
            else
            {
                minute = (minute + delta + 60) % 60;
                newCaret = 4;
            }

            _isUpdatingTimeBox = true;
            tb.Text = $"{hour:00}:{minute:00}";
            tb.CaretIndex = newCaret;
            _isUpdatingTimeBox = false;
        }

        private void TaskDeadlineTimeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            tb.Text = NormalizeTaskDeadlineTime(tb.Text);

            // 正規化後も範囲外なら安全な値へ戻す。
            if (!TaskTimeRegex.IsMatch(tb.Text))
                tb.Text = "00:00";
        }

        private UIElement CreateTaskDivider()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 8, 0), Background = Brushes.Gray });
            panel.Children.Add(new TextBlock
            {
                Text = "完了タスク",
                Foreground = Brushes.LightGray,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new Separator { Margin = new Thickness(8, 8, 0, 0), Background = Brushes.Gray });
            return panel;
        }

        private Button CreateSmallButton(string content) => new Button
        {
            Content = content,
            Width = 22,
            Height = 22,
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        // タスク作成
        private Border CreateTaskCard(TaskItem task)
        {
            bool isOverdue = !task.IsDone && task.DueAt <= DateTime.Now;
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var stack = new StackPanel();
            var header = new DockPanel();

            var done = new CheckBox
            {
                IsChecked = task.IsDone,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            done.Checked += (s, e) => UpdateTaskDone(task, true);
            done.Unchecked += (s, e) => UpdateTaskDone(task, false);

            var titleBox = new TextBox
            {
                Text = task.Title,
                Foreground = isOverdue ? Brushes.Red : Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                IsReadOnly = isOverdue
            };
            titleBox.LostFocus += (s, e) =>
            {
                task.Title = titleBox.Text;
                SaveTasks();
                RenderTasks();
            };

            var deleteButton = new Button
            {
                Style = null,
                Content = "×",
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Foreground = Brushes.Red,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            deleteButton.Foreground = new SolidColorBrush(Colors.Red);

            deleteButton.Click += (s, e) =>
            {
                // Shiftキーを押している時だけ削除
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    return;
                }

                _tasks.Remove(task);
                SaveTasks();
                RenderTasks();
            };

            var collapseButton = CreateSmallButton(task.IsCollapsed ? "∨" : "∧");
            collapseButton.Click += (s, e) =>
            {
                task.IsCollapsed = !task.IsCollapsed;
                SaveTasks();
                RenderTasks();
            };
            
            var taskButtons = new StackPanel { Orientation = Orientation.Horizontal };
            taskButtons.Children.Add(collapseButton);
            taskButtons.Children.Add(deleteButton);
            DockPanel.SetDock(taskButtons, Dock.Right);
            header.Children.Add(taskButtons);
            DockPanel.SetDock(done, Dock.Left);
            header.Children.Add(done);
            header.Children.Add(titleBox);

            stack.Children.Add(header);
            var detailsPanel = new StackPanel { Visibility = task.IsCollapsed ? Visibility.Collapsed : Visibility.Visible };

            var bodyBox = new TextBox
            {
                Text = task.Body,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Margin = new Thickness(0, 0, 0, 6)
            };
            bodyBox.TextChanged += (s, e) =>
            {
                task.Body = bodyBox.Text;
                SaveTasks();
            };
            detailsPanel.Children.Add(bodyBox);

            var duePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            duePanel.Children.Add(new TextBlock { Text = "期限: ", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });

            var dueDatePicker = new DatePicker 
            { 
                SelectedDate = task.DueAt.Date, 
                Width = 110,
                DisplayDateStart = DateTime.Today,
                IsEnabled = !isOverdue
            };
            var dueTimeBox = new TextBox 
            { 
                Text = task.DueAt.ToString("HH:mm"), 
                Width = 50,
                Margin = new Thickness(0,5,0,0),
                CaretBrush = Brushes.Black,
                IsEnabled = !isOverdue
            };

            dueTimeBox.TextChanged += TaskTimeBox_TextChanged;

            Action updateDue = () =>
            {
                var d = dueDatePicker.SelectedDate ?? DateTime.Today;

                if (TimeSpan.TryParse(dueTimeBox.Text.Trim(), out var t))
                {
                    var newDueAt = d.Add(t);

                    // 現在時刻以前は禁止
                    if (newDueAt <= DateTime.Now)
                    {
                        dueDatePicker.SelectedDate = task.DueAt.Date;
                        dueTimeBox.Text = task.DueAt.ToString("HH:mm");

                        return;
                    }

                    task.DueAt = newDueAt;
                    SaveTasks();
                    RenderTasks();
                }
            };

            dueDatePicker.LostFocus += (s, e) => updateDue();
            dueTimeBox.LostFocus += (s, e) => updateDue();

            duePanel.Children.Add(dueDatePicker);
            duePanel.Children.Add(dueTimeBox);

            detailsPanel.Children.Add(duePanel);
            stack.Children.Add(detailsPanel);

            border.Child = stack;
            return border;
        }

        private void UpdateTaskDone(TaskItem task, bool isDone)
        {
            task.IsDone = isDone;
            if (!isDone && task.DueAt > DateTime.Now)
            {
                task.Notified = false;
            }
            SaveTasks();
            RenderTasks();
        }

        private void TaskDueTimer_Tick(object? sender, EventArgs e)
        {
            CheckDueTasks();
        }

        private void CheckDueTasks()
        {
            var dueTasks = _tasks.Where(t => !t.IsDone && !t.Notified && t.DueAt <= DateTime.Now).ToList();
            foreach (var task in dueTasks)
            {
                task.Notified = true;
                ShowTaskDueNotification(task);
            }

            if (dueTasks.Count > 0)
            {
                SaveTasks();
                RenderTasks();
            }
        }

        private void ShowTaskDueNotification(TaskItem task)
        {
            ShowDesktopNotification("タスク締め切り", $"{task.Title}: {task.Body}");

            if (_isBackgroundMode || !IsVisible)
            {
                PlaySE(GetAudioPath("alerm.wav"));
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            ShowEventNotification("タスク締め切り", $"{task.Title}: {task.Body}", Color.FromRgb(185, 64, 42), TimeSpan.FromMinutes(2), GetAudioPath("alerm.wav"));
        }

        

        private void LoadTimestamps()
        {
            // 重要: 読み込みに失敗した状態で _timestampBoxes を空にして保存すると、
            // 既存の timestamps.json を空で上書きしてしまう。
            // まずローカル変数へ完全に読み込み、成功した場合だけ本体へ反映する。
            _timestampsLoaded = false;
            _timestampLoadFailed = false;

            try
            {
                EnsureUserDataDirectory();

                if (!File.Exists(TimestampsPath))
                {
                    _timestampBoxes.Clear();
                    EnsureTimestampBox();
                    _timestampsLoaded = true;
                    RenderTimestamps();
                    return;
                }

                string json = File.ReadAllText(TimestampsPath);
                List<TimestampBox>? boxes = null;
                TimestampDataFile? combinedData = null;

                try
                {
                    // 新形式を最優先で読む。
                    combinedData = JsonSerializer.Deserialize<TimestampDataFile>(json);
                    if (combinedData != null && combinedData.Boxes != null && combinedData.Boxes.Count > 0)
                    {
                        boxes = combinedData.Boxes;
                    }
                    else
                    {
                        // 旧形式: TimestampBox[]
                        boxes = JsonSerializer.Deserialize<List<TimestampBox>>(json);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TimestampBox JSON load failed: {ex}");

                    // 現在のJSONが壊れていた場合は、直前の正常保存を試す。
                    string backupPath = TimestampsPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            json = File.ReadAllText(backupPath);
                            try
                            {
                                combinedData = JsonSerializer.Deserialize<TimestampDataFile>(json);
                                boxes = combinedData?.Boxes != null && combinedData.Boxes.Count > 0
                                    ? combinedData.Boxes
                                    : JsonSerializer.Deserialize<List<TimestampBox>>(json);
                            }
                            catch
                            {
                                boxes = JsonSerializer.Deserialize<List<TimestampBox>>(json);
                            }
                            if (boxes != null)
                                File.Copy(backupPath, TimestampsPath, true);
                        }
                        catch (Exception backupEx)
                        {
                            Debug.WriteLine($"Timestamp backup load failed: {backupEx}");
                        }
                    }
                }

                // 現在の形式として正常に読み込めた場合だけ採用する。
                if (boxes != null)
                {
                    foreach (var box in boxes)
                    {
                        if (string.IsNullOrWhiteSpace(box.SortMode))
                            box.SortMode = box.SortDescending ? "time_desc" : "time_asc";

                        box.Items ??= new List<TimestampItem>();
                        if (string.IsNullOrWhiteSpace(box.Id))
                            box.Id = Guid.NewGuid().ToString();
                        if (string.IsNullOrWhiteSpace(box.Name))
                            box.Name = "タイムスタンプ";

                        // 旧形式では出典がBOX単位だったため、
                        // 各タイムスタンプへ一度だけ引き継ぐ。
                        foreach (var item in box.Items)
                        {
                            item.LiveUrl ??= string.Empty;
                            item.RecordingFileName ??= string.Empty;

                            if (string.IsNullOrWhiteSpace(item.LiveUrl) &&
                                string.IsNullOrWhiteSpace(item.RecordingFileName))
                            {
                                if (!string.IsNullOrWhiteSpace(box.LiveUrl))
                                    item.LiveUrl = box.LiveUrl.Trim();
                                else if (!string.IsNullOrWhiteSpace(box.RecordingFileName))
                                    item.RecordingFileName = box.RecordingFileName.Trim();
                            }

                            // 出典は必ず片方だけ。
                            if (!string.IsNullOrWhiteSpace(item.LiveUrl))
                                item.RecordingFileName = string.Empty;
                            else if (!string.IsNullOrWhiteSpace(item.RecordingFileName))
                                item.LiveUrl = string.Empty;
                        }
                    }

                    _timestampBoxes.Clear();
                    _timestampBoxes.AddRange(boxes);
                }
                else
                {
                    // 旧形式（TimestampItem[]）を試す。ただし、これも成功した場合だけ反映。
                    List<TimestampItem>? oldItems;
                    try
                    {
                        oldItems = JsonSerializer.Deserialize<List<TimestampItem>>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Legacy timestamp JSON load failed: {ex}");
                        oldItems = null;
                    }

                    if (oldItems == null)
                    {
                        throw new InvalidDataException("timestamps.json を読み込めませんでした。既存データを保持します。");
                    }

                    _timestampBoxes.Clear();
                    _timestampBoxes.Add(new TimestampBox
                    {
                        Name = "タイムスタンプ",
                        FolderId = GetUncategorizedFolderId("timestamp"),
                        Order = 0,
                        Items = oldItems
                    });
                }

                // 新形式にフォルダが入っていて、外部のフォルダファイルが空だった場合は復元する。
                if (_timestampFolders.Count <= 1 && combinedData?.Folders != null && combinedData.Folders.Count > 1)
                {
                    var currentDefault = _timestampFolders.FirstOrDefault(f => f.Name == "未分類");
                    var restoredFolders = combinedData.Folders
                        .Where(f => f != null)
                        .Select(f => new FolderData
                        {
                            Id = string.IsNullOrWhiteSpace(f.Id) ? Guid.NewGuid().ToString() : f.Id,
                            Name = string.IsNullOrWhiteSpace(f.Name) ? "未分類" : f.Name,
                            Order = f.Order
                        })
                        .ToList();

                    if (restoredFolders.Count > 0)
                    {
                        _timestampFolders.Clear();
                        _timestampFolders.AddRange(restoredFolders);
                        EnsureDefaultFolder(_timestampFolders);
                        NormalizeFolderOrder(_timestampFolders);
                        SaveTimestampFolders();
                    }
                }

                EnsureTimestampBox();

                foreach (var box in _timestampBoxes)
                {
                    if (!string.IsNullOrWhiteSpace(box.LiveUrl))
                        box.RecordingFileName = string.Empty;
                    else if (!string.IsNullOrWhiteSpace(box.RecordingFileName))
                        box.LiveUrl = string.Empty;

                    if (string.IsNullOrWhiteSpace(box.FolderId) ||
                        !_timestampFolders.Any(f => f.Id == box.FolderId))
                    {
                        box.FolderId = GetUncategorizedFolderId("timestamp");
                    }
                }

                NormalizeTimestampBoxOrder();
                _timestampsLoaded = true;
                RenderTimestamps();
            }
            catch (Exception ex)
            {
                // 読み込み失敗時は _timestampBoxes を触らない。
                // Window_Closed の SaveTimestamps() も実行させない。
                _timestampLoadFailed = true;
                _timestampsLoaded = false;
                Debug.WriteLine($"Load timestamps failed: {ex}");
            }
        }

        private void SaveTimestamps()
        {
            // 起動時の読み込みが完了していない、または失敗している状態では
            // 絶対に既存ファイルを上書きしない。
            if (!_timestampsLoaded || _timestampLoadFailed)
                return;

            try
            {
                EnsureUserDataDirectory();
                // フォルダも同じ timestamps.json に保存する。
                // 別ファイルが消えても、ここからフォルダを復元できる。
                NormalizeFolderOrder(_timestampFolders);
                var data = new TimestampDataFile
                {
                    Folders = _timestampFolders.ToList(),
                    Boxes = _timestampBoxes.ToList()
                };
                var json = JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions { WriteIndented = true });

                string tempPath = TimestampsPath + ".tmp";
                string backupPath = TimestampsPath + ".bak";

                File.WriteAllText(tempPath, json);

                if (File.Exists(TimestampsPath))
                {
                    File.Copy(TimestampsPath, backupPath, true);
                    File.Replace(tempPath, TimestampsPath, null);
                }
                else
                {
                    File.Move(tempPath, TimestampsPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save timestamps failed: {ex}");
                try
                {
                    string tempPath = TimestampsPath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }

        private void BtnAddTimestamp_Click(object sender, RoutedEventArgs e)
        {
            // ショートカットでは「現在選択中」ではなく、
            // 全タイムスタンプBOXの実際の一番上を判定対象にする。
            var topBox = GetTopTimestampBoxForActiveFolder();

            var isLiveTimestamp =
                RbTimestampLive.IsChecked == true &&
                _liveStartedAt != null &&
                !string.IsNullOrWhiteSpace(_currentLiveUrl);

            if (isLiveTimestamp)
            {
                var currentUrl = NormalizeLiveUrl(_currentLiveUrl!);
                var topUrl = NormalizeLiveUrl(topBox.LiveUrl);
                var hasRecording = !string.IsNullOrWhiteSpace(topBox.RecordingFileName);

                // 配信ショートカットの場合:
                // ・一番上が録画BOX → 新しい配信用BOX
                // ・一番上の配信URLが別の配信 → 新しい配信用BOX
                // ・一番上の配信URLが同じ → 同じBOXへ追加
                // ・出典未設定 → 同じBOXへ追加し、配信URLを設定
                if (hasRecording ||
                    (!string.IsNullOrWhiteSpace(topUrl) && topUrl != currentUrl))
                {
                    var newBox = new TimestampBox
                    {
                        Name = "タイムスタンプ",
                        Order = 0,
                        RecordingFileName = string.Empty,
                        LiveUrl = _currentLiveUrl!.Trim(),
                        IsCollapsed = false,
                        SortDescending = topBox.SortDescending
                    };

                    foreach (var box in _timestampBoxes)
                        box.Order++;

                    _timestampBoxes.Insert(0, newBox);
                    _activeTimestampBoxId = newBox.Id;
                    AddTimestampItemToBox(newBox);
                }
                else
                {
                    // 同じ配信、または出典未設定の一番上のBOXへ追加。
                    AddTimestampItemToBox(topBox);
                }
            }
            else
            {
                // 録画ショートカットも、一番上のBOXを基準にする。
                var topRecording = !string.IsNullOrWhiteSpace(topBox.RecordingFileName);
                var topHasLiveUrl = !string.IsNullOrWhiteSpace(topBox.LiveUrl);
                var currentRecording = _currentRecordingFileName ?? string.Empty;
                var sameRecording = topRecording &&
                    !string.IsNullOrWhiteSpace(currentRecording) &&
                    string.Equals(
                        NormalizeRecordingPath(topBox.RecordingFileName),
                        NormalizeRecordingPath(currentRecording),
                        StringComparison.OrdinalIgnoreCase);

                if (topHasLiveUrl || (topRecording && !sameRecording))
                {
                    var newBox = new TimestampBox
                    {
                        Name = "タイムスタンプ",
                        Order = 0,
                        RecordingFileName = currentRecording,
                        LiveUrl = string.Empty,
                        IsCollapsed = false,
                        SortDescending = topBox.SortDescending
                    };

                    foreach (var box in _timestampBoxes)
                        box.Order++;

                    _timestampBoxes.Insert(0, newBox);
                    _activeTimestampBoxId = newBox.Id;
                    AddTimestampItemToBox(newBox);
                }
                else
                {
                    AddTimestampToBox(topBox);
                }
            }

            SaveTimestamps();
            RefreshTimestampPopupViews();
            RenderTimestamps();
        }

        private static string NormalizeLiveUrl(string? url)
        {
            return (url ?? string.Empty).Trim().TrimEnd('/');
        }

        private static string NormalizeRecordingPath(string? path)
        {
            return (path ?? string.Empty).Trim().TrimEnd('\\');
        }

        private void AddTimestampToBox(TimestampBox box)
        {
            _activeTimestampBoxId = box.Id;

            string liveUrl = string.Empty;
            string recordingFileName = string.Empty;

            if (RbTimestampLive.IsChecked == true &&
                !string.IsNullOrWhiteSpace(_currentLiveUrl))
            {
                liveUrl = _currentLiveUrl.Trim();
                box.LiveUrl = liveUrl;
                box.RecordingFileName = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(_currentRecordingFileName))
            {
                recordingFileName = _currentRecordingFileName.Trim();
                box.RecordingFileName = recordingFileName;
                box.LiveUrl = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(box.LiveUrl))
            {
                liveUrl = box.LiveUrl.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(box.RecordingFileName))
            {
                recordingFileName = box.RecordingFileName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(liveUrl))
                recordingFileName = string.Empty;
            else if (!string.IsNullOrWhiteSpace(recordingFileName))
                liveUrl = string.Empty;

            box.Items.Insert(0, new TimestampItem
            {
                Time = GetCurrentTimestampText(),
                Body = string.Empty,
                IsChecked = false,
                LiveUrl = liveUrl,
                RecordingFileName = recordingFileName
            });

            box.IsCollapsed = false;

            SaveTimestamps();
            RenderTimestamps();
        }

        private void BtnAddTimestampBox_Click(object sender, RoutedEventArgs e)
        {
            var isLiveTimestamp =
                RbTimestampLive.IsChecked == true &&
                _liveStartedAt != null &&
                !string.IsNullOrWhiteSpace(_currentLiveUrl);

            var newBox = new TimestampBox
            {
                Name = "タイムスタンプ",
                FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                    ? GetUncategorizedFolderId("timestamp")
                    : _activeTimestampFolderId,
                Order = 0,
                RecordingFileName = isLiveTimestamp
                    ? string.Empty
                    : (_currentRecordingFileName ?? string.Empty),
                LiveUrl = isLiveTimestamp
                    ? _currentLiveUrl!.Trim()
                    : string.Empty
            };

            foreach (var box in _timestampBoxes)
            {
                box.Order++;
            }

            _timestampBoxes.Insert(0,newBox);
            _activeTimestampBoxId = newBox.Id;

            SaveTimestamps();

            // タイムスタンプ全体ポップアップを表示中
            if (TimestampEditorPopup.Visibility == Visibility.Visible)
            {
                _timestampEditorBoxes = _timestampBoxes;
                RefreshTimestampEditorInMainWindow();
            }
            else
            {
                RenderTimestamps();
            }
        }

        private void BtnSortTimestampsAsc_Click(object sender, RoutedEventArgs e)
        {
            GetActiveTimestampBox().SortDescending = false;
            SaveTimestamps();
            RenderTimestamps();
        }

        private void BtnSortTimestampsDesc_Click(object sender, RoutedEventArgs e)
        {
            GetActiveTimestampBox().SortDescending = true;
            SaveTimestamps();
            RenderTimestamps();
        }

        private void BtnClearCheckedTimestamps_Click(object sender, RoutedEventArgs e)
        {
            // Shiftキーを押していなければ何もしない
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                return;
            }

            // チェック済みが無ければ終了
            if (_timestampBoxes.SelectMany(b => b.Items).All(t => !t.IsChecked))
            {
                return;
            }

            foreach (var box in _timestampBoxes)
            {
                box.Items.RemoveAll(t => t.IsChecked);
            }

            SaveTimestamps();
            RenderTimestamps();
        }

        private void BtnPopOutAllTimestamps_Click(
    object sender,
    RoutedEventArgs e)
        {
            ShowAllTimestampPopupInMainWindow(
                "タイムスタンプ",
                GetTimestampBoxesInDisplayOrder());
        }

        private void ShowAllTimestampPopupInMainWindow(
    string title,
    List<TimestampBox> boxes)
        {
            if (_floatingAllTimestampPopup != null)
                return;

            _floatingAllTimestampBoxes = boxes;

            // ★ 全BOXをメインのタイムスタンプパネルから非表示
            foreach (var box in boxes)
            {
                _poppedOutTimestampBoxIds.Add(box.Id);
            }

            RenderTimestamps();

            // 以下既存処理...

            var contentPanel = new StackPanel();

            void Refresh()
            {
                contentPanel.Children.Clear();

                var popupBoxes = GetTimestampBoxesInDisplayOrder();
                if (string.IsNullOrEmpty(_activeTimestampFolderId))
                {
                    foreach (var folder in _timestampFolders.OrderBy(f => f.Order))
                    {
                        var folderBoxes = popupBoxes.Where(b => b.FolderId == folder.Id).ToList();
                        if (folderBoxes.Count == 0)
                            continue;

                        contentPanel.Children.Add(CreateTimestampFolderSeparator(folder));
                        foreach (var box in folderBoxes)
                            contentPanel.Children.Add(CreateTimestampBoxPopupEditor(box, Refresh));
                    }
                }
                else
                {
                    foreach (var box in popupBoxes)
                        contentPanel.Children.Add(CreateTimestampBoxPopupEditor(box, Refresh));
                }
            }

            Refresh();
            _floatingAllTimestampPopupRefreshAction = Refresh;

            // ヘッダー
            var header = new Border
            {
                Background = new SolidColorBrush(
                    Color.FromRgb(45, 45, 45)),
                Padding = new Thickness(8)
            };

            var headerGrid = new Grid();

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition());

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

            headerGrid.Children.Add(new TextBlock
            {
                Text = $"{title}  [{GetFolderName("timestamp", _activeTimestampFolderId)}]",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment =
                    VerticalAlignment.Center
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 配信 / 録画の選択
            // このポップアップは実際にはXAMLのTimestampEditorPopupではなく、
            // ここで動的に生成しているため、ここにも明示的に追加する。
            var sourceSelector = CreateTimestampPopupSourceSelector();
            buttonPanel.Children.Add(sourceSelector);

            // ＋
            var addButton =
                CreateHeaderButton(
                    "＋",
                    "タイムスタンプBOXを追加");

            addButton.Click += (s, e) =>
            {
                // 既存のBOXを全部1つ後ろへ
                foreach (var box in _timestampBoxes)
                {
                    box.Order++;
                }

                // 新しいBOXを一番上へ
                var isLiveTimestamp =
                    RbTimestampLive.IsChecked == true &&
                    _liveStartedAt != null &&
                    !string.IsNullOrWhiteSpace(_currentLiveUrl);

                var newBox = new TimestampBox
                {
                    Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0,
                    SortDescending = true,
                    RecordingFileName = isLiveTimestamp
                        ? string.Empty
                        : (_currentRecordingFileName ?? string.Empty),
                    LiveUrl = isLiveTimestamp
                        ? _currentLiveUrl!.Trim()
                        : string.Empty
                };

                _timestampBoxes.Insert(0, newBox);
                NormalizeTimestampBoxOrder();

                // ポップアップ表示中に追加したBOXもポップアウト扱いにする。
                // これをしないと次回RenderTimestamps()でメイン側に出てしまう。
                _poppedOutTimestampBoxIds.Add(newBox.Id);

                SaveTimestamps();

                // メインのタイムスタンプパネルは更新しない。
                // ポップアップ内だけ更新する。
                Refresh();
            };

            buttonPanel.Children.Add(addButton);

            // ↗
            var detach =
                CreateHeaderButton(
                    "↗",
                    "別ウィンドウとして表示");

            detach.Click += (s, e) =>
            {
                ShowTimestampEditorAsExternalWindow(
                    title,
                    boxes);
            };

            buttonPanel.Children.Add(detach);

            // □
            var close =
                CreateHeaderButton(
                    "□",
                    "ポップアップを閉じる");

            close.Click += (s, e) =>
            {
                CloseAllTimestampPopup();
            };

            buttonPanel.Children.Add(close);

            Grid.SetColumn(buttonPanel, 1);
            headerGrid.Children.Add(buttonPanel);

            header.Child = headerGrid;

            // ドラッグ対象はヘッダーだけ。本文のTextBox/チェック操作を奪わない。
            _floatingAllTimestampDragHeader = header;

            var root = new DockPanel();

            DockPanel.SetDock(
                header,
                Dock.Top);

            root.Children.Add(header);

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,

                Content = contentPanel
            });

            _floatingAllTimestampPopup = new Border
            {
                Width = 720,
                Height = 760,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(34, 34, 34)),

                BorderBrush = Brushes.Gray,

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(8),

                Child = root
            };

            // テキストメモと同じドラッグ処理
            _floatingAllTimestampPopup.MouseLeftButtonDown +=
                FloatingAllTimestampPopup_MouseLeftButtonDown;

            _floatingAllTimestampPopup.MouseMove +=
                FloatingAllTimestampPopup_MouseMove;

            _floatingAllTimestampPopup.MouseLeftButtonUp +=
                FloatingAllTimestampPopup_MouseLeftButtonUp;

            Canvas.SetLeft(
                _floatingAllTimestampPopup,
                100);

            Canvas.SetTop(
                _floatingAllTimestampPopup,
                100);

            Panel.SetZIndex(
                _floatingAllTimestampPopup,
                100);

            FloatingMemoCanvas.Children.Add(
                _floatingAllTimestampPopup);
        }

        private void FloatingAllTimestampPopup_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
        {
            if (_floatingAllTimestampPopup == null)
                return;

            // ドラッグできるのはヘッダー部分だけ。
            // BOX本体のTextBoxやチェックボックス等の操作は絶対に奪わない。
            if (_floatingAllTimestampDragHeader == null ||
                e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            bool insideHeader = false;
            DependencyObject? current = source;
            while (current != null && !ReferenceEquals(current, _floatingAllTimestampPopup))
            {
                if (ReferenceEquals(current, _floatingAllTimestampDragHeader))
                {
                    insideHeader = true;
                    break;
                }

                if (current is ButtonBase || current is TextBoxBase || current is CheckBox)
                {
                    return;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            if (!insideHeader)
                return;

            _isDraggingAllTimestampPopup = true;

            _allTimestampDragMouseStart =
                e.GetPosition(FloatingMemoCanvas);

            _allTimestampDragStartLeft =
                Canvas.GetLeft(
                    _floatingAllTimestampPopup);

            _allTimestampDragStartTop =
                Canvas.GetTop(
                    _floatingAllTimestampPopup);

            _floatingAllTimestampPopup.CaptureMouse();

            Panel.SetZIndex(
                _floatingAllTimestampPopup,
                1000);

            e.Handled = true;
        }

        private void FloatingAllTimestampPopup_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_isDraggingAllTimestampPopup ||
                _floatingAllTimestampPopup == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current =
                e.GetPosition(FloatingMemoCanvas);

            double newLeft =
                _allTimestampDragStartLeft +
                (current.X - _allTimestampDragMouseStart.X);

            double newTop =
                _allTimestampDragStartTop +
                (current.Y - _allTimestampDragMouseStart.Y);

            newLeft = Math.Max(0, newLeft);
            newTop = Math.Max(0, newTop);

            Canvas.SetLeft(
                _floatingAllTimestampPopup,
                newLeft);

            Canvas.SetTop(
                _floatingAllTimestampPopup,
                newTop);
        }

        private void FloatingAllTimestampPopup_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_floatingAllTimestampPopup == null)
                return;

            _isDraggingAllTimestampPopup = false;

            _floatingAllTimestampPopup.ReleaseMouseCapture();
        }

        private void CloseAllTimestampPopup()
        {
            if (_floatingAllTimestampPopup != null)
            {
                _floatingAllTimestampPopup.MouseLeftButtonDown -=
                    FloatingAllTimestampPopup_MouseLeftButtonDown;

                _floatingAllTimestampPopup.MouseMove -=
                    FloatingAllTimestampPopup_MouseMove;

                _floatingAllTimestampPopup.MouseLeftButtonUp -=
                    FloatingAllTimestampPopup_MouseLeftButtonUp;

                FloatingMemoCanvas.Children.Remove(
                    _floatingAllTimestampPopup);

                _floatingAllTimestampPopup = null;
            }

            _floatingAllTimestampDragHeader = null;

            // ★ ポップアップ状態を解除
            _poppedOutTimestampBoxIds.Clear();

            _floatingAllTimestampBoxes = null;

            _floatingAllTimestampPopupRefreshAction = null;

            SaveTimestamps();
            RenderTimestamps();
        }

        private void PopOutTimestampBox(TimestampBox box)
        {
            ShowTimestampBoxAsInternalWindow(box);
        }

        // タイムスタンプ個別ポップアップもテキストメモと同じ
        // 「アプリ内フローティング → 別ウィンドウ → 本体へ統合」の流れにする。
        private void ShowTimestampBoxAsInternalWindow(TimestampBox box)
        {
            if (_floatingTimestampBoxPopup != null)
                return;

            _floatingTimestampBoxSource = box;

            _poppedOutTimestampBoxIds.Add(box.Id);
            RenderTimestamps();

            var content = new StackPanel();

            Action refresh = null;

            refresh = () =>
            {
                content.Children.Clear();
                content.Children.Add(CreateTimestampBoxPopupEditor(box, refresh));
            };
            refresh();

            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Padding = new Thickness(8)
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text = box.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var detach = CreateHeaderButton("↗", "別ウィンドウとして表示");
            detach.Click += (s, e) => ShowTimestampBoxAsExternalWindow(box, content);
            buttonPanel.Children.Add(detach);

            var close = CreateHeaderButton("□", "ポップアップを閉じる");
            close.Click += (s, e) => CloseTimestampBoxInternalPopup();
            buttonPanel.Children.Add(close);

            Grid.SetColumn(buttonPanel, 1);
            headerGrid.Children.Add(buttonPanel);
            header.Child = headerGrid;

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = content
            });

            _floatingTimestampBoxPopup = new Border
            {
                Width = 520,
                Height = 720,
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = root
            };

            // テキストメモと同じドラッグ処理
            _floatingTimestampBoxPopup.MouseLeftButtonDown -= FloatingTimestampBox_MouseLeftButtonDown;
            _floatingTimestampBoxPopup.MouseMove -= FloatingTimestampBox_MouseMove;
            _floatingTimestampBoxPopup.MouseLeftButtonUp -= FloatingTimestampBox_MouseLeftButtonUp;

            _floatingTimestampBoxPopup.MouseLeftButtonDown += FloatingTimestampBox_MouseLeftButtonDown;
            _floatingTimestampBoxPopup.MouseMove += FloatingTimestampBox_MouseMove;
            _floatingTimestampBoxPopup.MouseLeftButtonUp += FloatingTimestampBox_MouseLeftButtonUp;

            Panel.SetZIndex(_floatingTimestampBoxPopup, 100);

            Canvas.SetLeft(_floatingTimestampBoxPopup, 100);
            Canvas.SetTop(_floatingTimestampBoxPopup, 100);
            FloatingMemoCanvas.Children.Add(_floatingTimestampBoxPopup);
        }

        private void FloatingTimestampBox_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
        {
            if (sender is not Border timestampBox)
                return;

            // ボタン・TextBox・CheckBoxを操作しているときは
            // ポップアップ自体をドラッグしない
            if (e.OriginalSource is DependencyObject source)
            {
                if (source is Button ||
                    source is TextBox ||
                    source is CheckBox)
                {
                    return;
                }
            }

            _draggingFloatingTimestampBox = timestampBox;

            _floatingTimestampBoxMouseStart =
                e.GetPosition(FloatingMemoCanvas);

            _floatingTimestampBoxStartLeft =
                Canvas.GetLeft(timestampBox);

            _floatingTimestampBoxStartTop =
                Canvas.GetTop(timestampBox);

            timestampBox.CaptureMouse();

            Panel.SetZIndex(timestampBox, 1000);

            e.Handled = true;
        }

        private void FloatingTimestampBox_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (_draggingFloatingTimestampBox == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current =
                e.GetPosition(FloatingMemoCanvas);

            double dx =
                current.X - _floatingTimestampBoxMouseStart.X;

            double dy =
                current.Y - _floatingTimestampBoxMouseStart.Y;

            double left =
                _floatingTimestampBoxStartLeft + dx;

            double top =
                _floatingTimestampBoxStartTop + dy;

            left = Math.Max(0, left);
            top = Math.Max(0, top);

            Canvas.SetLeft(
                _draggingFloatingTimestampBox,
                left);

            Canvas.SetTop(
                _draggingFloatingTimestampBox,
                top);
        }

        private void FloatingTimestampBox_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_draggingFloatingTimestampBox != null)
            {
                _draggingFloatingTimestampBox.ReleaseMouseCapture();
                _draggingFloatingTimestampBox = null;
            }
        }

        private void ShowTimestampBoxAsExternalWindow(
    TimestampBox box,
    StackPanel content)
        {
            if (_floatingTimestampBoxPopup != null)
            {
                FloatingMemoCanvas.Children.Remove(
                    _floatingTimestampBoxPopup
                );

                _floatingTimestampBoxPopup = null;
            }

            var win = new Window
            {
                Title = box.Name,
                Width = 420,
                Height = 600,
                Background = new SolidColorBrush(
                    Color.FromRgb(34, 34, 34)
                ),
                WindowStartupLocation =
                    WindowStartupLocation.CenterScreen,
                Topmost =
                    ChkPopOutTopmost?.IsChecked ?? true
            };

            _floatingTimestampBoxWindow = win;

            var root = new DockPanel();

            // ========================================================
            // ヘッダー
            // ========================================================

            var header = new Border
            {
                Background = new SolidColorBrush(
                    Color.FromRgb(45, 45, 45)
                ),
                Padding = new Thickness(8)
            };

            var headerGrid = new Grid();

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition()
            );

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                }
            );

            headerGrid.Children.Add(new TextBlock
            {
                Text = box.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };


            // ↙ アプリ内ポップアップへ統合
            var integrate = CreateHeaderButton(
                "↙",
                "アプリ本体のポップアップに統合"
            );

            integrate.Click += (s, e) =>
            {
                win.Close();

            };

            buttonPanel.Children.Add(integrate);


            // □ 別ウィンドウを閉じる
            var close = CreateHeaderButton(
                "□",
                "別ウィンドウを閉じる"
            );

            close.Click += (s, e) =>
            {
                win.Close();
            };

            buttonPanel.Children.Add(close);


            Grid.SetColumn(buttonPanel, 1);

            headerGrid.Children.Add(buttonPanel);

            header.Child = headerGrid;

            DockPanel.SetDock(header, Dock.Top);

            root.Children.Add(header);


            // ========================================================
            // 本体
            // ========================================================

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,

                Content = content,

                Margin = new Thickness(10)
            });

            win.Content = root;


            // ========================================================
            // 閉じる
            // ========================================================

            win.Closed += (s, e) =>
            {
                SaveTimestamps();

                _poppedOutTimestampBoxIds.Remove(box.Id);

                RenderTimestamps();

                _floatingTimestampBoxWindow = null;
            };

            win.Show();
        }

        private void CloseTimestampBoxInternalPopup()
        {
            // ポップアップしていたBOXを記憶
            var box = _floatingTimestampBoxSource;

            if (_floatingTimestampBoxPopup != null)
            {
                FloatingMemoCanvas.Children.Remove(_floatingTimestampBoxPopup);
                _floatingTimestampBoxPopup = null;
            }

            // ★ ポップアップ状態を解除
            if (box != null)
            {
                _poppedOutTimestampBoxIds.Remove(box.Id);
            }

            _floatingTimestampBoxSource = null;

            SaveTimestamps();
            RenderTimestamps();
        }

        // ============================================================
        // タイムスタンプ全体ポップアップ
        // テキストメモ・リストメモと同じ構造
        // ============================================================

        private void SyncTimestampPopupSourceSelectors()
        {
            if (!IsInitialized)
                return;

            bool isLive = RbTimestampLive.IsChecked == true;

            if (RbTimestampPopupLive != null)
            {
                RbTimestampPopupLive.IsEnabled = _liveStartedAt != null;
                RbTimestampPopupLive.IsChecked = isLive && _liveStartedAt != null;
            }

            if (RbTimestampPopupRecording != null)
            {
                RbTimestampPopupRecording.IsChecked = !isLive || _liveStartedAt == null;
            }
        }

        private void RbTimestampPopupLive_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || RbTimestampPopupLive.IsChecked != true)
                return;

            if (_liveStartedAt == null)
            {
                RbTimestampPopupRecording.IsChecked = true;
                return;
            }

            RbTimestampLive.IsChecked = true;
            RbTimestampRecording.IsChecked = false;
        }

        private void RbTimestampPopupRecording_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || RbTimestampPopupRecording.IsChecked != true)
                return;

            RbTimestampRecording.IsChecked = true;
            RbTimestampLive.IsChecked = false;
        }

        private StackPanel CreateTimestampPopupSourceSelector()
        {
            var selector = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };

            var live = new RadioButton
            {
                Content = "配信",
                GroupName = "ExternalTimestampPopupSource",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 12, 0),
                IsEnabled = _liveStartedAt != null,
                IsChecked = RbTimestampLive.IsChecked == true && _liveStartedAt != null
            };

            var recording = new RadioButton
            {
                Content = "録画",
                GroupName = "ExternalTimestampPopupSource",
                Foreground = Brushes.White,
                IsChecked = RbTimestampLive.IsChecked != true || _liveStartedAt == null
            };

            live.Checked += (s, e) =>
            {
                if (_liveStartedAt == null)
                {
                    recording.IsChecked = true;
                    return;
                }

                RbTimestampLive.IsChecked = true;
                RbTimestampRecording.IsChecked = false;
            };

            recording.Checked += (s, e) =>
            {
                RbTimestampRecording.IsChecked = true;
                RbTimestampLive.IsChecked = false;
            };

            selector.Children.Add(live);
            selector.Children.Add(recording);
            return selector;
        }

        private void OpenTimestampEditorInMainWindow(string title, List<TimestampBox> boxes)
        {
            if (_timestampEditorWindow != null)
            {
                _timestampEditorWindow.Close();
                _timestampEditorWindow = null;
            }

            _timestampEditorTitle = title;
            _timestampEditorBoxes = boxes;

            TimestampEditorPopupTitle.Text = title;
            TimestampEditorPopup.Visibility = Visibility.Visible;

            // メインウィンドウ内へ戻すたびに中央位置から開始する。
            TimestampEditorPopupTranslate.X = 0;
            TimestampEditorPopupTranslate.Y = 0;

            SyncTimestampPopupSourceSelectors();
            RefreshTimestampEditorInMainWindow();
        }


        private void RefreshTimestampEditorInMainWindow()
        {
            if (_timestampEditorBoxes == null)
                return;

            TimestampEditorPopupPanel.Children.Clear();

            foreach (var box in GetTimestampBoxesInDisplayOrder())
            {
                TimestampEditorPopupPanel.Children.Add(
                    CreateTimestampBoxPopupEditor(
                        box,
                        RefreshTimestampEditorInMainWindow
                    )
                );
            }
        }


        // ============================================================
        // メインウィンドウ内に統合されたタイムスタンプポップアップのドラッグ
        // ============================================================
        private void TimestampEditorPopupBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source &&
                (source is ButtonBase || source is TextBoxBase || source is CheckBox))
            {
                return;
            }

            // ヘッダー部分だけをドラッグ対象にする。
            Point local = e.GetPosition(TimestampEditorPopupBorder);
            if (local.Y > 70)
                return;

            _isDraggingTimestampEditorPopup = true;
            _timestampEditorPopupDragStartMouse = e.GetPosition(DesignViewport);
            _timestampEditorPopupStartX = TimestampEditorPopupTranslate.X;
            _timestampEditorPopupStartY = TimestampEditorPopupTranslate.Y;

            TimestampEditorPopupBorder.CaptureMouse();
            Panel.SetZIndex(TimestampEditorPopupBorder, 1000);
            e.Handled = true;
        }

        private void TimestampEditorPopupBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingTimestampEditorPopup ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(DesignViewport);
            double dx = current.X - _timestampEditorPopupDragStartMouse.X;
            double dy = current.Y - _timestampEditorPopupDragStartMouse.Y;

            TimestampEditorPopupTranslate.X = _timestampEditorPopupStartX + dx;
            TimestampEditorPopupTranslate.Y = _timestampEditorPopupStartY + dy;
            e.Handled = true;
        }

        private void TimestampEditorPopupBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingTimestampEditorPopup)
                return;

            _isDraggingTimestampEditorPopup = false;
            TimestampEditorPopupBorder.ReleaseMouseCapture();
            e.Handled = true;
        }

        // ↗ 別ウィンドウ
        private void BtnDetachTimestampPopup_Click(object sender, RoutedEventArgs e)
        {
            if (_timestampEditorBoxes == null ||
                _timestampEditorBoxes.Count == 0)
            {
                return;
            }

            ShowTimestampEditorAsExternalWindow(
                _timestampEditorTitle,
                _timestampEditorBoxes
            );
        }


        // □ ポップアップ解除
        private void BtnCloseTimestampPopup_Click(object sender, RoutedEventArgs e)
        {
            TimestampEditorPopup.Visibility = Visibility.Collapsed;

            _timestampEditorBoxes = null;

            SaveTimestamps();
            RenderTimestamps();
        }


        // ============================================================
        // タイムスタンプ全体を別ウィンドウにする
        // ============================================================

        private void ShowTimestampEditorAsExternalWindow(
            string title,
            List<TimestampBox> boxes)
        {
            if (_timestampEditorWindow != null)
            {
                _timestampEditorWindow.Activate();
                return;
            }

            // ========================================================
            // ★ 本体側のタイムスタンプポップアップを完全に削除
            // ========================================================

            if (_floatingAllTimestampPopup != null)
            {
                _floatingAllTimestampPopup.MouseLeftButtonDown -=
                    FloatingAllTimestampPopup_MouseLeftButtonDown;

                _floatingAllTimestampPopup.MouseMove -=
                    FloatingAllTimestampPopup_MouseMove;

                _floatingAllTimestampPopup.MouseLeftButtonUp -=
                    FloatingAllTimestampPopup_MouseLeftButtonUp;

                FloatingMemoCanvas.Children.Remove(
                    _floatingAllTimestampPopup);

                _floatingAllTimestampPopup = null;
            }

            // 旧XAML方式のポップアップも念のため非表示
            TimestampEditorPopup.Visibility =
                Visibility.Collapsed;

            // ★ 全BOXをポップアップ状態にする
            foreach (var box in boxes)
            {
                _poppedOutTimestampBoxIds.Add(box.Id);
            }

            // ★ 元のタイムスタンプパネルから消す
            RenderTimestamps();

            _timestampEditorBoxes = boxes;

            // メイン側ポップアップを隠す
            TimestampEditorPopup.Visibility = Visibility.Collapsed;

            var win = new Window
            {
                Title = title,
                Width = 460,
                Height = 640,
                Background = new SolidColorBrush(
                    Color.FromRgb(34, 34, 34)
                ),
                Topmost = ChkPopOutTopmost?.IsChecked ?? true
            };

            _timestampEditorWindow = win;

            var root = new DockPanel();


            // --------------------------------------------------------
            // タイムスタンプ本体
            // --------------------------------------------------------

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Hidden
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(10)
            };

            Action refresh = null!;

            refresh = () =>
            {
                panel.Children.Clear();

                foreach (var box in GetTimestampBoxesInDisplayOrder())
                {
                    panel.Children.Add(
                        CreateTimestampBoxPopupEditor(
                            box,
                            refresh
                        )
                    );
                }
            };

            _timestampEditorWindowRefreshAction = refresh;

            // --------------------------------------------------------
            // ヘッダー
            // --------------------------------------------------------

            var header = new Border
            {
                Background = new SolidColorBrush(
                    Color.FromRgb(45, 45, 45)
                ),
                Padding = new Thickness(8)
            };

            var headerGrid = new Grid();

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition()
            );

            headerGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                }
            );

            headerGrid.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            buttons.Children.Add(CreateTimestampPopupSourceSelector());

            var addButton = CreateHeaderButton("＋","タイムスタンプBOXを追加");

            addButton.Click += (s, e) =>
            {
                var isLiveTimestamp =
                    RbTimestampLive.IsChecked == true &&
                    _liveStartedAt != null &&
                    !string.IsNullOrWhiteSpace(_currentLiveUrl);

                var newBox = new TimestampBox
                {
                    Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0,
                    SortDescending = true,
                    RecordingFileName = isLiveTimestamp
                        ? string.Empty
                        : (_currentRecordingFileName ?? string.Empty),
                    LiveUrl = isLiveTimestamp
                        ? _currentLiveUrl!.Trim()
                        : string.Empty
                };

                _timestampBoxes.Insert(0, newBox);
                NormalizeTimestampBoxOrder();

                // 別ウィンドウ表示中に追加したBOXもポップアウト状態にする。
                // メインのタイムスタンプパネルには、別ウィンドウを閉じるまで出さない。
                _poppedOutTimestampBoxIds.Add(newBox.Id);

                SaveTimestamps();

                _activeTimestampBoxId = newBox.Id;
                RefreshTimestampPopupViews();

                // 別ウィンドウだけを更新する。
                // RenderTimestamps() はここでは呼ばない。
                refresh();
            };

            buttons.Children.Add(addButton);

            // ↙ 本体に統合
            var integrate = CreateHeaderButton(
                "↙",
                "メインウィンドウに統合"
            );

            integrate.Click += (s, e) =>
            {
                _timestampEditorReturnToMain = true;
                win.Close();
            };

            buttons.Children.Add(integrate);



            // □ ポップアップ解除
            var close = CreateHeaderButton(
                "□",
                "ポップアップ解除"
            );

            close.Click += (s, e) =>
            {
                _timestampEditorReturnToMain = false;
                win.Close();
            };

            buttons.Children.Add(close);


            Grid.SetColumn(buttons, 1);
            headerGrid.Children.Add(buttons);

            header.Child = headerGrid;

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);



            refresh();

            scroll.Content = panel;

            root.Children.Add(scroll);

            win.Content = root;


            // --------------------------------------------------------
            // ウィンドウを閉じたとき
            // --------------------------------------------------------

            win.Closed += (s, e) =>
            {
                SaveTimestamps();

                _timestampEditorWindowRefreshAction = null;
                _timestampEditorWindow = null;

                if (_timestampEditorReturnToMain)
                {
                    _timestampEditorReturnToMain = false;

                    // ★ ポップアップ中状態を解除
                    foreach (var box in boxes)
                    {
                        _poppedOutTimestampBoxIds.Remove(box.Id);
                    }

                    // ★ 元のタイムスタンプBOXを復活
                    RenderTimestamps();

                    // ★ 本体内ポップアップを再生成
                    ShowAllTimestampPopupInMainWindow(
                        title,
                        boxes);
                }
                else
                {
                    // 「ポップアップ解除」の場合
                    foreach (var box in boxes)
                    {
                        _poppedOutTimestampBoxIds.Remove(box.Id);
                    }

                    _timestampEditorBoxes = null;

                    RenderTimestamps();
                }
            };

            win.Show();
        }


        private static string GetTimestampSortLabel(string? sortMode)
        {
            return sortMode switch
            {
                "time_desc" => "遅",
                "time_asc" => "早",
                "added_desc" => "追",
                _ => "追"
            };
        }

        private static string GetNextTimestampSortMode(string? sortMode)
        {
            return sortMode switch
            {
                // 遅 → 早 → 追 → 遅
                "time_desc" => "time_asc",
                "time_asc" => "added_desc",
                _ => "time_desc"
            };
        }

        private Border CreateTimestampBoxPopupEditor(TimestampBox box, Action refresh)
        {
            var border = new Border
            {
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.DimGray,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                AllowDrop = true,
                Tag = box
            };

            // ポップアップ内のタイムスタンプBOXの並び順変更は
            // 矢印ボタンではなく、このドラッグ＆ドロップだけで行う。
            // PreviewDropで確実に先に受け取り、ドラッグ終了直後に
            // ポップアップ自身を再構築する。通常のDropだけだと、
            // 子要素側のルーティングやDragDrop終了処理のタイミングによって
            // 表示更新が1操作遅れることがある。
            border.PreviewDragOver += (s, e) =>
            {
                if (e.Data.GetData(typeof(TimestampBox)) is TimestampBox source &&
                    !ReferenceEquals(source, box))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };

            border.PreviewDrop += (s, e) =>
            {
                if (e.Data.GetData(typeof(TimestampBox)) is not TimestampBox source ||
                    ReferenceEquals(source, box))
                    return;

                // 「すべて」表示中は、別BOXへドロップするとフォルダも移動。
                if (string.IsNullOrEmpty(_activeTimestampFolderId))
                    source.FolderId = box.FolderId;

                // ポップアップ内で見た順番だけを変更するのではなく、
                // 実際の _timestampBoxes の並びも変更する。
                // ここを _timestampBoxes に反映しないと、Order が後で正規化され、
                // ショートカットが「一番上」と違うBOXを選んでしまう。
                ReorderTimestampBox(source, box);

                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                // 並び替え後の状態をポップアップだけ再構築する。
                border.Dispatcher.BeginInvoke(
                    new Action(refresh),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            };

            var stack = new StackPanel();

            // DockPanelで右寄せボタンを並べると、BOX名が狭くなった際に
            // 「ⓘ」と「×」などが重なることがある。
            // ヘッダーを3列Gridにして、ボタン領域を完全に独立させる。
            var header = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var handle = new TextBlock
            {
                Text = "⋮",
                Foreground = Brushes.LightGray,
                FontSize = 20,
                Width = 20,
                Cursor = Cursors.SizeAll,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            handle.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragDrop.DoDragDrop(handle, box, DragDropEffects.Move);
                }
            };
            Grid.SetColumn(handle, 0);
            header.Children.Add(handle);

            var name = new TextBox
            {
                Text = box.Name,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                MinWidth = 0
            };
            name.TextChanged += (s, e) => { box.Name = name.Text; SaveTimestamps(); };
            Grid.SetColumn(name, 1);
            header.Children.Add(name);

            // ポップアップ内もメインBOXと同じボタン順にする。
            var shortcutTarget = CreateTimestampShortcutTargetButton(box, refresh);

            var add = CreateSmallButton("＋");
            add.ToolTip = "タイムスタンプを追加";
            add.Click += (s, e) =>
            {
                AddTimestampItemToBox(box);
                SaveTimestamps();
                refresh();
            };

            var popout = CreateSmallButton("□");
            popout.ToolTip = "このタイムスタンプを個別にポップアップ";
            popout.Click += (s, e) => PopOutTimestampBox(box);

            var sort = CreateSmallButton(GetTimestampSortLabel(box.SortMode));
            sort.ToolTip = "タイムスタンプの並び順（クリックで 遅→早→追→遅）";
            sort.Click += (s, e) =>
            {
                box.SortMode = GetNextTimestampSortMode(box.SortMode);
                box.SortDescending = box.SortMode == "time_desc";
                SaveTimestamps();
                border.Dispatcher.BeginInvoke(new Action(refresh), System.Windows.Threading.DispatcherPriority.Render);
            };

            var folderButton = CreateFolderAssignButton(
                "timestamp",
                () => box.FolderId,
                id =>
                {
                    box.FolderId = id;
                    SaveTimestamps();
                    refresh();
                });
            folderButton.ToolTip = "このタイムスタンプの保存先フォルダを変更";

            var export = CreateSmallButton("↧");
            export.ToolTip = "タイムスタンプを保存";
            export.Click += (s, e) => ExportTextFile(box.Name, BuildTimestampBoxText(box));

            var clearChecked = CreateSmallButton("🗑");
            clearChecked.ToolTip = "このBOX内のチェック済みタイムスタンプをすべて削除";
            clearChecked.Click += (s, e) => ClearCheckedTimestampItems(box, refresh);

            var collapse = CreateSmallButton(box.IsCollapsed ? "∨" : "∧");
            collapse.ToolTip = "折りたたみ";
            collapse.Click += (s, e) => { box.IsCollapsed = !box.IsCollapsed; SaveTimestamps(); refresh(); };

            var delete = CreateSmallButton("×");
            delete.Foreground = Brushes.Red;
            delete.ToolTip = "Shift+クリックでこのタイムスタンプBOXを削除";
            delete.Click += (s, e) =>
            {
                // 誤操作防止のため、メインのタイムスタンプBOXと同じく
                // Shiftキーを押している場合だけ削除する。
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                    return;

                _timestampBoxes.Remove(box);
                _poppedOutTimestampBoxIds.Remove(box.Id);
                EnsureTimestampBox();
                SaveTimestamps();
                _timestampEditorBoxes = null;
                RenderTimestamps();
                refresh();
            };

            // 右側ボタンは1つのStackPanelにまとめ、GridのAuto列へ固定する。
            // これでⓘが×の領域へ入り込むことがない。
            var buttons = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            buttons.Children.Add(shortcutTarget);
            buttons.Children.Add(add);
            buttons.Children.Add(popout);
            buttons.Children.Add(sort);
            buttons.Children.Add(folderButton);
            buttons.Children.Add(export);
            buttons.Children.Add(clearChecked);
            buttons.Children.Add(collapse);
            buttons.Children.Add(delete);

            Grid.SetColumn(buttons, 2);
            header.Children.Add(buttons);
            stack.Children.Add(header);

            if (!box.IsCollapsed)
            {
                // 元コードはここだけ box.Items をそのまま foreach していたため、
                // SortDescending がポップアップの表示順に反映されていなかった。
                var items = box.SortDescending
                    ? box.Items.OrderByDescending(i => ParseTimestamp(i.Time)).ToList()
                    : box.Items.OrderBy(i => ParseTimestamp(i.Time)).ToList();
                foreach (var item in items)
                {
                    var row = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                    var top = new DockPanel();
                    var check = new CheckBox { IsChecked = item.IsChecked, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0) };
                    check.Checked += (s, e) => { item.IsChecked = true; SaveTimestamps(); };
                    check.Unchecked += (s, e) => { item.IsChecked = false; SaveTimestamps(); };
                    DockPanel.SetDock(check, Dock.Left); top.Children.Add(check);

                    var copyButton = CreateSmallButton("⧉");
                    copyButton.ToolTip = "時刻をコピー";
                    copyButton.Click += (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(item.Time))
                            Clipboard.SetText(item.Time);
                    };

                    var infoButton = CreateTimestampInfoButton(item);

                    var deleteItemButton = CreateSmallButton("×");
                    deleteItemButton.Foreground = Brushes.Red;
                    deleteItemButton.ToolTip = "Shift+クリックでこのタイムスタンプを削除";
                    deleteItemButton.Click += (s, e) =>
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                            return;

                        box.Items.Remove(item);
                        SaveTimestamps();
                        refresh();
                    };

                    DockPanel.SetDock(deleteItemButton, Dock.Right);
                    DockPanel.SetDock(infoButton, Dock.Right);
                    top.Children.Add(deleteItemButton);
                    top.Children.Add(infoButton);

                    var time = new TextBox { Text = item.Time, Width = 100, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
                    time.PreviewTextInput += TimestampTimeBox_PreviewTextInput;
                    time.PreviewKeyDown += TimestampTimeBox_PreviewKeyDown;
                    time.TextChanged += TimestampTimeBox_TextChanged;
                    time.LostFocus += TimestampTimeBox_LostFocus;
                    time.TextChanged += (s, e) => { item.Time = time.Text; SaveTimestamps(); };
                    var timeAndCopy = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    timeAndCopy.Children.Add(time);
                    timeAndCopy.Children.Add(copyButton);

                    if (_timestampSourceAlwaysVisible)
                    {
                        timeAndCopy.Children.Add(CreateTimestampSourceDisplay(item));
                    }

                    top.Children.Add(timeAndCopy);

                    top.PreviewMouseDown += (s, e) => FocusTimestampTimeBoxFromLeftHitArea(top, time, e);
                    row.Children.Add(top);
                    var body = new TextBox { Text = item.Body, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 36 };
                    body.TextChanged += (s, e) => { item.Body = body.Text; SaveTimestamps(); };
                    row.Children.Add(body); stack.Children.Add(row);
                }
            }
            border.Child = stack;
            return border;
        }

        private string BuildTimestampBoxText(TimestampBox box)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"[{box.Name}]");
            var items = box.SortMode switch
            {
                "time_asc" => box.Items.OrderBy(i => ParseTimestamp(i.Time)),
                "time_desc" => box.Items.OrderByDescending(i => ParseTimestamp(i.Time)),
                _ => box.Items.AsEnumerable().Reverse()
            };
            foreach (var item in items)
            {
                text.AppendLine($"{(item.IsChecked ? "[x]" : "[ ]")} {item.Time}");
                if (!string.IsNullOrWhiteSpace(item.Body)) text.AppendLine(item.Body);
                text.AppendLine();
            }
            return text.ToString();
        }

        private string GetCurrentTimestampText()
        {
            if (_liveStartedAt != null && RbTimestampLive.IsChecked == true)
            {
                return FormatElapsed(DateTimeOffset.Now - _liveStartedAt.Value);
            }

            return string.IsNullOrWhiteSpace(_lastRecordingElapsed) ? "00:00:00" : _lastRecordingElapsed;
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return elapsed.TotalHours >= 100
                ? $"{(int)elapsed.TotalHours:000}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        private void SetFavoriteTimestampBox(TimestampBox selected)
        {
            foreach (var b in _timestampBoxes)
            {
                b.IsShortcutTarget = ReferenceEquals(b, selected);
            }

            SaveTimestamps();
            RenderTimestamps();
            RefreshTimestampPopupViews();
        }

        private void RenderTimestamps()
        {
            UpdateTimestampFilterButton();
            TimestampPanel.Children.Clear();
            EnsureTimestampBox();

            var visibleBoxes = GetTimestampBoxesInDisplayOrder()
                .Where(box => !_poppedOutTimestampBoxIds.Contains(box.Id))
                .ToList();

            if (string.IsNullOrEmpty(_activeTimestampFolderId))
            {
                foreach (var folder in _timestampFolders.OrderBy(f => f.Order))
                {
                    var folderBoxes = visibleBoxes.Where(b => b.FolderId == folder.Id).ToList();
                    if (folderBoxes.Count == 0)
                        continue;

                    TimestampPanel.Children.Add(CreateTimestampFolderSeparator(folder));
                    foreach (var box in folderBoxes)
                        TimestampPanel.Children.Add(CreateTimestampBox(box));
                }
            }
            else
            {
                foreach (var box in visibleBoxes)
                    TimestampPanel.Children.Add(CreateTimestampBox(box));
            }
        }

        private Border CreateTimestampFolderSeparator(FolderData folder)
        {
            return new Border
            {
                Margin = new Thickness(4, 10, 4, 4),
                Padding = new Thickness(8, 4, 8, 4),
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = $"--- {folder.Name} ---",
                    Foreground = Brushes.LightGray,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
        }

        private void EnsureTimestampBox()
        {
            if (_timestampBoxes.Count == 0)
            {
                var box = new TimestampBox { Name = "タイムスタンプ",
                    FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                        ? GetUncategorizedFolderId("timestamp")
                        : _activeTimestampFolderId,
                    Order = 0, SortDescending = true };
                _timestampBoxes.Insert(0, box);
                _activeTimestampBoxId = box.Id;
            }
            _activeTimestampBoxId ??= _timestampBoxes.OrderBy(b => b.Order).First().Id;
        }

        // タイムスタンプBOXの表示順は、メインのタイムスタンプパネル
        // (_timestampBoxes の実際の並び)を唯一の正とする。
        // Order は保存用の補助値として、この順番に同期する。
        private List<TimestampBox> GetAllTimestampBoxesInDisplayOrder()
        {
            NormalizeTimestampBoxOrder();
            return _timestampBoxes.ToList();
        }

        private List<TimestampBox> GetTimestampBoxesInDisplayOrder()
        {
            return GetAllTimestampBoxesInDisplayOrder()
                .Where(b => IsFolderVisible("timestamp", b.FolderId))
                .Where(MatchesTimestampSourceFilter)
                .Where(MatchesTimestampSearch)
                .ToList();
        }

        private bool MatchesTimestampSearch(TimestampBox box)
        {
            if (string.IsNullOrWhiteSpace(_timestampSearchText)) return true;
            return SearchMatches(box.Name, _timestampSearchText) ||
                   SearchMatches(box.LiveUrl, _timestampSearchText) ||
                   SearchMatches(box.RecordingFileName, _timestampSearchText) ||
                   box.Items.Any(i =>
                       SearchMatches(i.Time, _timestampSearchText) ||
                       SearchMatches(i.Body, _timestampSearchText) ||
                       SearchMatches(i.LiveUrl, _timestampSearchText) ||
                       SearchMatches(i.RecordingFileName, _timestampSearchText));
        }

        private bool MatchesTimestampSourceFilter(TimestampBox box)
        {
            // 何も選択されていなければフィルターなし。
            if (_timestampFilterLiveUrls.Count == 0 &&
                _timestampFilterRecordingFiles.Count == 0)
                return true;

            bool liveMatch = !string.IsNullOrWhiteSpace(box.LiveUrl) &&
                _timestampFilterLiveUrls.Contains(box.LiveUrl.Trim());

            bool recordingMatch = !string.IsNullOrWhiteSpace(box.RecordingFileName) &&
                _timestampFilterRecordingFiles.Contains(box.RecordingFileName.Trim());

            bool itemLiveMatch = box.Items.Any(i =>
                !string.IsNullOrWhiteSpace(i.LiveUrl) &&
                _timestampFilterLiveUrls.Contains(i.LiveUrl.Trim()));

            bool itemRecordingMatch = box.Items.Any(i =>
                !string.IsNullOrWhiteSpace(i.RecordingFileName) &&
                _timestampFilterRecordingFiles.Contains(i.RecordingFileName.Trim()));

            // BOX単位の旧データ、または個々のタイムスタンプの出典のどちらかに一致すれば表示。
            return liveMatch || recordingMatch || itemLiveMatch || itemRecordingMatch;
        }

        private void TimestampFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ShowTimestampSourceFilterPopup((UIElement)sender);
        }

        private void UpdateTimestampFilterButton()
        {
            int count = _timestampFilterLiveUrls.Count + _timestampFilterRecordingFiles.Count;
            TimestampFilterButton.Content = count == 0 ? "🔽" : $"🔽({count})";
            TimestampFilterButton.ToolTip = count == 0
                ? "配信URL・録画ファイル名でフィルター"
                : $"フィルター中: {count}件";
        }

        private void ShowTimestampSourceFilterPopup(UIElement target)
        {
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                MinWidth = 360,
                MaxHeight = 520
            };

            var outer = new StackPanel();
            outer.Children.Add(new TextBlock
            {
                Text = "タイムスタンプ フィルター",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var allCheck = new CheckBox
            {
                Content = "すべて解除（全て表示）",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            allCheck.Checked += (s, e) =>
            {
                _timestampFilterLiveUrls.Clear();
                _timestampFilterRecordingFiles.Clear();
                RefreshTimestampFilterPopupAndViews(popup);
            };
            outer.Children.Add(allCheck);

            var liveValues = _timestampBoxes
                .SelectMany(b => b.Items.Select(i => i.LiveUrl))
                .Concat(_timestampBoxes.Select(b => b.LiveUrl))
                .Select(v => v?.Trim() ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var recordingValues = _timestampBoxes
                .SelectMany(b => b.Items.Select(i => i.RecordingFileName))
                .Concat(_timestampBoxes.Select(b => b.RecordingFileName))
                .Select(v => v?.Trim() ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AddTimestampFilterSection(outer, "配信URL", liveValues, _timestampFilterLiveUrls, popup);
            AddTimestampFilterSection(outer, "録画ファイル名", recordingValues, _timestampFilterRecordingFiles, popup);

            if (liveValues.Count == 0 && recordingValues.Count == 0)
            {
                outer.Children.Add(new TextBlock
                {
                    Text = "設定済みの配信URL・録画ファイル名がありません。",
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            root.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                MaxHeight = 500,
                Content = outer
            };
            popup.Child = root;
            popup.IsOpen = true;
        }

        private void AddTimestampFilterSection(Panel parent, string title, List<string> values, HashSet<string> selected, Popup popup)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.LightGray,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 4)
            });

            if (values.Count == 0)
            {
                parent.Children.Add(new TextBlock
                {
                    Text = "（なし）",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(8, 0, 0, 4)
                });
                return;
            }

            foreach (var value in values)
            {
                var check = new CheckBox
                {
                    Content = value,
                    Tag = value,
                    Foreground = Brushes.White,
                    IsChecked = selected.Contains(value),
                    Margin = new Thickness(8, 2, 0, 2),
                    ToolTip = value
                };

                check.Checked += (s, e) =>
                {
                    selected.Add(value);
                    RenderTimestamps();
                    RefreshTimestampPopupViews();
                    UpdateTimestampFilterButton();
                };

                check.Unchecked += (s, e) =>
                {
                    selected.Remove(value);
                    RenderTimestamps();
                    RefreshTimestampPopupViews();
                    UpdateTimestampFilterButton();
                };

                parent.Children.Add(check);
            }
        }

        private void RefreshTimestampFilterPopupAndViews(Popup popup)
        {
            popup.IsOpen = false;
            RenderTimestamps();
            RefreshTimestampPopupViews();
            UpdateTimestampFilterButton();
        }

        private TimestampBox GetTopTimestampBoxForActiveFolder()
        {
            EnsureTimestampBox();

            var box = GetTimestampBoxesInDisplayOrder()
                .FirstOrDefault(b => IsFolderVisible("timestamp", b.FolderId));

            if (box != null)
                return box;

            var newBox = new TimestampBox
            {
                Name = "タイムスタンプ",
                FolderId = string.IsNullOrEmpty(_activeTimestampFolderId)
                    ? GetUncategorizedFolderId("timestamp")
                    : _activeTimestampFolderId,
                Order = 0,
                SortDescending = true
            };

            foreach (var existing in _timestampBoxes)
                existing.Order++;

            _timestampBoxes.Insert(0, newBox);
            NormalizeTimestampBoxOrder();
            SaveTimestamps();
            return newBox;
        }

        private TimestampBox GetActiveTimestampBox()
        {
            EnsureTimestampBox();
            return _timestampBoxes.FirstOrDefault(b => b.Id == _activeTimestampBoxId)
                ?? GetTimestampBoxesInDisplayOrder().First();
        }

        private void ClearCheckedTimestampItems(TimestampBox box, Action? refresh = null)
        {
            var checkedItems = box.Items.Where(i => i.IsChecked).ToList();
            if (checkedItems.Count == 0) return;

            box.Items.RemoveAll(i => i.IsChecked);
            SaveTimestamps();
            refresh?.Invoke();
            if (refresh == null) RenderTimestamps();
        }

        private Button CreateTimestampShortcutTargetButton(TimestampBox box, Action? refresh = null)
        {
            var button = CreateSmallButton(box.IsShortcutTarget ? "★" : "☆");
            button.ToolTip = box.IsShortcutTarget
                ? "ショートカットのタイムスタンプ追加先"
                : "ショートカットのタイムスタンプ追加先にする";

            button.Foreground = box.IsShortcutTarget ? Brushes.Gold : Brushes.LightGray;
            button.FontSize = 16;

            button.Click += (s, e) =>
            {
                // ★はクリックして解除できない。
                // ☆を押したときだけ、そのBOXを唯一のショートカット追加先にする。
                foreach (var other in _timestampBoxes)
                    other.IsShortcutTarget = ReferenceEquals(other, box);

                SaveTimestamps();

                if (refresh != null)
                    refresh();
                else
                    RenderTimestamps();

                RefreshTimestampPopupViews();
            };

            return button;
        }

        private Border CreateTimestampBox(TimestampBox box)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            border.AllowDrop = true;
            border.PreviewDragOver += (s, e) =>
            {
                if (e.Data.GetData(typeof(TimestampBox)) is TimestampBox source &&
                    !ReferenceEquals(source, box))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };
            border.PreviewDrop += (s, e) =>
            {
                if (e.Data.GetData(typeof(TimestampBox)) is not TimestampBox source ||
                    ReferenceEquals(source, box))
                    return;

                // 「すべて」表示中は、別BOXへドロップした時点で
                // 並び替えだけでなく、そのBOXと同じフォルダへ移動する。
                if (string.IsNullOrEmpty(_activeTimestampFolderId))
                    source.FolderId = box.FolderId;

                ReorderTimestampBox(source, box);
                SaveTimestamps();
                RenderTimestamps();
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            };



            var stack = new StackPanel();
            // ポップアップだけでなく通常のタイムスタンプBOXも、右側ボタンを
            // GridのAuto列に固定してⓘと×が重ならないようにする。
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var handle = new TextBlock
            {
                Text = "⋮",
                Foreground = Brushes.LightGray,
                FontSize = 20,
                Width = 16,
                Cursor = Cursors.SizeAll,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            handle.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragDrop.DoDragDrop(handle, box, DragDropEffects.Move);
                }
            };
            Grid.SetColumn(handle, 0);
            header.Children.Add(handle);

            var nameBox = new TextBox
            {
                Text = box.Name,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                MinWidth = 0
            };
            nameBox.TextChanged += (s, e) => { box.Name = nameBox.Text; SaveTimestamps(); };
            Grid.SetColumn(nameBox, 1);
            header.Children.Add(nameBox);

            // ボタン順: 追加 → ☆/★ → ポップアップ → ソート → フォルダ → ダウンロード →
            //          チェック済み削除 → 折りたたみ → BOX削除
            var shortcutTargetButton = CreateTimestampShortcutTargetButton(box);

            var addButton = CreateSmallButton("＋");
            addButton.ToolTip = "タイムスタンプを追加";
            addButton.Click += (s, e) => { AddTimestampToBox(box); };

            var popoutButton = CreateSmallButton("□");
            popoutButton.Uid = "TimestampPopoutButton";
            popoutButton.ToolTip = "このタイムスタンプを個別にポップアップ";
            popoutButton.Click += (s, e) => PopOutTimestampBox(box);

            // タイムスタンプの並び順ボタン。
            // クリックするたびに「遅 → 早 → 追 → 遅」と切り替える。
            var sortModeButton = CreateSmallButton(GetTimestampSortLabel(box.SortMode));
            sortModeButton.ToolTip = "タイムスタンプの並び順（クリックで 遅→早→追→遅）";
            sortModeButton.Click += (s, e) =>
            {
                box.SortMode = GetNextTimestampSortMode(box.SortMode);
                box.SortDescending = box.SortMode == "time_desc";
                SaveTimestamps();
                RenderTimestamps();
                RefreshTimestampPopupViews();
            };

            var folderButton = CreateFolderAssignButton(
                "timestamp",
                () => box.FolderId,
                id =>
                {
                    box.FolderId = id;
                    SaveTimestamps();
                    RenderTimestamps();
                });
            folderButton.ToolTip = "このタイムスタンプの保存先フォルダを変更";

            var exportButton = CreateSmallButton("↧");
            exportButton.ToolTip = "タイムスタンプを保存";
            exportButton.Click += (s, e) => ExportTextFile(box.Name, BuildTimestampBoxText(box));

            var clearCheckedButton = CreateSmallButton("🗑");
            clearCheckedButton.ToolTip = "このBOX内のチェック済みタイムスタンプをすべて削除";
            clearCheckedButton.Click += (s, e) => ClearCheckedTimestampItems(box);

            var collapseButton = CreateSmallButton(box.IsCollapsed ? "∨" : "∧");
            collapseButton.ToolTip = "折りたたみ";
            collapseButton.Click += (s, e) => { box.IsCollapsed = !box.IsCollapsed; SaveTimestamps(); RenderTimestamps(); };

            var deleteButton = CreateSmallButton("×");
            deleteButton.Foreground = Brushes.Red;
            deleteButton.ToolTip = "Shift+クリックでこのタイムスタンプBOXを削除";
            deleteButton.Click += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                    return;
                _timestampBoxes.Remove(box);
                EnsureTimestampBox();
                SaveTimestamps();
                RenderTimestamps();
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(addButton);
            buttons.Children.Add(shortcutTargetButton);
            buttons.Children.Add(popoutButton);
            buttons.Children.Add(sortModeButton);
            buttons.Children.Add(folderButton);
            buttons.Children.Add(exportButton);
            buttons.Children.Add(clearCheckedButton);
            buttons.Children.Add(collapseButton);
            buttons.Children.Add(deleteButton);
            Grid.SetColumn(buttons, 2);
            header.Children.Add(buttons);
            stack.Children.Add(header);

            var itemsPanel = new StackPanel { Visibility = box.IsCollapsed ? Visibility.Collapsed : Visibility.Visible };
            var items = box.SortMode switch
            {
                "time_asc" => box.Items
                    .OrderBy(i => ParseTimestamp(i.Time))
                    .ToList(),

                "time_desc" => box.Items
                    .OrderByDescending(i => ParseTimestamp(i.Time))
                    .ToList(),

                // 追加順（降順）: JSON上のItemsの末尾を最新として表示
                _ => box.Items
                    .AsEnumerable()
                    .Reverse()
                    .ToList()
            };

            if (!string.IsNullOrWhiteSpace(_timestampSearchText) && !SearchMatches(box.Name, _timestampSearchText) &&
                !SearchMatches(box.LiveUrl, _timestampSearchText) && !SearchMatches(box.RecordingFileName, _timestampSearchText))
            {
                items = items.Where(i =>
                    SearchMatches(i.Time, _timestampSearchText) ||
                    SearchMatches(i.Body, _timestampSearchText) ||
                    SearchMatches(i.LiveUrl, _timestampSearchText) ||
                    SearchMatches(i.RecordingFileName, _timestampSearchText)).ToList();
            }

            foreach (var item in items)
            {
                itemsPanel.Children.Add(CreateTimestampRow(box, item));
            }
            stack.Children.Add(itemsPanel);

            border.Child = stack;
            return border;
        }

        private string GetTimestampSourceText(TimestampItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.LiveUrl))
                return $"配信URL: {item.LiveUrl.Trim()}";

            if (!string.IsNullOrWhiteSpace(item.RecordingFileName))
                return $"録画ファイル: {item.RecordingFileName.Trim()}";

            return "出典なし";
        }

        private string GetTimestampSourceValue(TimestampItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.LiveUrl))
                return item.LiveUrl.Trim();

            if (!string.IsNullOrWhiteSpace(item.RecordingFileName))
                return item.RecordingFileName.Trim();

            return string.Empty;
        }

        private Button CreateTimestampSourceCopyButton(TimestampItem item)
        {
            var button = CreateSmallButton("⧉");
            button.ToolTip = "出典をコピー";
            button.Click += (s, e) =>
            {
                var source = GetTimestampSourceValue(item);
                if (!string.IsNullOrWhiteSpace(source))
                    Clipboard.SetText(source);
            };
            return button;
        }

        private TextBlock CreateTimestampSourceDisplay(TimestampItem item)
        {
            return new TextBlock
            {
                Text = GetTimestampSourceText(item),
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
                ToolTip = GetTimestampSourceText(item)
            };
        }

        private Button CreateTimestampInfoButton(TimestampItem item)
        {
            var infoButton = CreateSmallButton("ⓘ");
            infoButton.Margin = new Thickness(2, 0, 2, 0);
            infoButton.ToolTip = "このタイムスタンプの出典";

            var popup = new Popup
            {
                PlacementTarget = infoButton,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var sourceText = new TextBlock
            {
                Text = GetTimestampSourceText(item),
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var copyButton = CreateTimestampSourceCopyButton(item);

            var popupPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            popupPanel.Children.Add(sourceText);
            popupPanel.Children.Add(copyButton);

            popup.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(32, 36, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(86, 97, 119)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Child = popupPanel
            };

            infoButton.Click += (s, e) =>
            {
                popup.IsOpen = !popup.IsOpen;
            };

            return infoButton;
        }

        private Border CreateTimestampRow(TimestampBox box, TimestampItem item)
        {
            var border = new Border { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)) };
            var stack = new StackPanel();
            var header = new DockPanel();
            var check = new CheckBox { IsChecked = item.IsChecked, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0) };
            check.Checked += (s, e) => { item.IsChecked = true; SaveTimestamps(); };
            check.Unchecked += (s, e) => { item.IsChecked = false; SaveTimestamps(); };
            var infoButton = CreateTimestampInfoButton(item);

            var copyButton = CreateSmallButton("⧉");
            copyButton.ToolTip = "時刻をコピー";
            copyButton.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(item.Time))
                    Clipboard.SetText(item.Time);
            };

            var deleteButton = CreateSmallButton("×");
            
            deleteButton.Foreground = Brushes.Red;
            deleteButton.Click += (s, e) =>
            { 
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    return;
                }

                box.Items.Remove(item); 
                SaveTimestamps(); 
                RenderTimestamps(); 
            
            };
            var timeBox = new TextBox { Text = item.Time, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Width = 90, TextAlignment = TextAlignment.Center };
            timeBox.PreviewTextInput += TimestampTimeBox_PreviewTextInput;
            timeBox.PreviewKeyDown += TimestampTimeBox_PreviewKeyDown;
            timeBox.TextChanged += TimestampTimeBox_TextChanged;
            timeBox.LostFocus += TimestampTimeBox_LostFocus;
            timeBox.TextChanged += (s, e) => { item.Time = timeBox.Text; SaveTimestamps(); };
            DockPanel.SetDock(check, Dock.Left);
            DockPanel.SetDock(deleteButton, Dock.Right);
            DockPanel.SetDock(copyButton, Dock.Right);
            DockPanel.SetDock(infoButton, Dock.Right);
            header.Children.Add(check);
            header.Children.Add(deleteButton);
            header.Children.Add(infoButton);

            // 時刻とコピーは隣り合わせにする。
            // copyButtonをheaderにも追加すると、同じUIElementを2つの親へ
            // 追加することになり「既に別の要素の論理子です」が発生する。
            var timeAndSource = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            timeAndSource.Children.Add(timeBox);
            timeAndSource.Children.Add(copyButton);

            if (_timestampSourceAlwaysVisible)
            {
                timeAndSource.Children.Add(CreateTimestampSourceDisplay(item));
            }

            header.Children.Add(timeAndSource);
            header.PreviewMouseDown += (s, e) => FocusTimestampTimeBoxFromLeftHitArea(header, timeBox, e);
            stack.Children.Add(header);
            var bodyBox = new TextBox { Text = item.Body, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 36 };
            bodyBox.TextChanged += (s, e) => { item.Body = bodyBox.Text; SaveTimestamps(); };
            stack.Children.Add(bodyBox);
            border.Child = stack;
            return border;
        }

        private void MoveTimestampBox(TimestampBox box, int direction)
        {
            var ordered = _timestampBoxes.ToList();
            var index = ordered.IndexOf(box);
            var newIndex = index + direction;
            if (index < 0 || newIndex < 0 || newIndex >= ordered.Count) return;
            (ordered[index], ordered[newIndex]) = (ordered[newIndex], ordered[index]);
            _timestampBoxes.Clear();
            _timestampBoxes.AddRange(ordered);
            NormalizeTimestampBoxOrder();
            SaveTimestamps();
            RenderTimestamps();
        }

        private void ReorderTimestampBox(TimestampBox source, TimestampBox target)
        {
            var ordered = _timestampBoxes.ToList();
            var oldIndex = ordered.IndexOf(source);
            var newIndex = ordered.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0)
            {
                return;
            }

            ordered.RemoveAt(oldIndex);
            ordered.Insert(newIndex, source);
            _timestampBoxes.Clear();
            _timestampBoxes.AddRange(ordered);
            NormalizeTimestampBoxOrder();
            SaveTimestamps();
            // ポップアップ内から呼ばれた場合は、呼び出し側のrefresh()で
            // ポップアップだけを即時再構築する。ここでRenderTimestamps()まで
            // 実行すると、非表示中の本体パネルを毎回再構築してしまい、
            // ポップアップの更新タイミングと競合する。
        }

        private void NormalizeTimestampBoxOrder()
        {
            // _timestampBoxes の並びそのものが表示順。
            for (var i = 0; i < _timestampBoxes.Count; i++)
            {
                _timestampBoxes[i].Order = i;
            }
        }

        private static TimeSpan ParseTimestamp(string value)
        {
            return TimeSpan.TryParse(value, out var parsed) ? parsed : TimeSpan.Zero;
        }

        // --- Extracted implementation for ExtractAmount previously lost --
        private int ExtractAmount(string message)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, @"\[￥([\d,]+)\]");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value.Replace(",", ""), out int amount))
                {
                    return amount;
                }
            }
            return 0;
        }

        private static int ExtractGiftCount(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return 0;

            var patterns = new[]
            {
                @"(\d+)\s*件",
                @"(\d+)\s*個",
                @"(\d+)\s*人",
                @"(\d+)\s*gift",
                @"(\d+)\s*memberships?",
                @"(\d+)\s*membership\s*gifts?"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);

                if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
                    return count;
            }

            return 0;
        }

        private void ChkSoundEffectsEnabled_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkSoundEffectsEnabled.IsChecked == true;

            // 親がOFFなら子もOFFにする
            if (!enabled)
            {
                ChkNotificationSoundEnabled.IsChecked = false;
            }
        }

        private Color GetSuperChatColor(int amount)
        {
            if (amount < 200) return Color.FromRgb(21, 101, 192); // Blue
            if (amount < 500) return Color.FromRgb(0, 229, 255); // Light Blue
            if (amount < 1000) return Color.FromRgb(29, 233, 182); // Green
            if (amount < 2000) return Color.FromRgb(255, 202, 40); // Yellow
            if (amount < 5000) return Color.FromRgb(245, 124, 0); // Orange
            if (amount < 10000) return Color.FromRgb(233, 30, 99); // Magenta
            return Color.FromRgb(230, 33, 23); // Red
        }

        private TimeSpan GetSuperChatDuration(int amount)
        {
            if (amount < 200) return TimeSpan.FromSeconds(30); // Modified so it's not 0
            if (amount < 500) return TimeSpan.FromMinutes(1);
            if (amount < 1000) return TimeSpan.FromMinutes(2);
            if (amount < 2000) return TimeSpan.FromMinutes(5);
            if (amount < 5000) return TimeSpan.FromMinutes(10);
            if (amount < 10000) return TimeSpan.FromMinutes(30);
            return TimeSpan.FromMinutes(60);
        }

        private string GetAudioPath(string fileName)
        {
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio");
            return Path.Combine(basePath, fileName);
        }
        
        private void ShowEventNotification(string header, string message, Color bgColor, TimeSpan duration, string? audioFilePath, float volume = 1.0f)
        {
            var item = new NotificationItem
            {
                Header = header,
                Message = message,
                BackgroundBrush = new SolidColorBrush(bgColor),
                ExpiryTime = DateTime.Now.Add(duration),
                
            };

            Notifications.Add(item);
            item.AnimateEnter();
            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                PlaySE(audioFilePath, volume);
            }

            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                item.AnimateLeave(() => Notifications.Remove(item));
            };
            timer.Start();

            item.Timer = timer;
        }

        private void NotificationClose_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: NotificationItem item })
            {
                item.Timer?.Stop();
                item.AnimateLeave(() => Notifications.Remove(item));
            }
        }

        // ====== Audio Playback =======

        private IWavePlayer? _sePlayer;
        private WaveStream? _seReader;

        private void PlaySE(string filePath, float volume = 1.0f)
        {
            try
            {
                if (!_soundEffectsEnabled && !Path.GetFileName(filePath).Equals("alerm.wav", StringComparison.OrdinalIgnoreCase)) return;
                if (!File.Exists(filePath)) return;

                if (filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    _seReader = new VorbisWaveReader(filePath);
                else
                    _seReader = new AudioFileReader(filePath);

                _sePlayer = new WaveOutEvent();
                _sePlayer.Init(_seReader);
                _sePlayer.Volume = volume;
                _sePlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play SE: {ex.Message}");
            }
        }
        
        // --------------------------------------------------------------------------------------


        private void MovePanelChild(UIElement element, int direction)
        {
            if (element is not FrameworkElement { Parent: Panel panel })
            {
                return;
            }

            var index = panel.Children.IndexOf(element);
            var newIndex = index + direction;
            if (index < 0 || newIndex < 0 || newIndex >= panel.Children.Count)
            {
                return;
            }

            panel.Children.RemoveAt(index);
            panel.Children.Insert(newIndex, element);
            SaveMemos();
        }

        private TextBlock CreateDragHandle(Border card)
        {
            var handle = new TextBlock
            {
                Text = "⋮",
                Foreground = Brushes.LightGray,
                FontSize = 20,
                Width = 16,
                Cursor = Cursors.SizeAll,
                VerticalAlignment = VerticalAlignment.Stretch,
                TextAlignment = TextAlignment.Center
            };
            handle.MouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _dragSourceCard = card;
                    DragDrop.DoDragDrop(handle, card, DragDropEffects.Move);
                }
            };
            return handle;
        }

        private void EnableCardDrop(Border card)
        {
            card.AllowDrop = true;
            card.Drop += (s, e) =>
            {
                if (_dragSourceCard == null || ReferenceEquals(_dragSourceCard, card))
                {
                    return;
                }

                if (_dragSourceCard.Parent is not Panel sourcePanel || card.Parent is not Panel targetPanel || !ReferenceEquals(sourcePanel, targetPanel))
                {
                    return;
                }

                var oldIndex = sourcePanel.Children.IndexOf(_dragSourceCard);
                var newIndex = targetPanel.Children.IndexOf(card);
                if (oldIndex < 0 || newIndex < 0)
                {
                    return;
                }

                sourcePanel.Children.RemoveAt(oldIndex);
                targetPanel.Children.Insert(newIndex, _dragSourceCard);

                // DOM上の順序を確定してから保存する。
                // これにより、メインのメモパネルで並び替えた順序と、
                // 次に開く全メモポップアップの順序が必ず一致する。
                targetPanel.UpdateLayout();
                var movedCard = _dragSourceCard;
                _dragSourceCard = null;
                e.Handled = true;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SaveMemos();
                    if (movedCard != null)
                        movedCard.UpdateLayout();
                }), System.Windows.Threading.DispatcherPriority.DataBind);
            };
        }

        private Button CreateIconButton(string content, RoutedEventHandler click)
        {
            var button = CreateSmallButton(content);
            button.Click += click;
            return button;
        }

        private Popup CreateChecklistFilterPopup(UIElement placementTarget, Func<int> getMode, Action<int> setMode)
        {
            var popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                Margin = new Thickness(0),
                MinWidth = 170
            };
            panel.Children.Add(new TextBlock
            {
                Text = "表示する項目",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 8, 10, 4)
            });

            var group = $"ChecklistFilter_{Guid.NewGuid():N}";
            void AddGroupedOption(string text, int mode)
            {
                var radio = new RadioButton
                {
                    Content = text,
                    Tag = mode,
                    Foreground = Brushes.White,
                    Margin = new Thickness(10, 4, 10, 4),
                    IsChecked = getMode() == mode,
                    GroupName = group
                };
                radio.Checked += (s, e) =>
                {
                    if (radio.Tag is int selectedMode)
                    {
                        setMode(selectedMode);
                        popup.IsOpen = false;
                    }
                };
                panel.Children.Add(radio);
            }

            AddGroupedOption("全て表示", 0);
            AddGroupedOption("1にチェック済み", 1);
            AddGroupedOption("2にチェック済み", 2);
            AddGroupedOption("1と2の両方チェック済み", 3);
            AddGroupedOption("1だけチェック済み", 4);
            AddGroupedOption("2だけチェック済み", 5);
            popup.Child = new Border
            {
                Background = panel.Background,
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                BorderThickness = new Thickness(1),
                Child = panel
            };
            return popup;
        }

        private Border CreateMemoContainer(string title = "無題", string text = "", Panel? ownerPanel = null)
        {
            Border border = new Border
            {
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(
                Color.FromArgb(255, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(
                Color.FromArgb(255, 80, 80, 80)),
                Tag = ownerPanel
            };

            EnableCardDrop(border);

            StackPanel mainStack = new StackPanel();

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainStack.Children.Add(header);
            var handle = CreateDragHandle(border);
            Grid.SetColumn(handle, 0);
            header.Children.Add(handle);

            TextBox titleBox = new TextBox
            {
                Text = string.IsNullOrEmpty(title) ? "無題" : title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(titleBox, 1);
            titleBox.TextChanged += (s, e) => SaveMemos();
            header.Children.Add(titleBox);

            var popoutButton = new Button
            {
                Uid = "MemoPopoutButton",
                Content = "□",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "このメモを個別にウィンドウ表示",
                Margin = new Thickness(0, 0, 5, 0)
            };
            popoutButton.Click += BtnIndividualPopOut_Click;

            // アプリ内フローティング時だけ表示する「ポップアップを閉じる」ボタン
            // 通常時は非表示にして、別ウィンドウへ切り離すボタンと役割を分ける。
            var internalCloseButton = new Button
            {
                Uid = "MemoInternalCloseButton",
                Content = "□",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "ポップアップを閉じる",
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 5, 0)
            };
            internalCloseButton.Click += InternalMemoCloseButton_Click;

            Button closeButton = new Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Foreground = Brushes.Red,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) =>
            {
                // Shiftキーを押していなければ何もしない
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    return;
                }

                var targetPanel = ownerPanel ?? border.Parent as Panel;

                // targetPanelが空でなければ、RemoveMemoOrClear(メモ消去)を実行
                if (targetPanel != null)
                {
                    RemoveMemoOrClear(border, targetPanel);
                }
                else
                {
                    ResetMemoContent(border);
                }
            };

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(btnStack, 2);
            var collapseButton = CreateSmallButton("∧");
            btnStack.Children.Add(internalCloseButton);
            btnStack.Children.Add(popoutButton);
            btnStack.Children.Add(CreateIconButton("↧", (s, e) => ExportTextFile(GetBoxTitle(border, "メモ"), BuildMemoBoxExportText(border))));
            btnStack.Children.Add(collapseButton);
            btnStack.Children.Add(closeButton);
            header.Children.Add(btnStack);

            TextBox contentBox = new TextBox
            {
                Text = text,
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60,
                Margin = new Thickness(0, 5, 0, 0)
            };
            contentBox.TextChanged += (s, e) => SaveMemos();
            mainStack.Children.Add(contentBox);
            // Resize grip
            Grid resizer = new Grid
            {
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNS
            };
            resizer.MouseMove += MemoResizeGrip_MouseMove;
            resizer.MouseLeftButtonDown += MemoResizeGrip_MouseLeftButtonDown;
            resizer.MouseLeftButtonUp += MemoResizeGrip_MouseLeftButtonUp;
            collapseButton.Click += (s, e) =>
            {
                var collapsed = contentBox.Visibility == Visibility.Visible;
                contentBox.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                resizer.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                collapseButton.Content = collapsed ? "∨" : "∧";
                SaveMemos();
            };
            border.Child = new Grid
            {
                Background = Brushes.Transparent,
                Children =
                {
                    mainStack,
                    resizer
                }
            };

            _memoFolderIds[border] = string.IsNullOrEmpty(_activeTextFolderId)
                ? _textFolders.FirstOrDefault()?.Id ?? string.Empty
                : _activeTextFolderId;
            AddFolderSelectorToMemoCard(border, "text");

            return border;
        }

        private Border CreateListMemoContainer(string title = "リスト", Panel? ownerPanel = null)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Tag = ownerPanel
            };
            EnableCardDrop(border);

            var root = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.Children.Add(header);
            var handle = CreateDragHandle(border);
            Grid.SetColumn(handle, 0);
            header.Children.Add(handle);

            var titleBox = new TextBox
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White
            };
            Grid.SetColumn(titleBox, 1);
            titleBox.TextChanged += (s, e) => SaveMemos();
            header.Children.Add(titleBox);

            var itemsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var filterMode = 0;

            Action applyFilter = () =>
            {
                foreach (var row in itemsPanel.Children.OfType<StackPanel>())
                {
                    var checks = row.Children.OfType<CheckBox>().ToList();

                    bool chk1 = checks.ElementAtOrDefault(0)?.IsChecked == true;
                    bool chk2 = checks.ElementAtOrDefault(1)?.IsChecked == true;

                    row.Visibility = filterMode switch
                    {
                        // 全て表示
                        0 => Visibility.Visible,

                        // 1にチェック済み（2もチェックされていてOK）
                        1 => chk1 ? Visibility.Visible : Visibility.Collapsed,

                        // 2にチェック済み（1もチェックされていてOK）
                        2 => chk2 ? Visibility.Visible : Visibility.Collapsed,

                        // 1と2の両方チェック済み
                        3 => chk1 && chk2 ? Visibility.Visible : Visibility.Collapsed,

                        // 1だけチェック済み（2は未チェック）
                        4 => chk1 && !chk2 ? Visibility.Visible : Visibility.Collapsed,

                        // 2だけチェック済み（1は未チェック）
                        5 => !chk1 && chk2 ? Visibility.Visible : Visibility.Collapsed,

                        _ => Visibility.Visible
                    };
                }
            };
            Action addItem = () =>
            {
                var row = CreateListItemRow(applyFilter);

                var checks = row.Children.OfType<CheckBox>().ToList();

                // 現在の絞り込み条件に合わせて新規項目のチェック状態を設定
                switch (filterMode)
                {
                    // 1にチェック済み
                    // → 1だけチェック
                    case 1:
                    case 4:
                        if (checks.Count >= 2)
                        {
                            checks[0].IsChecked = true;
                            checks[1].IsChecked = false;
                        }
                        break;

                    // 2にチェック済み
                    // → 2だけチェック
                    case 2:
                    case 5:
                        if (checks.Count >= 2)
                        {
                            checks[0].IsChecked = false;
                            checks[1].IsChecked = true;
                        }
                        break;

                    // 両方チェック済み
                    case 3:
                        if (checks.Count >= 2)
                        {
                            checks[0].IsChecked = true;
                            checks[1].IsChecked = true;
                        }
                        break;

                    // 全て表示
                    case 0:
                    default:
                        break;
                }

                itemsPanel.Children.Add(row);
                applyFilter();
                SaveMemos();
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };

            // テキストメモと同じ個別ポップアップ処理をチェックリストにも適用。
            var popoutButton = CreateIconButton("□", BtnIndividualPopOut_Click);
            popoutButton.Uid = "MemoPopoutButton";
            popoutButton.ToolTip = "このメモを個別にウィンドウ表示";
            popoutButton.Margin = new Thickness(0, 0, 5, 0);
            buttons.Children.Add(popoutButton);

            // アプリ内フローティング時だけ表示するポップアップ解除ボタン。
            var internalCloseButton = CreateIconButton("□", InternalMemoCloseButton_Click);
            internalCloseButton.Uid = "MemoInternalCloseButton";
            internalCloseButton.ToolTip = "ポップアップを閉じる";
            internalCloseButton.Visibility = Visibility.Collapsed;
            internalCloseButton.Margin = new Thickness(0, 0, 5, 0);
            buttons.Children.Add(internalCloseButton);

            buttons.Children.Add(CreateIconButton("↧", (s, e) => ExportTextFile(GetBoxTitle(border, "リスト"), BuildListBoxExportText(border))));
            buttons.Children.Add(CreateIconButton("＋", (s, e) => addItem()));
            var searchButton = CreateSmallButton("🔍");
            searchButton.ToolTip = "チェック状態で絞り込み";
            
            
            void UpdateFilterButtonColor()
            {
                searchButton.Foreground = filterMode == 0 ? Brushes.White : Brushes.DodgerBlue;
            }
            
            var filterPopup = CreateChecklistFilterPopup(searchButton, () => filterMode, mode =>
            {
                filterMode = mode;
                applyFilter();
                UpdateFilterButtonColor();
            });

            UpdateFilterButtonColor();
            searchButton.Click += (s, e) => filterPopup.IsOpen = true;
            buttons.Children.Add(searchButton);
            var collapseButton = CreateSmallButton("∧");
            collapseButton.Click += (s, e) =>
            {
                var collapsed = itemsPanel.Visibility == Visibility.Visible;
                itemsPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                collapseButton.Content = collapsed ? "∨" : "∧";
                SaveMemos();
            };
            buttons.Children.Add(collapseButton);
            var deleteButton = CreateIconButton("×", (s, e) =>
            {
                // shiftを必要にする
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                     return;
                }

                var targetPanel = ownerPanel ?? border.Parent as Panel;
                if (targetPanel != null) RemoveMemoOrClear(border, targetPanel);
            });

            

            deleteButton.Foreground = Brushes.Red;
            buttons.Children.Add(deleteButton);
            Grid.SetColumn(buttons, 2);
            header.Children.Add(buttons);

            root.Children.Add(itemsPanel);
            border.Child = root;
            addItem();

            _memoFolderIds[border] = string.IsNullOrEmpty(_activeListFolderId)
                ? _listFolders.FirstOrDefault()?.Id ?? string.Empty
                : _activeListFolderId;
            AddFolderSelectorToMemoCard(border, "list");

            return border;
        }

        private StackPanel CreateListItemRow(Action applyFilter)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var chk1 = new CheckBox { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var chk2 = new CheckBox { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            chk1.Checked += (s, e) => { applyFilter(); SaveMemos(); };
            chk1.Unchecked += (s, e) => { applyFilter(); SaveMemos(); };
            chk2.Checked += (s, e) => { applyFilter(); SaveMemos(); };
            chk2.Unchecked += (s, e) => { applyFilter(); SaveMemos(); };
            var text = new TextBox { Text = "項目", Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), Width = 230 };
            text.TextChanged += (s, e) => SaveMemos();
            var delete = CreateSmallButton("×");
            delete.Foreground = Brushes.Red;
            delete.Click += (s, e) =>
            {

                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    return;
                }


                if (row.Parent is Panel panel)
                {
                    panel.Children.Remove(row);
                    SaveMemos();
                }
            };
            row.Children.Add(chk1);
            row.Children.Add(chk2);
            row.Children.Add(text);
            row.Children.Add(delete);
            return row;
        }

        private void MemoResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                _draggingFloatingWindow = fe;
                _floatingDragMouseStart = Mouse.GetPosition(null);
                _floatingDragElementStart = new Point(fe.Width, fe.Height);

                fe.CaptureMouse();
            }
        }

        private void MemoResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingFloatingWindow != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = Mouse.GetPosition(null);
                double diffX = pos.X - _floatingDragMouseStart.X;
                double diffY = pos.Y - _floatingDragMouseStart.Y;

                double newWidth = Math.Max(100, _floatingDragElementStart.X + diffX);
                double newHeight = Math.Max(100, _floatingDragElementStart.Y + diffY);

                _draggingFloatingWindow.Width = newWidth;
                _draggingFloatingWindow.Height = newHeight;
            }
        }

        private void MemoResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingFloatingWindow != null)
            {
                _draggingFloatingWindow.ReleaseMouseCapture();
                _draggingFloatingWindow = null;
            }
        }

        private void AddLegacyTextMemo()
        {
            var memoContainer = CreateMemoContainer();
            TextMemosPanel.Children.Add(memoContainer);
        }

        private void BtnExportAllMemos_Click(object sender, RoutedEventArgs e) =>
            ExportTextFile("OSAKA_all", BuildAllExportText());

        private void BtnExportTextMemos_Click(object sender, RoutedEventArgs e) =>
            ExportTextFile("OSAKA_text_memos", BuildTextMemoExportText());

        private void BtnExportListMemos_Click(object sender, RoutedEventArgs e) =>
            ExportTextFile("OSAKA_list_memos", BuildListMemoExportText());

        private void BtnExportTimestamps_Click(object sender, RoutedEventArgs e) =>
            ExportTextFile("OSAKA_timestamps", BuildTimestampExportText());

        private void ExportTextFile(string prefix, string content)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{SanitizeFileName(prefix)}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog(this) == true)
            {
                File.WriteAllText(dialog.FileName, content);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((string.IsNullOrWhiteSpace(name) ? "export" : name).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
        }

        private string BuildAllExportText() =>
            BuildTextMemoExportText() + Environment.NewLine + BuildListMemoExportText() + Environment.NewLine + BuildTimestampExportText();

        private string BuildTextMemoExportText()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("--- テキストメモ ---");
            foreach (var child in TextMemosPanel.Children.OfType<Border>())
            {
                var boxes = child.Descendants().OfType<TextBox>().ToList();
                text.AppendLine($"[{(boxes.Count > 0 ? boxes[0].Text : "無題")}]");
                if (boxes.Count > 1) text.AppendLine(boxes[1].Text);
                text.AppendLine("-----");
            }
            return text.ToString();
        }

        private string BuildMemoBoxExportText(Border box)
        {
            var boxes = box.Descendants().OfType<TextBox>().ToList();
            var text = new System.Text.StringBuilder();
            text.AppendLine($"[{(boxes.Count > 0 ? boxes[0].Text : "無題")}]");
            if (boxes.Count > 1) text.AppendLine(boxes[1].Text);
            return text.ToString();
        }

        private string GetBoxTitle(Border box, string fallback)
        {
            return box.Descendants().OfType<TextBox>().FirstOrDefault()?.Text ?? fallback;
        }

        private string BuildListMemoExportText()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("--- リストメモ ---");
            foreach (var child in ListMemosPanel.Children.OfType<Border>())
            {
                var boxes = child.Descendants().OfType<TextBox>().ToList();
                text.AppendLine($"[{(boxes.Count > 0 ? boxes[0].Text : "リスト")}]");
                foreach (var row in child.Descendants().OfType<StackPanel>())
                {
                    var chk = row.Children.OfType<CheckBox>().FirstOrDefault();
                    var itemText = row.Children.OfType<TextBox>().FirstOrDefault();
                    if (chk != null && itemText != null)
                    {
                        var checks = row.Children.OfType<CheckBox>().ToList();
                        text.AppendLine($"{(checks.ElementAtOrDefault(0)?.IsChecked == true ? "[x]" : "[ ]")} {(checks.ElementAtOrDefault(1)?.IsChecked == true ? "[x]" : "[ ]")} {itemText.Text}");
                    }
                }
                text.AppendLine();
            }
            return text.ToString();
        }

        private string BuildListBoxExportText(Border box)
        {
            var text = new System.Text.StringBuilder();
            var boxes = box.Descendants().OfType<TextBox>().ToList();
            text.AppendLine($"[{(boxes.Count > 0 ? boxes[0].Text : "リスト")}]");
            foreach (var row in box.Descendants().OfType<StackPanel>())
            {
                var checks = row.Children.OfType<CheckBox>().ToList();
                var itemText = row.Children.OfType<TextBox>().FirstOrDefault();
                if (checks.Count >= 2 && itemText != null)
                {
                    text.AppendLine($"{(checks[0].IsChecked == true ? "[x]" : "[ ]")} {(checks[1].IsChecked == true ? "[x]" : "[ ]")} {itemText.Text}");
                }
            }
            return text.ToString();
        }

        private string BuildTimestampExportText()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("--- タイムスタンプ ---");
            foreach (var box in GetAllTimestampBoxesInDisplayOrder())
            {
                text.AppendLine($"[{box.Name}]");
                var items = box.SortDescending
                    ? box.Items.OrderByDescending(i => ParseTimestamp(i.Time))
                    : box.Items.OrderBy(i => ParseTimestamp(i.Time));
                foreach (var item in items)
                {
                    text.AppendLine($"{(item.IsChecked ? "[x]" : "[ ]")} {item.Time}");
                    if (!string.IsNullOrWhiteSpace(item.Body)) text.AppendLine(item.Body);
                    text.AppendLine();
                }
            }
            return text.ToString();
        }

        private void BtnExportMemos_Click(object sender, RoutedEventArgs e)
        {
            SaveMemos();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"OSAKA_memos_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog(this) == true)
            {
                File.WriteAllText(dialog.FileName, BuildMemoExportText());
            }
        }

        private string BuildMemoExportText()
        {
            var txtExport = new System.Text.StringBuilder();
            txtExport.AppendLine("--- テキストメモ ---");
            foreach (var child in TextMemosPanel.Children)
            {
                if (child is Border b && b.Child is StackPanel sp)
                {
                    txtExport.AppendLine($"[{GetTitleBox(b)?.Text ?? "無題"}]");
                    txtExport.AppendLine(sp.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? "");
                    txtExport.AppendLine("-----");
                }
            }

            txtExport.AppendLine();
            txtExport.AppendLine("--- リストメモ ---");
            foreach (var child in ListMemosPanel.Children)
            {
                if (child is Border b && b.Child is StackPanel mainStack)
                {
                    var title = GetTitleBox(b)?.Text ?? "リスト";
                    txtExport.AppendLine($"[{title}]");

                    var itemsStack = mainStack.Children.OfType<StackPanel>().FirstOrDefault(panel => panel.Name == "ItemsStack" || panel.Name == "InitialCheckListItems");
                    if (itemsStack != null)
                    {
                        foreach (var itemRow in itemsStack.Children.OfType<StackPanel>())
                        {
                            if (itemRow.Children.Count >= 2 && itemRow.Children[0] is CheckBox chk && itemRow.Children[1] is TextBox tbItem)
                            {
                                txtExport.AppendLine($"{(chk.IsChecked == true ? "[x]" : "[ ]")} {tbItem.Text}");
                            }
                        }
                    }

                    txtExport.AppendLine();
                }
            }

            return txtExport.ToString();
        }


        private void ShowMemoAsInternalWindow(Border memo)
        {
            // 別ウィンドウから呼ばれた場合は、そのWindowを先に取得する。
            Window? parentWindow = Window.GetWindow(memo);

            // 現在の親から切り離す
            if (memo.Parent is Panel panel)
                panel.Children.Remove(memo);
            else if (memo.Parent is ContentControl contentControl)
                contentControl.Content = null;
            else if (memo.Parent is Decorator decorator)
                decorator.Child = null;

            Canvas.SetLeft(memo, 100);
            Canvas.SetTop(memo, 100);
            memo.Margin = new Thickness(0);

            if (memo.Parent != FloatingMemoCanvas)
                FloatingMemoCanvas.Children.Add(memo);

            memo.IsHitTestVisible = true;

            // Uidで2つの□ボタンを完全に区別する。
            var internalCloseButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoInternalCloseButton");

            var popoutButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

            // アプリ内ポップアップ状態：
            //   □ = ポップアップ解除
            //   ↗ = 別ウィンドウへ切り離す
            if (internalCloseButton != null)
            {
                internalCloseButton.Content = "□";
                internalCloseButton.ToolTip = "ポップアップを閉じる";
                internalCloseButton.Visibility = Visibility.Visible;
                internalCloseButton.Tag = null;
            }

            if (popoutButton != null)
            {
                popoutButton.Click -= ExternalMemoCloseButton_Click;
                popoutButton.Click -= BtnIndividualPopOut_Click;
                popoutButton.Click += BtnIndividualPopOut_Click;

                popoutButton.Content = "↗";
                popoutButton.ToolTip = "別ウィンドウとして表示";
                popoutButton.Tag = null;
            }

            // 別ウィンドウ専用の↙はアプリ内では存在させない。
            var externalIntegrateButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoIntegrateButton");

            if (externalIntegrateButton?.Parent is StackPanel buttonStack)
            {
                externalIntegrateButton.Click -= ExternalMemoIntegrateButton_Click;
                buttonStack.Children.Remove(externalIntegrateButton);
            }

            Panel.SetZIndex(memo, 100);

            memo.MouseLeftButtonDown -= FloatingMemo_MouseLeftButtonDown;
            memo.MouseMove -= FloatingMemo_MouseMove;
            memo.MouseLeftButtonUp -= FloatingMemo_MouseLeftButtonUp;

            memo.MouseLeftButtonDown += FloatingMemo_MouseLeftButtonDown;
            memo.MouseMove += FloatingMemo_MouseMove;
            memo.MouseLeftButtonUp += FloatingMemo_MouseLeftButtonUp;

            // ↙による統合の場合だけ、元の別Windowを閉じる。
            // 通常のアプリ内ポップアップ化では何もしない。
            if (parentWindow != null && parentWindow != this)
            {
                parentWindow.Close();
            }
        }


        private void InternalMemoCloseButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var memo = FindMemoContainer(button);
            if (memo == null)
                return;

            var ownerPanel = memo.Tag as Panel;
            if (ownerPanel == null)
                return;

            // FloatingMemoCanvasから元のメモパネルへ戻す。
            if (memo.Parent is Panel panel)
                panel.Children.Remove(memo);

            memo.ClearValue(Canvas.LeftProperty);
            memo.ClearValue(Canvas.TopProperty);
            memo.Margin = new Thickness(0, 5, 0, 5);

            if (!ownerPanel.Children.Contains(memo))
                ownerPanel.Children.Add(memo);

            // 通常状態：
            //   □ = 別ウィンドウとして表示
            //   ポップアップ解除ボタン = 非表示
            button.Visibility = Visibility.Collapsed;

            var popoutButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoPopoutButton");

            if (popoutButton != null)
            {
                popoutButton.Click -= ExternalMemoCloseButton_Click;
                popoutButton.Click -= BtnIndividualPopOut_Click;
                popoutButton.Click += BtnIndividualPopOut_Click;

                popoutButton.Content = "□";
                popoutButton.ToolTip = "このメモを個別にウィンドウ表示";
                popoutButton.Tag = null;
                popoutButton.Visibility = Visibility.Visible;
            }

            // 念のため↙ボタンが残っていれば削除する。
            var integrateButton = memo.Descendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Uid == "MemoIntegrateButton");

            if (integrateButton?.Parent is StackPanel buttonStack)
            {
                integrateButton.Click -= ExternalMemoIntegrateButton_Click;
                buttonStack.Children.Remove(integrateButton);
            }

            Panel.SetZIndex(memo, 0);
        }


        private void FloatingMemo_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
        {
            if (sender is not Border memo)
                return;

            // ボタンやTextBoxを操作しているときは移動させない
            if (e.OriginalSource is DependencyObject source)
            {
                if (source is Button ||
                    source is TextBox ||
                    source is CheckBox)
                {
                    return;
                }
            }

            _draggingFloatingMemo = memo;

            _floatingMemoMouseStart =
                e.GetPosition(FloatingMemoCanvas);

            _floatingMemoStartLeft =
                Canvas.GetLeft(memo);

            _floatingMemoStartTop =
                Canvas.GetTop(memo);

            memo.CaptureMouse();

            Panel.SetZIndex(memo, 1000);

            e.Handled = true;
        }

        private void FloatingMemo_MouseMove( object sender, MouseEventArgs e)
        {
            if (_draggingFloatingMemo == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current =
                e.GetPosition(FloatingMemoCanvas);

            double dx = current.X - _floatingMemoMouseStart.X;
            double dy = current.Y - _floatingMemoMouseStart.Y;

            double left = _floatingMemoStartLeft + dx;
            double top = _floatingMemoStartTop + dy;

            left = Math.Max(0, left);
            top = Math.Max(0, top);

            Canvas.SetLeft(_draggingFloatingMemo, left);
            Canvas.SetTop(_draggingFloatingMemo, top);
        }

        private void FloatingMemo_MouseLeftButtonUp(object sender,MouseButtonEventArgs e)
        {
            if (_draggingFloatingMemo != null)
            {
                _draggingFloatingMemo.ReleaseMouseCapture();
                _draggingFloatingMemo = null;
            }
        }


        private void UpdateChatOverlayVisibility()
        {

            // 設定画面を開いている間も非表示
            if (SettingsPopup.Visibility == Visibility.Visible)
            {
                ChatOverlayArea.Visibility = Visibility.Hidden;
                return;
            }

            // OBS設定詳細を開いている間も非表示
            if (ObsSettingsDetail.Visibility == Visibility.Visible)
            {
                ChatOverlayArea.Visibility = Visibility.Hidden;
                return;
            }

            // チャット非表示ボタンの状態
            ChatOverlayArea.Visibility =
                _isChatHidden
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }



    }

    public class AppSettings
    {
        public string ObsWebSocketUrl { get; set; } = "192.168.1.106:4455";
        public string ObsPassword { get; set; } = "";
        public string YouTubeChannelUrl { get; set; } = "https://www.youtube.com/@urota_7";
    }

    
    public class LoopStream : WaveStream
    {
        private readonly WaveStream _sourceStream;

        public LoopStream(WaveStream sourceStream)
        {
            _sourceStream = sourceStream;
            EnableLooping = true;
        }

        public bool EnableLooping { get; set; }

        public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
        public override long Length => _sourceStream.Length;

        public override long Position
        {
            get => _sourceStream.Position;
            set => _sourceStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                var bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    if (_sourceStream.Position == 0 || !EnableLooping)
                    {
                        break;
                    }

                    _sourceStream.Position = 0;
                }
                else
                {
                    totalBytesRead += bytesRead;
                }
            }

            return totalBytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sourceStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    

}
