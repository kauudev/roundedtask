using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace RoundedTask
{
    internal sealed class SettingsSavedEventArgs : EventArgs
    {
        public readonly AppSettings Settings;

        public SettingsSavedEventArgs(AppSettings settings)
        {
            Settings = settings;
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _secondary;
        private readonly CheckBox _restoreOnExit;
        private readonly CheckBox _startup;
        private readonly CheckBox _dpiScaling;
        private readonly CheckBox _showTraySegment;
        private readonly CheckBox _showTrayOnHover;
        private readonly CheckBox _showAppsOnHover;
        private readonly CheckBox _fillMaximized;
        private readonly CheckBox _fillTaskSwitcher;
        private readonly CheckBox _translucentTb;
        private readonly ComboBox _preset;
        private readonly ComboBox _layout;
        private readonly ModernNumberBox _radius;
        private readonly ModernNumberBox _left;
        private readonly ModernNumberBox _top;
        private readonly ModernNumberBox _right;
        private readonly ModernNumberBox _bottom;
        private readonly ModernNumberBox _centerWidth;
        private readonly ModernNumberBox _centerOffset;
        private readonly ModernNumberBox _systemWidth;
        private readonly ModernNumberBox _systemRightInset;
        private readonly Timer _liveApplyTimer;
        private bool _loadingPreset;

        public event EventHandler<SettingsSavedEventArgs> SettingsSaved;
        public event EventHandler RestoreRequested;

        public SettingsForm(AppSettings settings)
        {
            Text = "RoundedTask - Configura\u00e7\u00f5es";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 930);
            Font = new Font("Segoe UI", 9F);
            BackColor = Palette.Page;
            Icon = LoadAppIcon();

            _liveApplyTimer = new Timer();
            _liveApplyTimer.Interval = 120;
            _liveApplyTimer.Tick += delegate
            {
                _liveApplyTimer.Stop();
                SaveAndApply();
            };

            HeaderPanel header = new HeaderPanel();
            header.Location = new Point(18, 16);
            header.Size = new Size(524, 92);
            Controls.Add(header);

            PictureBox logo = new PictureBox();
            logo.BackColor = Color.Transparent;
            logo.Location = new Point(24, 18);
            logo.Size = new Size(58, 58);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Image = LoadLogo();
            header.Controls.Add(logo);

            Label title = new Label();
            title.Text = "RoundedTask";
            title.Font = new Font("Segoe UI Semibold", 20F);
            title.ForeColor = Color.White;
            title.BackColor = Color.Transparent;
            title.Location = new Point(96, 17);
            title.Size = new Size(280, 36);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Configura\u00e7\u00f5es da barra de tarefas";
            subtitle.ForeColor = Color.FromArgb(225, 232, 245);
            subtitle.BackColor = Color.Transparent;
            subtitle.Location = new Point(98, 57);
            subtitle.Size = new Size(360, 20);
            header.Controls.Add(subtitle);

            Panel mainPanel = MakeCard(18, 124, 524, 430);
            Controls.Add(mainPanel);

            mainPanel.Controls.Add(MakeSection("Geral", 22, 18));
            _enabled = MakeCheck("Ativar arredondamento", settings.Enabled, 22, 50);
            _secondary = MakeCheck("Aplicar em outros monitores", settings.ApplyToSecondaryTaskbars, 22, 80);
            _restoreOnExit = MakeCheck("Restaurar ao sair", settings.RestoreOnExit, 22, 110);
            _startup = MakeCheck("Iniciar com o Windows", StartupManager.IsEnabled(), 22, 140);
            mainPanel.Controls.AddRange(new Control[] { _enabled, _secondary, _restoreOnExit, _startup });

            mainPanel.Controls.Add(MakeSection("Formato", 22, 178));
            mainPanel.Controls.Add(MakeLabel("Predefini\u00e7\u00e3o", 22, 212));
            _preset = MakeCombo(190, 208, 260);
            _preset.Items.AddRange(new object[] { "Balanceado", "Compacto", "Flutuante", "P\u00edlula completa", "Centro e sistema", "Segmentos autom\u00e1ticos", "Personalizado" });
            _preset.SelectedItem = AppSettings.NormalizePreset(settings.Preset);
            if (_preset.SelectedIndex < 0)
            {
                _preset.SelectedItem = "Personalizado";
            }
            _preset.SelectedIndexChanged += OnPresetChanged;
            mainPanel.Controls.Add(_preset);

            mainPanel.Controls.Add(MakeLabel("Layout", 22, 252));
            _layout = MakeCombo(190, 248, 260);
            _layout.Items.AddRange(new object[] { "Barra inteira", "Centro + sistema", "Segmentos autom\u00e1ticos" });
            _layout.SelectedItem = ShapeModeToLayoutText(settings.ShapeMode);
            mainPanel.Controls.Add(_layout);

            mainPanel.Controls.Add(MakeLabel("Raio dos cantos", 22, 292));
            _radius = MakeNumber(settings.CornerRadius, 0, 96, 190, 288);
            mainPanel.Controls.Add(_radius);

            mainPanel.Controls.Add(MakeSection("\u00c1reas da barra", 22, 334));
            _centerWidth = AddField(mainPanel, "Largura centro", settings.CenterWidth, 80, 3000, 22, 364);
            _centerOffset = AddField(mainPanel, "Posi\u00e7\u00e3o centro", settings.CenterOffset, -1500, 1500, 148, 364);
            _systemWidth = AddField(mainPanel, "Largura sistema", settings.SystemWidth, 80, 1200, 274, 364);
            _systemRightInset = AddField(mainPanel, "Recuo direito", settings.SystemRightInset, 0, 600, 400, 364);

            Panel marginPanel = MakeCard(18, 568, 524, 122);
            Controls.Add(marginPanel);
            marginPanel.Controls.Add(MakeSection("Margens da barra", 22, 16));
            _top = AddField(marginPanel, "Topo", settings.MarginTop, 0, 256, 22, 48);
            _bottom = AddField(marginPanel, "Baixo", settings.MarginBottom, 0, 256, 148, 48);
            _left = AddField(marginPanel, "Esquerda", settings.MarginLeft, 0, 1500, 274, 48);
            _right = AddField(marginPanel, "Direita", settings.MarginRight, 0, 1500, 400, 48);

            Panel behaviorPanel = MakeCard(18, 704, 524, 162);
            Controls.Add(behaviorPanel);
            behaviorPanel.Controls.Add(MakeSection("Comportamento inteligente", 22, 16));

            _fillMaximized = MakeCheck("Barra inteira com janela maximizada", settings.FillOnMaximized, 22, 42);
            _fillMaximized.Size = new Size(238, 24);
            _fillTaskSwitcher = MakeCheck("Barra inteira no Alt+Tab", settings.FillOnTaskSwitcher, 274, 42);
            _fillTaskSwitcher.Size = new Size(220, 24);
            _showTraySegment = MakeCheck("Mostrar sistema separado", settings.ShowSystemTraySegment, 22, 72);
            _showTraySegment.Size = new Size(220, 24);
            _showTrayOnHover = MakeCheck("Mostrar sistema ao passar o mouse", settings.ShowTrayOnHover, 274, 72);
            _showTrayOnHover.Size = new Size(220, 24);
            _showAppsOnHover = MakeCheck("Mostrar apps ao passar o mouse", settings.ShowAppsOnHover, 22, 102);
            _showAppsOnHover.Size = new Size(238, 24);
            _translucentTb = MakeCheck("Compatibilidade com TranslucentTB", settings.TranslucentTbCompatibility, 22, 132);
            _translucentTb.Size = new Size(238, 24);
            _dpiScaling = MakeCheck("Escala DPI por monitor", settings.UseDpiScaling, 274, 102);
            _dpiScaling.Size = new Size(220, 24);
            behaviorPanel.Controls.AddRange(new Control[] { _fillMaximized, _fillTaskSwitcher, _showTraySegment, _showTrayOnHover, _showAppsOnHover, _translucentTb, _dpiScaling });

            Button restore = MakeButton("Restaurar", 18, 880, ButtonStyle.Secondary);
            restore.Click += delegate
            {
                _liveApplyTimer.Stop();
                _enabled.Checked = false;
                SaveAndApply();

                if (RestoreRequested != null)
                {
                    RestoreRequested(this, EventArgs.Empty);
                }
            };
            Controls.Add(restore);

            Button close = MakeButton("Fechar", 434, 880, ButtonStyle.Primary);
            close.Click += delegate
            {
                _liveApplyTimer.Stop();
                SaveAndApply();
                Hide();
            };
            Controls.Add(close);

            RegisterLiveApplyHandlers();
            UpdateControlStates();
        }

        private void OnPresetChanged(object sender, EventArgs e)
        {
            string selected = Convert.ToString(_preset.SelectedItem);
            if (String.Equals(selected, "Personalizado", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleLiveApply();
                return;
            }

            AppSettings presetSettings = new AppSettings();
            presetSettings.ApplyPreset(selected);

                _loadingPreset = true;
            try
            {
                _radius.Value = presetSettings.CornerRadius;
                _layout.SelectedItem = ShapeModeToLayoutText(presetSettings.ShapeMode);
                _left.Value = presetSettings.MarginLeft;
                _top.Value = presetSettings.MarginTop;
                _right.Value = presetSettings.MarginRight;
                _bottom.Value = presetSettings.MarginBottom;
                _centerWidth.Value = presetSettings.CenterWidth;
                _centerOffset.Value = presetSettings.CenterOffset;
                _systemWidth.Value = presetSettings.SystemWidth;
                _systemRightInset.Value = presetSettings.SystemRightInset;
                _showTraySegment.Checked = presetSettings.ShowSystemTraySegment;
                _showTrayOnHover.Checked = presetSettings.ShowTrayOnHover;
                _showAppsOnHover.Checked = presetSettings.ShowAppsOnHover;
                _fillMaximized.Checked = presetSettings.FillOnMaximized;
                _fillTaskSwitcher.Checked = presetSettings.FillOnTaskSwitcher;
            }
            finally
            {
                _loadingPreset = false;
            }

            UpdateControlStates();
            ScheduleLiveApply();
        }

        private void RegisterLiveApplyHandlers()
        {
            _enabled.CheckedChanged += OnLiveControlChanged;
            _secondary.CheckedChanged += OnLiveControlChanged;
            _restoreOnExit.CheckedChanged += OnLiveControlChanged;
            _startup.CheckedChanged += OnLiveControlChanged;
            _layout.SelectedIndexChanged += OnLiveControlChanged;
            _radius.ValueChanged += OnLiveControlChanged;
            _left.ValueChanged += OnLiveControlChanged;
            _top.ValueChanged += OnLiveControlChanged;
            _right.ValueChanged += OnLiveControlChanged;
            _bottom.ValueChanged += OnLiveControlChanged;
            _centerWidth.ValueChanged += OnLiveControlChanged;
            _centerOffset.ValueChanged += OnLiveControlChanged;
            _systemWidth.ValueChanged += OnLiveControlChanged;
            _systemRightInset.ValueChanged += OnLiveControlChanged;
            _dpiScaling.CheckedChanged += OnLiveControlChanged;
            _showTraySegment.CheckedChanged += OnLiveControlChanged;
            _showTrayOnHover.CheckedChanged += OnLiveControlChanged;
            _showAppsOnHover.CheckedChanged += OnLiveControlChanged;
            _fillMaximized.CheckedChanged += OnLiveControlChanged;
            _fillTaskSwitcher.CheckedChanged += OnLiveControlChanged;
            _translucentTb.CheckedChanged += OnLiveControlChanged;
        }

        private void OnLiveControlChanged(object sender, EventArgs e)
        {
            if (_loadingPreset)
            {
                return;
            }

            if (sender == _layout || sender == _fillMaximized || sender == _showTrayOnHover || sender == _showAppsOnHover)
            {
                UpdateControlStates();
            }

            if (_preset.SelectedItem != null && sender != _preset)
            {
                string preset = Convert.ToString(_preset.SelectedItem);
                if (!String.Equals(preset, "Personalizado", StringComparison.OrdinalIgnoreCase))
                {
                    _preset.SelectedItem = "Personalizado";
                }
            }

            ScheduleLiveApply();
        }

        private void ScheduleLiveApply()
        {
            if (_loadingPreset)
            {
                return;
            }

            _liveApplyTimer.Stop();
            _liveApplyTimer.Start();
        }

        private void SaveAndApply()
        {
            AppSettings settings = new AppSettings();
            settings.Enabled = _enabled.Checked;
            settings.ApplyToSecondaryTaskbars = _secondary.Checked;
            settings.RestoreOnExit = _restoreOnExit.Checked;
            settings.Preset = Convert.ToString(_preset.SelectedItem);
            settings.ShapeMode = LayoutTextToShapeMode(Convert.ToString(_layout.SelectedItem));
            settings.CornerRadius = _radius.Value;
            settings.MarginLeft = _left.Value;
            settings.MarginTop = _top.Value;
            settings.MarginRight = _right.Value;
            settings.MarginBottom = _bottom.Value;
            settings.CenterWidth = _centerWidth.Value;
            settings.CenterOffset = _centerOffset.Value;
            settings.SystemWidth = _systemWidth.Value;
            settings.SystemRightInset = _systemRightInset.Value;
            settings.UseDpiScaling = _dpiScaling.Checked;
            settings.ShowSystemTraySegment = _showTraySegment.Checked;
            settings.ShowTrayOnHover = _showTrayOnHover.Checked;
            settings.ShowAppsOnHover = _showAppsOnHover.Checked;
            settings.FillOnMaximized = _fillMaximized.Checked;
            settings.FillOnTaskSwitcher = _fillTaskSwitcher.Checked;
            settings.TranslucentTbCompatibility = _translucentTb.Checked;

            StartupManager.SetEnabled(_startup.Checked);

            if (SettingsSaved != null)
            {
                SettingsSaved(this, new SettingsSavedEventArgs(settings));
            }
        }

        private void UpdateControlStates()
        {
            string selectedLayout = Convert.ToString(_layout.SelectedItem);
            bool split = String.Equals(selectedLayout, "Centro + sistema", StringComparison.OrdinalIgnoreCase);
            bool dynamicSegments = String.Equals(selectedLayout, "Segmentos autom\u00e1ticos", StringComparison.OrdinalIgnoreCase);

            _centerWidth.Enabled = split;
            _centerOffset.Enabled = split;
            _systemWidth.Enabled = split;
            _systemRightInset.Enabled = split;

            _left.Enabled = !split && !dynamicSegments;
            _right.Enabled = !split && !dynamicSegments;
            _top.Enabled = true;
            _bottom.Enabled = true;

            _showTraySegment.Enabled = dynamicSegments && !_showTrayOnHover.Checked;
            _showTrayOnHover.Enabled = dynamicSegments;
            _showAppsOnHover.Enabled = dynamicSegments;
        }

        private static string ShapeModeToLayoutText(string shapeMode)
        {
            if (String.Equals(shapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase))
            {
                return "Centro + sistema";
            }

            if (String.Equals(shapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase))
            {
                return "Segmentos autom\u00e1ticos";
            }

            return "Barra inteira";
        }

        private static string LayoutTextToShapeMode(string layout)
        {
            if (String.Equals(layout, "Centro + sistema", StringComparison.OrdinalIgnoreCase))
            {
                return "CenterAndSystem";
            }

            if (String.Equals(layout, "Segmentos autom\u00e1ticos", StringComparison.OrdinalIgnoreCase))
            {
                return "DynamicSegments";
            }

            return "FullBar";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _liveApplyTimer.Stop();
                _liveApplyTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _liveApplyTimer.Stop();

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SaveAndApply();
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        private static ModernNumberBox AddField(Control parent, string labelText, int value, int min, int max, int x, int y)
        {
            Label label = MakeFieldLabel(labelText, x, y);
            ModernNumberBox number = MakeNumber(value, min, max, x, y + 22);
            parent.Controls.Add(label);
            parent.Controls.Add(number);
            return number;
        }

        private static Image LoadLogo()
        {
            string path = Path.Combine(Application.StartupPath, "assets", "roundedtask.png");
            if (File.Exists(path))
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (Image image = Image.FromStream(stream))
                    {
                        return new Bitmap(image);
                    }
                }
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

            return SystemIcons.Application;
        }

        private static Panel MakeCard(int x, int y, int width, int height)
        {
            RoundedPanel panel = new RoundedPanel();
            panel.BackColor = Palette.Card;
            panel.BorderColor = Color.FromArgb(224, 234, 247);
            panel.Radius = 18;
            panel.Location = new Point(x, y);
            panel.Size = new Size(width, height);
            return panel;
        }

        private static CheckBox MakeCheck(string text, bool value, int x, int y)
        {
            CheckBox check = new ModernCheckBox();
            check.Text = text;
            check.Checked = value;
            check.Location = new Point(x, y);
            check.Size = new Size(420, 24);
            check.ForeColor = Palette.Text;
            return check;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.BackColor = Color.Transparent;
            label.ForeColor = Palette.MutedText;
            label.Location = new Point(x, y);
            label.Size = new Size(150, 20);
            return label;
        }

        private static Label MakeFieldLabel(string text, int x, int y)
        {
            Label label = MakeLabel(text, x, y);
            label.Size = new Size(104, 20);
            label.AutoEllipsis = true;
            return label;
        }

        private static Label MakeSection(string text, int x, int y)
        {
            Label label = MakeLabel(text, x, y);
            label.Font = new Font("Segoe UI Semibold", 9F);
            label.ForeColor = Palette.Text;
            return label;
        }

        private static ComboBox MakeCombo(int x, int y, int width)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Palette.Input;
            combo.ForeColor = Palette.Text;
            combo.Location = new Point(x, y);
            combo.Width = width;
            return combo;
        }

        private static ModernNumberBox MakeNumber(int value, int min, int max, int x, int y)
        {
            ModernNumberBox number = new ModernNumberBox();
            number.Minimum = min;
            number.Maximum = max;
            number.Value = value;
            number.Location = new Point(x, y);
            number.Size = new Size(88, 28);
            return number;
        }

        private static Button MakeButton(string text, int x, int y, ButtonStyle style)
        {
            Button button = new ModernButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(108, 34);
            button.BackColor = style == ButtonStyle.Primary ? Palette.Blue : Palette.Input;
            button.ForeColor = style == ButtonStyle.Primary ? Color.White : Palette.Text;
            return button;
        }

        private enum ButtonStyle
        {
            Primary,
            Secondary
        }

        private static class Palette
        {
            public static readonly Color Page = Color.FromArgb(245, 247, 250);
            public static readonly Color Card = Color.White;
            public static readonly Color Input = Color.FromArgb(234, 243, 255);
            public static readonly Color Text = Color.FromArgb(20, 34, 54);
            public static readonly Color MutedText = Color.FromArgb(72, 89, 112);
            public static readonly Color Blue = Color.FromArgb(47, 132, 255);
            public static readonly Color StrongBlue = Color.FromArgb(10, 94, 255);
            public static readonly Color LightBlue = Color.FromArgb(88, 175, 255);
        }

        private sealed class RoundedPanel : Panel
        {
            public int Radius = 16;
            public Color BorderColor = Color.FromArgb(224, 234, 247);

            public RoundedPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                if (Width <= 0 || Height <= 0)
                {
                    return;
                }

                Region oldRegion = Region;
                using (GraphicsPath path = CreateRoundRect(new Rectangle(0, 0, Width, Height), Radius))
                {
                    Region = new Region(path);
                }

                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (Parent != null)
                {
                    e.Graphics.Clear(Parent.BackColor);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = CreateRoundRect(rect, Radius))
                {
                    using (SolidBrush brush = new SolidBrush(BackColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(BorderColor))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }

            }
        }

        private sealed class ModernButton : Button
        {
            private bool _hot;
            private bool _pressed;

            public ModernButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Palette.Blue;
                ForeColor = Color.White;
                Font = new Font("Segoe UI Semibold", 9F);
                Cursor = Cursors.Hand;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hot = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hot = false;
                _pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                base.OnMouseDown(mevent);
                if (mevent.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                base.OnMouseUp(mevent);
                _pressed = false;
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Region oldRegion = Region;
                Region = null;
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                if (Parent != null)
                {
                    pevent.Graphics.Clear(Parent.BackColor);
                }
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Page);
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

                bool primary = BackColor == Palette.Blue;
                Rectangle fillRect = _pressed ? new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 1, rect.Height - 1) : rect;
                using (GraphicsPath path = CreateRoundRect(fillRect, 13))
                {
                    if (primary)
                    {
                        Color top = _hot ? Palette.LightBlue : Palette.Blue;
                        Color bottom = _hot ? Palette.Blue : Palette.StrongBlue;
                        if (_pressed)
                        {
                            top = Palette.StrongBlue;
                            bottom = Palette.Blue;
                        }

                        using (LinearGradientBrush brush = new LinearGradientBrush(fillRect, top, bottom, LinearGradientMode.Vertical))
                        {
                            pevent.Graphics.FillPath(brush, path);
                        }

                        using (Pen shine = new Pen(Color.FromArgb(90, Color.White)))
                        {
                            pevent.Graphics.DrawPath(shine, path);
                        }
                    }
                    else
                    {
                        Color fill = _hot ? Color.White : Palette.Input;
                        if (_pressed)
                        {
                            fill = Color.FromArgb(220, 235, 252);
                        }

                        using (SolidBrush brush = new SolidBrush(fill))
                        using (Pen pen = new Pen(_hot ? Palette.LightBlue : Color.FromArgb(195, 216, 242)))
                        {
                            pevent.Graphics.FillPath(brush, path);
                            pevent.Graphics.DrawPath(pen, path);
                        }
                    }
                }

                TextRenderer.DrawText(
                    pevent.Graphics,
                    Text,
                    Font,
                    rect,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private sealed class ModernCheckBox : CheckBox
        {
            private bool _hot;
            private bool _pressed;

            public ModernCheckBox()
            {
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                Font = new Font("Segoe UI", 9F);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs eventargs)
            {
                base.OnMouseEnter(eventargs);
                _hot = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs eventargs)
            {
                base.OnMouseLeave(eventargs);
                _hot = false;
                _pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                base.OnMouseDown(mevent);
                if (mevent.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                base.OnMouseUp(mevent);
                _pressed = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Card);

                Rectangle box = new Rectangle(0, 2, 20, 20);
                Color borderColor = Checked ? Palette.Blue : (_hot ? Palette.LightBlue : Color.FromArgb(190, 210, 235));
                Color uncheckedFill = _hot ? Color.White : Palette.Input;
                if (_pressed)
                {
                    uncheckedFill = Color.FromArgb(220, 235, 252);
                }

                using (GraphicsPath path = CreateRoundRect(box, 7))
                {
                    if (Checked)
                    {
                        Color top = _hot ? Palette.LightBlue : Palette.Blue;
                        Color bottom = _pressed ? Palette.Blue : Palette.StrongBlue;
                        using (LinearGradientBrush fill = new LinearGradientBrush(box, top, bottom, LinearGradientMode.Vertical))
                        {
                            e.Graphics.FillPath(fill, path);
                        }
                    }
                    else
                    {
                        using (SolidBrush fill = new SolidBrush(uncheckedFill))
                        {
                            e.Graphics.FillPath(fill, path);
                        }
                    }

                    using (Pen border = new Pen(borderColor))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }

                if (Checked)
                {
                    using (Pen pen = new Pen(Color.White, 2.4F))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        e.Graphics.DrawLines(pen, new Point[] {
                            new Point(5, 12),
                            new Point(9, 16),
                            new Point(16, 7)
                        });
                    }
                }

                Color textColor = Enabled ? ForeColor : Color.FromArgb(145, 158, 176);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(30, 0, Width - 30, Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }
        }

        private sealed class ModernNumberBox : Control
        {
            private readonly TextBox _editor;
            private int _minimum;
            private int _maximum = 100;
            private int _value;
            private bool _minusHot;
            private bool _plusHot;
            private bool _syncingText;

            public event EventHandler ValueChanged;

            public int Minimum
            {
                get { return _minimum; }
                set
                {
                    _minimum = value;
                    if (_maximum < _minimum)
                    {
                        _maximum = _minimum;
                    }

                    Value = _value;
                }
            }

            public int Maximum
            {
                get { return _maximum; }
                set
                {
                    _maximum = value;
                    if (_minimum > _maximum)
                    {
                        _minimum = _maximum;
                    }

                    Value = _value;
                }
            }

            public int Value
            {
                get { return _value; }
                set
                {
                    int next = Math.Max(_minimum, Math.Min(_maximum, value));
                    if (_value == next)
                    {
                        SyncEditorText(false);
                        return;
                    }

                    _value = next;
                    SyncEditorText(false);
                    Invalidate();

                    if (ValueChanged != null)
                    {
                        ValueChanged(this, EventArgs.Empty);
                    }
                }
            }

            public ModernNumberBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                BackColor = Palette.Input;
                ForeColor = Palette.Text;
                Font = new Font("Segoe UI Semibold", 9F);
                Cursor = Cursors.Hand;
                TabStop = true;

                _editor = new TextBox();
                _editor.BorderStyle = BorderStyle.None;
                _editor.TextAlign = HorizontalAlignment.Center;
                _editor.BackColor = Palette.Input;
                _editor.ForeColor = Palette.Text;
                _editor.Font = Font;
                _editor.ShortcutsEnabled = true;
                _editor.Location = new Point(27, 6);
                _editor.Size = new Size(34, 18);
                _editor.TextChanged += OnEditorTextChanged;
                _editor.KeyDown += OnEditorKeyDown;
                _editor.Leave += delegate { SyncEditorText(true); };
                Controls.Add(_editor);

                SyncEditorText(false);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                _editor.Enabled = Enabled;
                _editor.BackColor = Enabled ? Palette.Input : Color.FromArgb(242, 246, 251);
                _editor.ForeColor = Enabled ? Palette.Text : Color.FromArgb(145, 158, 176);
                Invalidate();
            }

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                _editor.Focus();
                _editor.SelectAll();
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _minusHot = false;
                _plusHot = false;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool minus = MinusRect.Contains(e.Location);
                bool plus = PlusRect.Contains(e.Location);
                if (_minusHot != minus || _plusHot != plus)
                {
                    _minusHot = minus;
                    _plusHot = plus;
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                if (!Enabled || e.Button != MouseButtons.Left)
                {
                    return;
                }

                if (MinusRect.Contains(e.Location))
                {
                    Value -= 1;
                    return;
                }

                if (PlusRect.Contains(e.Location))
                {
                    Value += 1;
                    return;
                }

                _editor.Focus();
                _editor.SelectAll();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!Enabled)
                {
                    return;
                }

                Value += e.Delta > 0 ? 1 : -1;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (!Enabled)
                {
                    return;
                }

                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right || e.KeyCode == Keys.Add)
                {
                    Value += 1;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Subtract)
                {
                    Value -= 1;
                    e.Handled = true;
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutEditor();
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                if (_editor != null)
                {
                    _editor.Font = Font;
                    LayoutEditor();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Card);

                Color fill = Enabled ? Palette.Input : Color.FromArgb(242, 246, 251);
                Color borderColor = Focused ? Palette.Blue : Color.FromArgb(195, 216, 242);
                Color textColor = Enabled ? Palette.Text : Color.FromArgb(145, 158, 176);
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

                using (GraphicsPath path = CreateRoundRect(rect, 10))
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen pen = new Pen(borderColor))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                DrawStepButton(e.Graphics, MinusRect, "-", _minusHot && Enabled, Enabled);
                DrawStepButton(e.Graphics, PlusRect, "+", _plusHot && Enabled, Enabled);
            }

            private void OnEditorTextChanged(object sender, EventArgs e)
            {
                if (_syncingText)
                {
                    return;
                }

                string text = _editor.Text.Trim();
                if (text.Length == 0 || text == "-")
                {
                    return;
                }

                int parsed;
                if (!Int32.TryParse(text, out parsed))
                {
                    return;
                }

                int next = Math.Max(_minimum, Math.Min(_maximum, parsed));
                if (_value == next)
                {
                    return;
                }

                _value = next;
                Invalidate();

                if (ValueChanged != null)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            }

            private void OnEditorKeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SyncEditorText(true);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode == Keys.Up)
                {
                    Value += 1;
                    _editor.SelectAll();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode == Keys.Down)
                {
                    Value -= 1;
                    _editor.SelectAll();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            }

            private void SyncEditorText(bool clampExistingText)
            {
                if (_editor == null)
                {
                    return;
                }

                int value = _value;
                if (clampExistingText)
                {
                    int parsed;
                    if (Int32.TryParse(_editor.Text.Trim(), out parsed))
                    {
                        value = Math.Max(_minimum, Math.Min(_maximum, parsed));
                        _value = value;
                    }
                }
                string next = value.ToString();
                if (String.Equals(_editor.Text, next, StringComparison.Ordinal))
                {
                    return;
                }

                _syncingText = true;
                try
                {
                    int selectionStart = _editor.SelectionStart;
                    _editor.Text = next;
                    _editor.SelectionStart = Math.Min(selectionStart, _editor.Text.Length);
                }
                finally
                {
                    _syncingText = false;
                }
            }

            private void LayoutEditor()
            {
                if (_editor == null)
                {
                    return;
                }

                int left = 27;
                int right = 27;
                _editor.Location = new Point(left, Math.Max(4, (Height - _editor.Height) / 2));
                _editor.Size = new Size(Math.Max(24, Width - left - right), _editor.Height);
            }

            private void DrawStepButton(Graphics graphics, Rectangle rect, string text, bool hot, bool enabled)
            {
                Color textColor = enabled ? (hot ? Palette.StrongBlue : Palette.Blue) : Color.FromArgb(160, 172, 188);
                using (SolidBrush brush = new SolidBrush(hot ? Color.White : Color.Transparent))
                using (GraphicsPath path = CreateRoundRect(rect, 7))
                {
                    if (hot)
                    {
                        graphics.FillPath(brush, path);
                    }
                }

                TextRenderer.DrawText(
                    graphics,
                    text,
                    Font,
                    rect,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            private Rectangle MinusRect
            {
                get { return new Rectangle(4, 4, 20, Height - 8); }
            }

            private Rectangle PlusRect
            {
                get { return new Rectangle(Width - 24, 4, 20, Height - 8); }
            }
        }

        private sealed class HeaderPanel : Panel
        {
            public HeaderPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                if (Width <= 0 || Height <= 0)
                {
                    return;
                }

                Region oldRegion = Region;
                using (GraphicsPath path = CreateRoundRect(new Rectangle(0, 0, Width, Height), 24))
                {
                    Region = new Region(path);
                }

                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (Parent != null)
                {
                    e.Graphics.Clear(Parent.BackColor);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (LinearGradientBrush brush = new LinearGradientBrush(
                    ClientRectangle,
                    Palette.StrongBlue,
                    Palette.LightBlue,
                    LinearGradientMode.Horizontal))
                {
                    using (GraphicsPath path = CreateRoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 24))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
            }
        }

        private static GraphicsPath CreateRoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
