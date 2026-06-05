using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RoundedTask
{
    internal sealed class UpdatePromptForm : Form
    {
        private readonly UpdateCheckResult _update;
        private readonly Label _statusLabel;
        private readonly Label _progressLabel;
        private readonly AnimatedProgressBar _progressBar;
        private readonly ModernActionButton _updateButton;
        private readonly ModernActionButton _laterButton;
        private readonly ModernActionButton _releaseButton;
        private bool _updating;
        private bool _installerStarted;

        public UpdatePromptForm(UpdateCheckResult update)
        {
            _update = update;

            Text = "RoundedTask - Atualizacao";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(560, 392);
            Font = new Font("Segoe UI", 9F);
            BackColor = Palette.Page;
            Icon = LoadAppIcon();

            HeaderPanel header = new HeaderPanel();
            header.Location = new Point(18, 18);
            header.Size = new Size(524, 128);
            Controls.Add(header);

            PictureBox logo = new PictureBox();
            logo.BackColor = Color.Transparent;
            logo.Location = new Point(22, 25);
            logo.Size = new Size(72, 72);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Image = LoadLogo();
            header.Controls.Add(logo);

            Label eyebrow = MakeLabel("ROUNDEDTASK UPDATE", 114, 22, 240, 20, Color.FromArgb(215, 232, 255), 8F, true);
            header.Controls.Add(eyebrow);

            Label title = MakeLabel("Nova atualiza\u00e7\u00e3o dispon\u00edvel", 112, 42, 360, 38, Color.White, 19F, true);
            header.Controls.Add(title);

            Label subtitle = MakeLabel("Vers\u00e3o " + SafeText(update.LatestTag) + " pronta para instalar", 114, 82, 340, 22, Color.FromArgb(230, 240, 255), 9.2F, false);
            header.Controls.Add(subtitle);

            Label versionLabel = MakeLabel(
                "Atual: " + FormatVersion(update.CurrentVersion) + "     Nova: " + SafeText(update.LatestTag),
                30,
                170,
                500,
                24,
                Palette.Text,
                10F,
                true);
            Controls.Add(versionLabel);

            Label releaseLabel = MakeLabel(SafeText(update.ReleaseName), 30, 198, 500, 24, Palette.MutedText, 9F, false);
            releaseLabel.AutoEllipsis = true;
            Controls.Add(releaseLabel);

            Label assetLabel = MakeLabel(
                "Arquivo: " + SafeText(update.AssetName) + "  " + FormatAssetSize(update.AssetSize),
                30,
                224,
                500,
                22,
                Palette.MutedText,
                8.6F,
                false);
            assetLabel.AutoEllipsis = true;
            Controls.Add(assetLabel);

            _statusLabel = MakeLabel(
                "O RoundedTask baixa o zip da release e aplica tudo sozinho.",
                30,
                256,
                500,
                26,
                Palette.Text,
                9.2F,
                false);
            Controls.Add(_statusLabel);

            _progressBar = new AnimatedProgressBar();
            _progressBar.Location = new Point(30, 290);
            _progressBar.Size = new Size(396, 18);
            _progressBar.Visible = false;
            Controls.Add(_progressBar);

            _progressLabel = MakeLabel("", 438, 286, 90, 26, Palette.Blue, 10F, true);
            _progressLabel.TextAlign = ContentAlignment.MiddleRight;
            _progressLabel.Visible = false;
            Controls.Add(_progressLabel);

            _releaseButton = MakeButton("Abrir release", 30, 334, 132, false);
            _releaseButton.Click += delegate { OpenRelease(); };
            Controls.Add(_releaseButton);

            _laterButton = MakeButton("Depois", 300, 334, 98, false);
            _laterButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(_laterButton);

            _updateButton = MakeButton("Atualizar agora", 410, 334, 120, true);
            _updateButton.Click += delegate { BeginUpdate(); };
            Controls.Add(_updateButton);

            AcceptButton = _updateButton;
            CancelButton = _laterButton;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_updating && !_installerStarted)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private void BeginUpdate()
        {
            if (_updating)
            {
                return;
            }

            _updating = true;
            _updateButton.Enabled = false;
            _laterButton.Enabled = false;
            _releaseButton.Enabled = false;
            _statusLabel.Text = "Conectando ao GitHub...";
            _progressBar.ProgressValue = 0;
            _progressBar.Visible = true;
            _progressBar.Start();
            _progressLabel.Text = "0%";
            _progressLabel.Visible = true;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    UpdateManager.DownloadAndStartInstaller(_update, OnDownloadProgress);
                    InvokeOnUi(delegate { CompleteUpdateStart(); });
                }
                catch (Exception ex)
                {
                    InvokeOnUi(delegate { ShowUpdateError(ex.Message); });
                }
            });
        }

        private void OnDownloadProgress(int percent, long bytesReceived, long totalBytes)
        {
            InvokeOnUi(delegate
            {
                if (percent >= 0)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.ProgressValue = percent;
                    _progressLabel.Text = percent.ToString() + "%";
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                    _progressLabel.Text = FormatBytes(bytesReceived);
                }

                _statusLabel.Text = totalBytes > 0
                    ? "Baixando update: " + FormatBytes(bytesReceived) + " de " + FormatBytes(totalBytes)
                    : "Baixando update: " + FormatBytes(bytesReceived);
            });
        }

        private void CompleteUpdateStart()
        {
            _installerStarted = true;
            _progressBar.IsIndeterminate = false;
            _progressBar.ProgressValue = 100;
            _progressLabel.Text = "100%";
            _statusLabel.Text = "Download pronto. O RoundedTask vai reiniciar para aplicar.";

            System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 800;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                DialogResult = DialogResult.OK;
                Close();
            };
            closeTimer.Start();
        }

        private void ShowUpdateError(string message)
        {
            _updating = false;
            _installerStarted = false;
            _progressBar.Stop();
            _progressBar.Visible = false;
            _progressLabel.Visible = false;
            _updateButton.Enabled = true;
            _laterButton.Enabled = true;
            _releaseButton.Enabled = true;
            _statusLabel.Text = "Nao foi possivel atualizar agora: " + message;
        }

        private void OpenRelease()
        {
            if (String.IsNullOrEmpty(_update.ReleaseUrl))
            {
                return;
            }

            try
            {
                Process.Start(_update.ReleaseUrl);
            }
            catch
            {
            }
        }

        private void InvokeOnUi(MethodInvoker action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                action();
            }
        }

        private static ModernActionButton MakeButton(string text, int x, int y, int width, bool primary)
        {
            ModernActionButton button = new ModernActionButton();
            button.Text = text;
            button.Primary = primary;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 38);
            return button;
        }

        private static Label MakeLabel(string text, int x, int y, int width, int height, Color color, float size, bool strong)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size);
            return label;
        }

        private static string SafeText(string value)
        {
            return String.IsNullOrEmpty(value) ? "-" : value;
        }

        private static string FormatVersion(Version version)
        {
            if (version == null)
            {
                return "0.0.0";
            }

            return version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
        }

        private static string FormatAssetSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "";
            }

            return "(" + FormatBytes(bytes) + ")";
        }

        private static string FormatBytes(long bytes)
        {
            double value = bytes;
            if (value >= 1024D * 1024D)
            {
                return String.Format("{0:0.0} MB", value / (1024D * 1024D));
            }

            if (value >= 1024D)
            {
                return String.Format("{0:0.0} KB", value / 1024D);
            }

            return bytes.ToString() + " B";
        }

        private static Image LoadLogo()
        {
            string path = Path.Combine(Application.StartupPath, "assets", "roundedtask.png");
            if (File.Exists(path))
            {
                return Image.FromFile(path);
            }

            return null;
        }

        private static Icon LoadAppIcon()
        {
            string path = Path.Combine(Application.StartupPath, "assets", "roundedtask.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }

            Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            return extracted != null ? extracted : SystemIcons.Application;
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static class Palette
        {
            public static readonly Color Page = Color.FromArgb(245, 247, 250);
            public static readonly Color Text = Color.FromArgb(20, 34, 54);
            public static readonly Color MutedText = Color.FromArgb(72, 89, 112);
            public static readonly Color Blue = Color.FromArgb(47, 132, 255);
            public static readonly Color StrongBlue = Color.FromArgb(10, 94, 255);
            public static readonly Color LightBlue = Color.FromArgb(88, 175, 255);
            public static readonly Color Input = Color.FromArgb(234, 243, 255);
            public static readonly Color Border = Color.FromArgb(202, 219, 242);
        }

        private sealed class HeaderPanel : Panel
        {
            public HeaderPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

                using (GraphicsPath path = RoundedRect(rect, 16))
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, Palette.LightBlue, Palette.StrongBlue, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(brush, path);
                }

                using (GraphicsPath path = RoundedRect(rect, 16))
                using (Pen pen = new Pen(Color.FromArgb(90, Color.White)))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private sealed class AnimatedProgressBar : Control
        {
            private readonly System.Windows.Forms.Timer _timer;
            private int _progressValue;
            private int _offset;
            private bool _running;

            public AnimatedProgressBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 28;
                _timer.Tick += delegate
                {
                    _offset = (_offset + 7) % Math.Max(1, Width + 80);
                    Invalidate();
                };
            }

            public bool IsIndeterminate { get; set; }

            public int ProgressValue
            {
                get { return _progressValue; }
                set
                {
                    _progressValue = Math.Max(0, Math.Min(100, value));
                    Invalidate();
                }
            }

            public void Start()
            {
                _running = true;
                _timer.Start();
            }

            public void Stop()
            {
                _running = false;
                _timer.Stop();
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

                using (GraphicsPath trackPath = RoundedRect(rect, Height / 2))
                using (SolidBrush track = new SolidBrush(Color.FromArgb(222, 235, 252)))
                using (Pen border = new Pen(Palette.Border))
                {
                    e.Graphics.FillPath(track, trackPath);
                    e.Graphics.DrawPath(border, trackPath);
                }

                Rectangle fillRect;
                if (IsIndeterminate)
                {
                    int fillWidth = Math.Max(54, Width / 3);
                    int x = _offset - fillWidth;
                    fillRect = new Rectangle(x, 0, fillWidth, Height - 1);
                }
                else
                {
                    int fillWidth = (int)Math.Round((Width - 1) * (_progressValue / 100D));
                    fillRect = new Rectangle(0, 0, Math.Max(0, fillWidth), Height - 1);
                }

                if (fillRect.Width > 0)
                {
                    using (GraphicsPath clip = RoundedRect(rect, Height / 2))
                    {
                        Region oldClip = e.Graphics.Clip;
                        e.Graphics.SetClip(clip);

                        using (LinearGradientBrush fill = new LinearGradientBrush(rect, Palette.LightBlue, Palette.StrongBlue, LinearGradientMode.Horizontal))
                        {
                            e.Graphics.FillRectangle(fill, fillRect);
                        }

                        if (_running)
                        {
                            int shineX = IsIndeterminate ? fillRect.Left + (fillRect.Width / 2) : _offset - 36;
                            using (LinearGradientBrush shine = new LinearGradientBrush(
                                new Rectangle(shineX, 0, 52, Math.Max(1, Height)),
                                Color.FromArgb(0, Color.White),
                                Color.FromArgb(130, Color.White),
                                LinearGradientMode.Horizontal))
                            {
                                e.Graphics.FillRectangle(shine, new Rectangle(shineX, 0, 52, Height));
                            }
                        }

                        e.Graphics.Clip = oldClip;
                    }
                }
            }
        }

        private sealed class ModernActionButton : Button
        {
            private bool _hot;
            private bool _pressed;

            public bool Primary { get; set; }

            public ModernActionButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Font = new Font("Segoe UI Semibold", 9F);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hot = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hot = false;
                _pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                if (mevent.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Invalidate();
                }

                base.OnMouseDown(mevent);
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                _pressed = false;
                Invalidate();
                base.OnMouseUp(mevent);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                Invalidate();
                base.OnEnabledChanged(e);
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Page);

                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundedRect(rect, 10))
                {
                    if (Primary)
                    {
                        Color top = Enabled ? (_hot ? Palette.LightBlue : Palette.Blue) : Color.FromArgb(170, 185, 205);
                        Color bottom = Enabled ? (_pressed ? Palette.Blue : Palette.StrongBlue) : Color.FromArgb(145, 160, 180);
                        using (LinearGradientBrush brush = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
                        {
                            pevent.Graphics.FillPath(brush, path);
                        }
                    }
                    else
                    {
                        Color fill = Enabled ? (_hot ? Color.White : Palette.Input) : Color.FromArgb(236, 241, 247);
                        using (SolidBrush brush = new SolidBrush(fill))
                        {
                            pevent.Graphics.FillPath(brush, path);
                        }

                        using (Pen pen = new Pen(Enabled ? (_hot ? Palette.LightBlue : Palette.Border) : Color.FromArgb(215, 224, 236)))
                        {
                            pevent.Graphics.DrawPath(pen, path);
                        }
                    }
                }

                Color textColor = Enabled ? (Primary ? Color.White : Palette.Text) : Color.FromArgb(145, 158, 176);
                TextRenderer.DrawText(
                    pevent.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
