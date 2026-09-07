using System.Configuration;
using System.Data;
using System.Windows;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.IO;
using Velopack;
using Velopack.Sources;

namespace OSAKA
{
    public partial class App : System.Windows.Application
    {
        public static bool IsBackgroundStartup { get; private set; }

        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _showMainWindowEvent;
        private static RegisteredWaitHandle? _showMainWindowRegisteredWait;

        private const string MutexName = @"Local\OSAKA_SingleInstance_8D7C4A2E";
        private const string ShowWindowEventName = @"Local\OSAKA_ShowMainWindow_8D7C4A2E";

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: MutexName,
                createdNew: out createdNew);

            if (!createdNew)
            {
                try
                {
                    using var showEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
                    showEvent.Set();
                }
                catch
                {
                }

                Shutdown();
                return;
            }

            _showMainWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            _showMainWindowRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
                _showMainWindowEvent, OnShowMainWindowRequested, null, Timeout.Infinite, false);

            VelopackApp.Build().Run();
            base.OnStartup(e);

            IsBackgroundStartup = e.Args.Any(arg =>
                arg.Equals("--background", StringComparison.OrdinalIgnoreCase));

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            // バージョン更新後の初回起動だけ「使い方」を表示する。
            // 初回インストール時も、保存済みバージョンがないため表示する。
            ShowHowToWindowIfVersionChanged(mainWindow);

            CheckForUpdates();
        }


        private static void ShowHowToWindowIfVersionChanged(MainWindow mainWindow)
        {
            try
            {
                string currentVersion = Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString() ?? "0.0.0.0";

                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OSAKA");
                Directory.CreateDirectory(appDataDir);

                string versionFile = Path.Combine(appDataDir, "LastShownVersion.txt");
                string? lastShownVersion = File.Exists(versionFile)
                    ? File.ReadAllText(versionFile).Trim()
                    : null;

                if (string.Equals(lastShownVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                    return;

                // 現在のバージョンを先に記録して、同じバージョンでの再起動では表示しない。
                File.WriteAllText(versionFile, currentVersion);

                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!mainWindow.IsVisible)
                        return;

                    var window = new HowToWindow
                    {
                        Owner = mainWindow
                    };
                    window.Show();
                    window.Activate();
                }));
            }
            catch
            {
                // 使い方ウィンドウの表示判定でアプリ本体を起動不能にしない。
            }
        }

        private void OnShowMainWindowRequested(object? state, bool timedOut)
        {
            if (timedOut) return;
            Dispatcher.BeginInvoke(new Action(ShowExistingMainWindow));
        }

        private void ShowExistingMainWindow()
        {
            try
            {
                if (MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ShowMainWindowFromExternalRequest();
                    return;
                }

                if (MainWindow is Window window)
                {
                    window.ShowInTaskbar = true;
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    if (!window.IsVisible)
                        window.Show();
                    window.Activate();
                }
            }
            catch
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _showMainWindowRegisteredWait?.Unregister(null);
                _showMainWindowRegisteredWait = null;
                _showMainWindowEvent?.Dispose();
                _showMainWindowEvent = null;
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            }
            catch
            {
            }

            base.OnExit(e);
        }

        private async void CheckForUpdates()
        {


            try
            {
                var source = new GithubSource(
                    "https://github.com/VLOTAN/NO46081",
                    null,
                    false);



                var updateManager = new UpdateManager(source);


                var updateInfo = await updateManager.CheckForUpdatesAsync();



                if (updateInfo == null)
                    return;



                await updateManager.DownloadUpdatesAsync(updateInfo);

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"アップデートエラー\n\n{ex}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}