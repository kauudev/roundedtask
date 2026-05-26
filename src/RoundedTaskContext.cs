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

        public RoundedTaskContext(EventWaitHandle showSettingsSignal, bool showSettingsOnStart)
        {
            _showSettingsSignal = showSettingsSignal;
            _settings = AppSettings.Load();
            _styler = new TaskbarStyler(_settings);
            _messageWindow = new TaskbarMessageWindow();
            _messageWindow.TaskbarCreated += delegate { ApplyNow(true); };

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = LoadTrayIcon();
            _notifyIcon.Text = "RoundedTask";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = BuildMenu();
            _notifyIcon.DoubleClick += delegate { ShowSettings(); };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = _settings.PollIntervalMs;
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
            _timer.Interval = _settings.PollIntervalMs;
            ApplyNow(true);
        }

        private void ApplyNow(bool force)
        {
            try
            {
                int count = _styler.Apply(force);
                _notifyIcon.Text = count > 0 ? "RoundedTask - ativo" : "RoundedTask";
            }
            catch (Exception ex)
            {
                _notifyIcon.Text = "RoundedTask - pausado";
                _notifyIcon.ShowBalloonTip(3000, "RoundedTask", ex.Message, ToolTipIcon.Warning);
                _timer.Stop();
            }
        }

        private sealed class TaskbarMessageWindow : NativeWindow, IDisposable
        {
            private readonly int _taskbarCreatedMessage;
            public event EventHandler TaskbarCreated;

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

                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }
    }
}
