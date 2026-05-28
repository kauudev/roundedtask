using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RoundedTask
{
    internal sealed class RoundedTaskContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _signalTimer;
        private readonly TaskbarStyler _styler;
        private readonly TaskbarMessageWindow _messageWindow;
        private readonly EventWaitHandle _showSettingsSignal;
        private AppSettings _settings;
        private SettingsForm _settingsForm;
        private bool _highResolutionTimerActive;

        public RoundedTaskContext(EventWaitHandle showSettingsSignal, bool showSettingsOnStart)
        {
            _showSettingsSignal = showSettingsSignal;
            _settings = AppSettings.Load();
            _styler = new TaskbarStyler(_settings);
            _messageWindow = new TaskbarMessageWindow();
            _messageWindow.TaskbarCreated += delegate { ApplyNow(true); };
            _messageWindow.TaskbarChanged += delegate { ApplyNow(true); };

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = LoadTrayIcon();
            _notifyIcon.Text = "RoundedTask";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = BuildMenu();
            _notifyIcon.DoubleClick += delegate { ShowSettings(); };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = GetEffectivePollInterval(_settings);
            _timer.Tick += delegate { ApplyNow(false); };
            _timer.Start();

            _signalTimer = new System.Windows.Forms.Timer();
            _signalTimer.Interval = 350;
            _signalTimer.Tick += delegate
            {
                if (_showSettingsSignal != null && _showSettingsSignal.WaitOne(0))
                {
                    ShowSettings();
                }
            };
            _signalTimer.Start();

            ApplyNow(true);

            if (showSettingsOnStart)
            {
                ShowSettings();
            }
        }

        private static Icon LoadTrayIcon()
        {
            string path = Path.Combine(Application.StartupPath, "assets", "roundedtask.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }

            Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            return extracted != null ? extracted : SystemIcons.Application;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_settings.RestoreOnExit)
                {
                    _styler.RestoreAll();
                }

                _timer.Stop();
                SetHighResolutionTimer(false);
                _timer.Dispose();
                _signalTimer.Stop();
                _signalTimer.Dispose();
                _messageWindow.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();

                if (_settingsForm != null)
                {
                    _settingsForm.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Defini\u00e7\u00f5es");
            settingsItem.Click += delegate { ShowSettings(); };
            menu.Items.Add(settingsItem);

            ToolStripMenuItem applyItem = new ToolStripMenuItem("Aplicar agora");
            applyItem.Click += delegate { ApplyNow(true); };
            menu.Items.Add(applyItem);

            ToolStripMenuItem restoreItem = new ToolStripMenuItem("Restaurar e pausar");
            restoreItem.Click += delegate
            {
                SetHighResolutionTimer(false);
                _settings.Enabled = false;
                _settings.Save();
                _styler.UpdateSettings(_settings);
                _styler.RestoreAll();
            };
            menu.Items.Add(restoreItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Sair");
            exitItem.Click += delegate { ExitThread(); };
            menu.Items.Add(exitItem);

            return menu;
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings);
                _settingsForm.SettingsSaved += OnSettingsSaved;
                _settingsForm.RestoreRequested += delegate
                {
                    _styler.RestoreAll();
                };
            }

            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void OnSettingsSaved(object sender, SettingsSavedEventArgs e)
        {
            _settings = e.Settings.Clone();
            _settings.Save();
            _styler.UpdateSettings(_settings);
            _timer.Interval = GetEffectivePollInterval(_settings);
            ApplyNow(true);
        }

        private static int GetEffectivePollInterval(AppSettings settings)
        {
            int interval = settings.PollIntervalMs;
            bool dynamicSegments = String.Equals(settings.ShapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase);
            if (dynamicSegments && (settings.ShowTrayOnHover || settings.ShowAppsOnHover))
            {
                interval = Math.Min(interval, 60);
            }
            else if (dynamicSegments)
            {
                interval = Math.Min(interval, 250);
            }

            return Math.Max(40, interval);
        }

        private void ApplyNow(bool force)
        {
            try
            {
                int count = _styler.Apply(force);
                SetHighResolutionTimer(_styler.HasActiveHoverAnimation);
                int nextInterval = _styler.HasActiveHoverAnimation ? _styler.ActiveHoverFrameIntervalMs : GetEffectivePollInterval(_settings);
                if (_timer.Interval != nextInterval)
                {
                    _timer.Interval = nextInterval;
                }

                _notifyIcon.Text = count > 0 ? "RoundedTask - ativo" : "RoundedTask";
            }
            catch (Exception ex)
            {
                _notifyIcon.Text = "RoundedTask - pausado";
                _notifyIcon.ShowBalloonTip(3000, "RoundedTask", ex.Message, ToolTipIcon.Warning);
                SetHighResolutionTimer(false);
                _timer.Stop();
            }
        }

        private void SetHighResolutionTimer(bool active)
        {
            if (_highResolutionTimerActive == active)
            {
                return;
            }

            _highResolutionTimerActive = active;
            if (active)
            {
                if (NativeMethods.timeBeginPeriod(1) != 0)
                {
                    _highResolutionTimerActive = false;
                }
            }
            else
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        private sealed class TaskbarMessageWindow : NativeWindow, IDisposable
        {
            private readonly int _taskbarCreatedMessage;
            public event EventHandler TaskbarCreated;
            public event EventHandler TaskbarChanged;

            public TaskbarMessageWindow()
            {
                _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == _taskbarCreatedMessage && TaskbarCreated != null)
                {
                    TaskbarCreated(this, EventArgs.Empty);
                }
                else if ((m.Msg == NativeMethods.WmDisplayChange ||
                    m.Msg == NativeMethods.WmSettingChange ||
                    m.Msg == NativeMethods.WmThemeChanged ||
                    m.Msg == NativeMethods.WmDpiChanged) && TaskbarChanged != null)
                {
                    TaskbarChanged(this, EventArgs.Empty);
                }

                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }
    }
}
