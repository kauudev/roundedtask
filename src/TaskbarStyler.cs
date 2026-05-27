using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RoundedTask
{
    internal sealed class TaskbarStyler
    {
        private readonly Dictionary<IntPtr, string> _applied = new Dictionary<IntPtr, string>();
        private AppSettings _settings;

        public TaskbarStyler(AppSettings settings)
        {
            _settings = settings.Clone();
        }

        public void UpdateSettings(AppSettings settings)
        {
            _settings = settings.Clone();
        }

        public int Apply(bool force)
        {
            if (!_settings.Enabled)
            {
                RestoreAll();
                return 0;
            }

            List<TaskbarInfo> taskbars = TaskbarDiscovery.FindTaskbars(_settings.ApplyToSecondaryTaskbars);
            HashSet<IntPtr> live = new HashSet<IntPtr>();
            int styled = 0;

            for (int i = 0; i < taskbars.Count; i++)
            {
                TaskbarInfo taskbar = taskbars[i];
                live.Add(taskbar.Hwnd);

                if (ApplyToWindow(taskbar, force))
                {
                    styled++;
                }
            }

            List<IntPtr> stale = new List<IntPtr>();
            foreach (IntPtr hwnd in _applied.Keys)
            {
                if (!live.Contains(hwnd) || !NativeMethods.IsWindow(hwnd))
                {
                    stale.Add(hwnd);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                _applied.Remove(stale[i]);
            }

            return styled;
        }

        public void RestoreAll()
        {
            List<TaskbarInfo> taskbars = TaskbarDiscovery.FindTaskbars(true);
            for (int i = 0; i < taskbars.Count; i++)
            {
                RestoreWindow(taskbars[i].Hwnd);
            }

            _applied.Clear();
        }

        private bool ApplyToWindow(TaskbarInfo taskbar, bool force)
        {
            IntPtr hwnd = taskbar.Hwnd;
            NativeMethods.RECT rect = taskbar.Rect;

            if (!NativeMethods.IsWindow(hwnd) || rect.IsEmpty)
            {
                return false;
            }

            int width = rect.Width;
            int height = rect.Height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (ShouldFillTaskbar(taskbar))
            {
                string filledFingerprint = "filled:" + width + "x" + height + ":" + taskbar.Edge + ":" +
                    _settings.FillOnMaximized + ":" + _settings.FillOnTaskSwitcher;

                string previousFilled;
                if (!force && _applied.TryGetValue(hwnd, out previousFilled) &&
                    String.Equals(previousFilled, filledFingerprint, StringComparison.Ordinal))
                {
                    return true;
                }

                RestoreWindow(hwnd);
                _applied[hwnd] = filledFingerprint;
                return true;
            }

            Margins margins = ClampMargins(width, height, taskbar.ScaleFactor);
            int radius = ScaleSetting(_settings.CornerRadius, taskbar.ScaleFactor);
            radius = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));

            List<RegionRect> regions = BuildRegions(taskbar, width, height, margins);
            if (regions.Count == 0)
            {
                RestoreWindow(hwnd);
                _applied.Remove(hwnd);
                return false;
            }

            string fingerprint = BuildFingerprint(taskbar, width, height, margins, radius, regions);

            string previous;
            if (!force && _applied.TryGetValue(hwnd, out previous) && String.Equals(previous, fingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            IntPtr region = CreateRegion(regions, radius);

            if (region == IntPtr.Zero)
            {
                return false;
            }

            if (!NativeMethods.SetWindowRgn(hwnd, region, true))
            {
                NativeMethods.DeleteObject(region);
                return false;
            }

            _applied[hwnd] = fingerprint;
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
            RefreshComposition(hwnd);
            return true;
        }

        private string BuildFingerprint(TaskbarInfo taskbar, int width, int height, Margins margins, int radius, List<RegionRect> regions)
        {
            string fingerprint = width + "x" + height + ":" + taskbar.Edge + ":" + taskbar.ScaleFactor.ToString("0.###") + ":" +
                _settings.ShapeMode + ":" + radius + ":" +
                margins.Left + "," + margins.Top + "," + margins.Right + "," + margins.Bottom + ":" +
                _settings.ShowSystemTraySegment + ":" + _settings.ShowTrayOnHover + ":" +
                _settings.FillOnMaximized + ":" + _settings.FillOnTaskSwitcher + ":" +
                _settings.TranslucentTbCompatibility + ":" +
                RectFingerprint(taskbar.AppListRect) + ":" + RectFingerprint(taskbar.TrayRect);

            for (int i = 0; i < regions.Count; i++)
            {
                fingerprint += ":" + regions[i].Left + "," + regions[i].Top + "," + regions[i].Right + "," + regions[i].Bottom;
            }

            return fingerprint;
        }

        private static string RectFingerprint(NativeMethods.RECT rect)
        {
            return rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom;
        }

        private List<RegionRect> BuildRegions(TaskbarInfo taskbar, int width, int height, Margins margins)
        {
            List<RegionRect> regions = new List<RegionRect>();

            if (String.Equals(_settings.ShapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase))
            {
                regions = BuildDynamicRegions(taskbar, width, height, margins);
                if (regions.Count > 0)
                {
                    return regions;
                }
            }

            if (String.Equals(_settings.ShapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase) && taskbar.IsHorizontal)
            {
                int usableTop = margins.Top;
                int usableBottom = height - margins.Bottom + 1;
                int centerWidth = ScaleSetting(_settings.CenterWidth, taskbar.ScaleFactor);
                int centerOffset = ScaleSetting(_settings.CenterOffset, taskbar.ScaleFactor);
                int systemWidth = ScaleSetting(_settings.SystemWidth, taskbar.ScaleFactor);
                int systemRightInset = ScaleSetting(_settings.SystemRightInset, taskbar.ScaleFactor);

                centerWidth = Clamp(centerWidth, 80, Math.Max(80, width - margins.Left - margins.Right));
                int centerLeft = (width - centerWidth) / 2 + centerOffset;
                systemWidth = Clamp(systemWidth, 80, Math.Max(80, width - 24));
                systemRightInset = Clamp(systemRightInset, 0, Math.Max(0, width - 80));
                int systemRight = width - systemRightInset + 1;

                regions.Add(ClampRegion(new RegionRect(
                    centerLeft,
                    usableTop,
                    centerLeft + centerWidth + 1,
                    usableBottom), width, height));

                regions.Add(ClampRegion(new RegionRect(
                    systemRight - systemWidth,
                    usableTop,
                    systemRight,
                    usableBottom), width, height));
            }
            else
            {
                regions.Add(BuildFullBarRegion(width, height, margins));
            }

            RemoveTinyRegions(regions);
            return regions;
        }

        private List<RegionRect> BuildDynamicRegions(TaskbarInfo taskbar, int width, int height, Margins margins)
        {
            List<RegionRect> regions = new List<RegionRect>();
            if (!taskbar.IsHorizontal)
            {
                return regions;
            }

            RegionRect appRect;
            bool hasApp = TryMakeRelativeRect(taskbar.AppListRect, taskbar.Rect, out appRect) &&
                IsUsefulChildRect(appRect, width, height);

            RegionRect trayRect;
            bool hasTray = TryMakeRelativeRect(taskbar.TrayRect, taskbar.Rect, out trayRect) &&
                IsUsefulChildRect(trayRect, width, height);

            int pad = Math.Max(3, ScaleSetting(10, taskbar.ScaleFactor));
            int minGap = Math.Max(3, ScaleSetting(8, taskbar.ScaleFactor));
            int usableTop = margins.Top;
            int usableBottom = height - margins.Bottom + 1;

            if (hasApp && hasTray && !taskbar.IsWindows11)
            {
                int gap = trayRect.Left - appRect.Right;
                if (gap <= minGap)
                {
                    return regions;
                }
            }

            if (hasApp)
            {
                int left = appRect.Left - pad;
                int right = appRect.Right + pad;
                if (hasTray && trayRect.Left > appRect.Right)
                {
                    right = Math.Min(right, trayRect.Left - minGap);
                }

                regions.Add(ClampRegion(new RegionRect(left, usableTop, right, usableBottom), width, height));
            }

            bool showTray = _settings.ShowTrayOnHover ? IsCursorNear(taskbar.TrayRect, ScaleSetting(24, taskbar.ScaleFactor)) : _settings.ShowSystemTraySegment;
            if (hasTray && showTray)
            {
                int left = trayRect.Left - pad;
                int right = trayRect.Right + pad;
                regions.Add(ClampRegion(new RegionRect(left, usableTop, right, usableBottom), width, height));
            }

            RemoveTinyRegions(regions);
            return regions;
        }

        private RegionRect BuildFullBarRegion(int width, int height, Margins margins)
        {
            return ClampRegion(new RegionRect(
                margins.Left,
                margins.Top,
                width - margins.Right + 1,
                height - margins.Bottom + 1), width, height);
        }

        private static bool TryMakeRelativeRect(NativeMethods.RECT child, NativeMethods.RECT parent, out RegionRect rect)
        {
            rect = new RegionRect();
            if (child.IsEmpty || parent.IsEmpty)
            {
                return false;
            }

            rect = new RegionRect(
                child.Left - parent.Left,
                child.Top - parent.Top,
                child.Right - parent.Left + 1,
                child.Bottom - parent.Top + 1);
            return true;
        }

        private static bool IsUsefulChildRect(RegionRect rect, int width, int height)
        {
            if (rect.Width < 24 || rect.Height < 8)
            {
                return false;
            }

            if (rect.Right <= 0 || rect.Bottom <= 0 || rect.Left >= width || rect.Top >= height)
            {
                return false;
            }

            if (rect.Width >= width - 2 && rect.Height >= height - 2)
            {
                return false;
            }

            return true;
        }

        private static void RemoveTinyRegions(List<RegionRect> regions)
        {
            for (int i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Width < 18 || regions[i].Height < 12)
                {
                    regions.RemoveAt(i);
                }
            }
        }

        private bool ShouldFillTaskbar(TaskbarInfo taskbar)
        {
            if (_settings.FillOnTaskSwitcher && IsTaskSwitcherActive())
            {
                return true;
            }

            if (!_settings.FillOnMaximized)
            {
                return false;
            }

            IntPtr taskbarMonitor = NativeMethods.MonitorFromWindow(taskbar.Hwnd, NativeMethods.MonitorDefaultToNearest);
            if (taskbarMonitor == IntPtr.Zero)
            {
                return false;
            }

            bool hasMaximizedWindow = false;
            NativeMethods.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (hasMaximizedWindow)
                {
                    return false;
                }

                if (hwnd == taskbar.Hwnd || !NativeMethods.IsWindowVisible(hwnd) || IsShellWindow(hwnd) || IsWindowCloaked(hwnd))
                {
                    return true;
                }

                if (NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest) != taskbarMonitor)
                {
                    return true;
                }

                NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
                placement.Length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
                if (NativeMethods.GetWindowPlacement(hwnd, ref placement) && placement.ShowCmd == NativeMethods.SwShowMaximized)
                {
                    hasMaximizedWindow = true;
                    return false;
                }

                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            return hasMaximizedWindow;
        }

        private static bool IsShellWindow(IntPtr hwnd)
        {
            string className = NativeMethods.GetWindowClass(hwnd);
            return String.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal) ||
                String.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal) ||
                String.Equals(className, "Progman", StringComparison.Ordinal) ||
                String.Equals(className, "WorkerW", StringComparison.Ordinal);
        }

        private static bool IsWindowCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked;
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int)) == 0)
                {
                    return cloaked != 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsTaskSwitcherActive()
        {
            NativeMethods.POINT origin = new NativeMethods.POINT();
            origin.X = 0;
            origin.Y = 0;
            IntPtr atOrigin = NativeMethods.WindowFromPoint(origin);
            if (atOrigin != IntPtr.Zero && IsTaskSwitcherClass(NativeMethods.GetWindowClass(atOrigin)))
            {
                return true;
            }

            bool active = false;
            NativeMethods.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (active)
                {
                    return false;
                }

                if (!NativeMethods.IsWindowVisible(hwnd) || IsWindowCloaked(hwnd))
                {
                    return true;
                }

                if (!IsTaskSwitcherClass(NativeMethods.GetWindowClass(hwnd)))
                {
                    return true;
                }

                NativeMethods.RECT rect;
                if (NativeMethods.GetWindowRect(hwnd, out rect) && rect.Width > 300 && rect.Height > 180)
                {
                    active = true;
                    return false;
                }

                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            return active;
        }

        private static bool IsTaskSwitcherClass(string className)
        {
            return String.Equals(className, "XamlExplorerHostIslandWindow", StringComparison.Ordinal) ||
                String.Equals(className, "TaskSwitcherWnd", StringComparison.Ordinal) ||
                String.Equals(className, "MultitaskingViewFrame", StringComparison.Ordinal);
        }

        private static IntPtr CreateRegion(List<RegionRect> regions, int radius)
        {
            if (regions.Count == 1)
            {
                RegionRect single = regions[0];
                return NativeMethods.CreateRoundRectRgn(
                    single.Left,
                    single.Top,
                    single.Right,
                    single.Bottom,
                    radius * 2,
                    radius * 2);
            }

            IntPtr combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
            if (combined == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < regions.Count; i++)
            {
                RegionRect item = regions[i];
                IntPtr child = NativeMethods.CreateRoundRectRgn(
                    item.Left,
                    item.Top,
                    item.Right,
                    item.Bottom,
                    radius * 2,
                    radius * 2);

                if (child == IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(combined);
                    return IntPtr.Zero;
                }

                int result = NativeMethods.CombineRgn(combined, combined, child, NativeMethods.RgnOr);
                NativeMethods.DeleteObject(child);

                if (result == 0)
                {
                    NativeMethods.DeleteObject(combined);
                    return IntPtr.Zero;
                }
            }

            return combined;
        }

        private static RegionRect ClampRegion(RegionRect region, int width, int height)
        {
            int left = Clamp(region.Left, 0, Math.Max(0, width - 1));
            int top = Clamp(region.Top, 0, Math.Max(0, height - 1));
            int right = Clamp(region.Right, left + 1, width + 1);
            int bottom = Clamp(region.Bottom, top + 1, height + 1);
            return new RegionRect(left, top, right, bottom);
        }

        private void RestoreWindow(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                return;
            }

            NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
            RefreshComposition(hwnd);
        }

        private Margins ClampMargins(int width, int height, double scaleFactor)
        {
            int maxHorizontal = Math.Max(0, (width - 24) / 2);
            int maxVertical = Math.Max(0, (height - 16) / 2);

            return new Margins(
                Clamp(ScaleSetting(_settings.MarginLeft, scaleFactor), 0, maxHorizontal),
                Clamp(ScaleSetting(_settings.MarginTop, scaleFactor), 0, maxVertical),
                Clamp(ScaleSetting(_settings.MarginRight, scaleFactor), 0, maxHorizontal),
                Clamp(ScaleSetting(_settings.MarginBottom, scaleFactor), 0, maxVertical));
        }

        private int ScaleSetting(int value, double scaleFactor)
        {
            if (!_settings.UseDpiScaling)
            {
                return value;
            }

            return Convert.ToInt32(Math.Round(value * scaleFactor));
        }

        private static bool IsCursorNear(NativeMethods.RECT rect, int padding)
        {
            if (rect.IsEmpty)
            {
                return false;
            }

            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return false;
            }

            NativeMethods.RECT padded = new NativeMethods.RECT();
            padded.Left = rect.Left - padding;
            padded.Top = rect.Top - padding;
            padded.Right = rect.Right + padding;
            padded.Bottom = rect.Bottom + padding;
            return NativeMethods.PtInRect(ref padded, point);
        }

        private void RefreshComposition(IntPtr taskbarHwnd)
        {
            if (!_settings.TranslucentTbCompatibility)
            {
                return;
            }

            IntPtr worker = NativeMethods.FindWindow("TTB_WorkerWindow", "TTB_WorkerWindow");
            if (worker != IntPtr.Zero)
            {
                int message = NativeMethods.RegisterWindowMessage("TTB_ForceRefreshTaskbar");
                if (message != 0)
                {
                    NativeMethods.SendMessage(worker, message, IntPtr.Zero, taskbarHwnd);
                }
            }

            NativeMethods.SendMessage(taskbarHwnd, NativeMethods.WmDwmCompositionChanged, new IntPtr(1), IntPtr.Zero);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private struct Margins
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;

            public Margins(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        private struct RegionRect
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;

            public int Width
            {
                get { return Right - Left; }
            }

            public int Height
            {
                get { return Bottom - Top; }
            }

            public RegionRect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }
    }
}
