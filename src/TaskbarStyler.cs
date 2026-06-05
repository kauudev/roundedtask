using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoundedTask
{
    internal sealed class TaskbarStyler
    {
        private readonly Dictionary<IntPtr, string> _applied = new Dictionary<IntPtr, string>();
        private readonly Dictionary<IntPtr, DynamicMeasurementState> _dynamicMeasurements = new Dictionary<IntPtr, DynamicMeasurementState>();
        private readonly Dictionary<IntPtr, HoverSegmentState> _systemHoverStates = new Dictionary<IntPtr, HoverSegmentState>();
        private readonly Dictionary<IntPtr, HoverSegmentState> _appHoverStates = new Dictionary<IntPtr, HoverSegmentState>();
        private AppSettings _settings;

        public bool HasActiveHoverAnimation { get; private set; }
        public int ActiveHoverFrameIntervalMs { get; private set; }

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
            HasActiveHoverAnimation = false;
            ActiveHoverFrameIntervalMs = 17;

            if (!_settings.Enabled)
            {
                RestoreAll();
                return 0;
            }

            List<TaskbarInfo> taskbars = TaskbarDiscovery.FindTaskbars(_settings.ApplyToSecondaryTaskbars, NeedsAppButtonMeasurements());
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
                _dynamicMeasurements.Remove(stale[i]);
                _systemHoverStates.Remove(stale[i]);
                _appHoverStates.Remove(stale[i]);
            }

            return styled;
        }

        public void RestoreAll()
        {
            List<TaskbarInfo> taskbars = TaskbarDiscovery.FindTaskbars(true, false);
            for (int i = 0; i < taskbars.Count; i++)
            {
                RestoreWindow(taskbars[i].Hwnd);
            }

            _applied.Clear();
            _dynamicMeasurements.Clear();
            _systemHoverStates.Clear();
            _appHoverStates.Clear();
        }

        private bool NeedsAppButtonMeasurements()
        {
            return String.Equals(_settings.ShapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(_settings.ShapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase);
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

            StabilizeMeasuredRects(taskbar);

            bool allowEmptyRegion;
            List<RegionRect> regions = BuildRegions(taskbar, width, height, margins, out allowEmptyRegion);
            if (regions.Count == 0 && !allowEmptyRegion)
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
                _settings.ShowSystemTraySegment + ":" + _settings.ShowTrayOnHover + ":" + _settings.ShowAppsOnHover + ":" +
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

        private List<RegionRect> BuildRegions(TaskbarInfo taskbar, int width, int height, Margins margins, out bool allowEmptyRegion)
        {
            List<RegionRect> regions = new List<RegionRect>();
            allowEmptyRegion = false;

            if (String.Equals(_settings.ShapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase))
            {
                regions = BuildDynamicRegions(taskbar, width, height, margins, out allowEmptyRegion);
                if (regions.Count > 0 || allowEmptyRegion)
                {
                    return regions;
                }
            }

            if (String.Equals(_settings.ShapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase) && taskbar.IsHorizontal)
            {
                if (!taskbar.IsWindows11 && TryBuildMeasuredSplitRegions(taskbar, width, height, margins, out regions))
                {
                    return regions;
                }

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

        private bool TryBuildMeasuredSplitRegions(TaskbarInfo taskbar, int width, int height, Margins margins, out List<RegionRect> regions)
        {
            regions = new List<RegionRect>();

            RegionRect appRect;
            bool hasApp = TryMakeRelativeRect(taskbar.AppListRect, taskbar.Rect, out appRect) &&
                IsUsefulChildRect(appRect, width, height);

            RegionRect trayRect;
            bool hasTray = TryMakeRelativeRect(taskbar.TrayRect, taskbar.Rect, out trayRect) &&
                IsUsefulChildRect(trayRect, width, height);

            if (!hasApp || !hasTray)
            {
                return false;
            }

            int pad = Math.Max(3, ScaleSetting(10, taskbar.ScaleFactor));
            int minGap = Math.Max(3, ScaleSetting(8, taskbar.ScaleFactor));
            int usableTop = margins.Top;
            int usableBottom = height - margins.Bottom + 1;

            int appLeft = appRect.Left - pad;
            int trayLeft = trayRect.Left - pad;
            int appRight = Math.Min(appRect.Right + pad, trayLeft - minGap);
            regions.Add(ClampRegion(new RegionRect(appLeft, usableTop, appRight, usableBottom), width, height));

            regions.Add(ClampRegion(new RegionRect(
                trayLeft,
                usableTop,
                trayRect.Right + pad,
                usableBottom), width, height));

            RemoveTinyRegions(regions);
            return regions.Count >= 2;
        }

        private List<RegionRect> BuildDynamicRegions(TaskbarInfo taskbar, int width, int height, Margins margins, out bool allowEmptyRegion)
        {
            List<RegionRect> regions = new List<RegionRect>();
            allowEmptyRegion = false;
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

            double appVisibility = GetAppSegmentVisibility(taskbar);
            bool showApp = appVisibility > 0.015;
            if (hasApp && showApp)
            {
                int left = appRect.Left - pad;
                int right = appRect.Right + pad;
                int top = usableTop;
                int bottom = usableBottom;
                if (hasTray)
                {
                    right = Math.Min(right, trayRect.Left - pad - minGap);
                }

                if (_settings.ShowAppsOnHover && appVisibility < 1.0)
                {
                    int fullHeight = Math.Max(1, bottom - top);
                    top = bottom - Math.Max(1, Convert.ToInt32(Math.Round(fullHeight * appVisibility)));
                }

                regions.Add(ClampRegion(new RegionRect(left, top, right, bottom), width, height));
            }

            double systemVisibility = GetSystemSegmentVisibility(taskbar);
            bool showTray = systemVisibility > 0.015;
            if (hasTray && showTray)
            {
                int left = trayRect.Left - pad;
                int right = trayRect.Right + pad;
                int top = usableTop;
                int bottom = usableBottom;
                if (_settings.ShowTrayOnHover && systemVisibility < 1.0)
                {
                    int fullHeight = Math.Max(1, bottom - top);
                    top = bottom - Math.Max(1, Convert.ToInt32(Math.Round(fullHeight * systemVisibility)));
                }

                regions.Add(ClampRegion(new RegionRect(left, top, right, bottom), width, height));
            }

            allowEmptyRegion = regions.Count == 0 &&
                ((hasApp && _settings.ShowAppsOnHover) || (hasTray && _settings.ShowTrayOnHover));

            RemoveTinyRegions(regions);
            allowEmptyRegion = allowEmptyRegion || (regions.Count == 0 &&
                ((hasApp && _settings.ShowAppsOnHover) || (hasTray && _settings.ShowTrayOnHover)));
            return regions;
        }

        private double GetAppSegmentVisibility(TaskbarInfo taskbar)
        {
            return GetHoverSegmentVisibility(
                _appHoverStates,
                taskbar,
                taskbar.AppListRect,
                _settings.ShowAppsOnHover,
                1.0);
        }

        private double GetSystemSegmentVisibility(TaskbarInfo taskbar)
        {
            return GetHoverSegmentVisibility(
                _systemHoverStates,
                taskbar,
                taskbar.TrayRect,
                _settings.ShowTrayOnHover,
                _settings.ShowSystemTraySegment ? 1.0 : 0.0);
        }

        private double GetHoverSegmentVisibility(
            Dictionary<IntPtr, HoverSegmentState> states,
            TaskbarInfo taskbar,
            NativeMethods.RECT hoverRect,
            bool enabled,
            double disabledVisibility)
        {
            if (!enabled)
            {
                states.Remove(taskbar.Hwnd);
                return disabledVisibility;
            }

            HoverSegmentState state;
            if (!states.TryGetValue(taskbar.Hwnd, out state))
            {
                state = new HoverSegmentState();
                states[taskbar.Hwnd] = state;
            }

            long now = Stopwatch.GetTimestamp();
            state.Update(now);

            bool hovering = IsCursorNear(hoverRect, ScaleSetting(36, taskbar.ScaleFactor));
            double target = hovering ? 1.0 : 0.0;
            if (Math.Abs(state.Target - target) > 0.001)
            {
                state.Start(target, now);
            }

            state.Update(now);
            if (state.IsAnimating)
            {
                MarkHoverAnimationActive(taskbar.RefreshRateHz);
            }

            return state.Progress;
        }

        private void MarkHoverAnimationActive(int refreshRateHz)
        {
            HasActiveHoverAnimation = true;
            ActiveHoverFrameIntervalMs = Math.Min(ActiveHoverFrameIntervalMs, RefreshRateToInterval(refreshRateHz));
        }

        private static int RefreshRateToInterval(int refreshRateHz)
        {
            int hz = refreshRateHz > 0 ? refreshRateHz : 60;
            int interval = Convert.ToInt32(Math.Round(1000.0 / hz));
            return Clamp(interval, 4, 33);
        }

        private void StabilizeMeasuredRects(TaskbarInfo taskbar)
        {
            bool dynamicSegments = String.Equals(_settings.ShapeMode, "DynamicSegments", StringComparison.OrdinalIgnoreCase);
            bool measuredCenterAndSystem = String.Equals(_settings.ShapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase) &&
                !taskbar.IsWindows11;

            if (!dynamicSegments && !measuredCenterAndSystem)
            {
                _dynamicMeasurements.Remove(taskbar.Hwnd);
                return;
            }

            bool shellSurfaceChecked = false;
            bool shellSurfaceActive = false;

            if (taskbar.AppListRect.IsEmpty)
            {
                DynamicMeasurementState missingState;
                if (_dynamicMeasurements.TryGetValue(taskbar.Hwnd, out missingState) && missingState.HasLast)
                {
                    missingState.MissingReads++;
                    taskbar.AppListRect = missingState.LastAppRect;
                    taskbar.TrayRect = SelectRect(taskbar.TrayRect, missingState.LastTrayRect);
                    return;
                }

                if (GetShellSurfaceActive(taskbar, ref shellSurfaceChecked, ref shellSurfaceActive) &&
                    IsUsefulAbsoluteChildRect(taskbar.NativeAppListRect, taskbar.Rect))
                {
                    taskbar.AppListRect = taskbar.NativeAppListRect;
                    missingState = new DynamicMeasurementState();
                    _dynamicMeasurements[taskbar.Hwnd] = missingState;
                    missingState.Accept(taskbar);
                }

                return;
            }

            DynamicMeasurementState state;
            if (!_dynamicMeasurements.TryGetValue(taskbar.Hwnd, out state))
            {
                state = new DynamicMeasurementState();
                _dynamicMeasurements[taskbar.Hwnd] = state;
            }

            int currentWidth = taskbar.AppListRect.Width;
            if (!state.HasLast || TaskbarFrameChanged(state.LastTaskbarRect, taskbar.Rect))
            {
                if (GetShellSurfaceActive(taskbar, ref shellSurfaceChecked, ref shellSurfaceActive) &&
                    IsUsefulAbsoluteChildRect(taskbar.NativeAppListRect, taskbar.Rect))
                {
                    taskbar.AppListRect = taskbar.NativeAppListRect;
                }

                state.Accept(taskbar);
                return;
            }

            int previousWidth = state.LastAppRect.Width;
            int suspiciousShrink = Math.Max(72, previousWidth / 5);
            int suspiciousInset = Math.Max(48, previousWidth / 10);
            bool shrunkWidth = previousWidth > 0 && currentWidth > 0 &&
                previousWidth - currentWidth >= suspiciousShrink;
            bool movedInside = state.LastAppRect.Left < taskbar.AppListRect.Left &&
                state.LastAppRect.Right > taskbar.AppListRect.Right &&
                (taskbar.AppListRect.Left - state.LastAppRect.Left >= suspiciousInset ||
                 state.LastAppRect.Right - taskbar.AppListRect.Right >= suspiciousInset);

            if (shrunkWidth || movedInside)
            {
                state.ShrinkReads++;
                taskbar.AppListRect = state.LastAppRect;
                taskbar.TrayRect = SelectRect(taskbar.TrayRect, state.LastTrayRect);
                return;
            }

            state.Accept(taskbar);
        }

        private static bool GetShellSurfaceActive(TaskbarInfo taskbar, ref bool checkedFlag, ref bool active)
        {
            if (!checkedFlag)
            {
                active = IsShellSurfaceActive(taskbar);
                checkedFlag = true;
            }

            return active;
        }

        private static NativeMethods.RECT SelectRect(NativeMethods.RECT preferred, NativeMethods.RECT fallback)
        {
            return preferred.IsEmpty ? fallback : preferred;
        }

        private static bool IsUsefulAbsoluteChildRect(NativeMethods.RECT child, NativeMethods.RECT parent)
        {
            RegionRect rect;
            return TryMakeRelativeRect(child, parent, out rect) &&
                IsUsefulChildRect(rect, parent.Width, parent.Height);
        }

        private static bool TaskbarFrameChanged(NativeMethods.RECT previous, NativeMethods.RECT current)
        {
            return previous.Left != current.Left ||
                previous.Top != current.Top ||
                previous.Right != current.Right ||
                previous.Bottom != current.Bottom;
        }

        private static bool IsShellSurfaceActive(TaskbarInfo taskbar)
        {
            IntPtr taskbarMonitor = NativeMethods.MonitorFromWindow(taskbar.Hwnd, NativeMethods.MonitorDefaultToNearest);
            if (taskbarMonitor == IntPtr.Zero)
            {
                return false;
            }

            bool active = false;
            NativeMethods.EnumWindowsProc callback = delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (active)
                {
                    return false;
                }

                if (hwnd == taskbar.Hwnd || !NativeMethods.IsWindowVisible(hwnd) || IsWindowCloaked(hwnd))
                {
                    return true;
                }

                if (NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest) != taskbarMonitor)
                {
                    return true;
                }

                if (IsTransientShellSurface(hwnd, taskbar.Rect))
                {
                    active = true;
                    return false;
                }

                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            return active;
        }

        private static bool IsTransientShellSurface(IntPtr hwnd, NativeMethods.RECT taskbarRect)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect) || rect.IsEmpty)
            {
                return false;
            }

            if (rect.Width < 160 || rect.Height < Math.Max(120, taskbarRect.Height * 2))
            {
                return false;
            }

            string processName = GetProcessName(hwnd);
            if (String.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(processName, "SearchUI", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string className = NativeMethods.GetWindowClass(hwnd);
            if (String.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal) ||
                String.Equals(className, "XamlExplorerHostIslandWindow", StringComparison.Ordinal))
            {
                return String.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(processName, "SearchUI", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string GetProcessName(IntPtr hwnd)
        {
            try
            {
                int processId;
                NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
                if (processId <= 0)
                {
                    return String.Empty;
                }

                using (Process process = Process.GetProcessById(processId))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return String.Empty;
            }
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
            if (regions.Count == 0)
            {
                return NativeMethods.CreateRectRgn(0, 0, 0, 0);
            }

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

        private sealed class DynamicMeasurementState
        {
            public bool HasLast;
            public NativeMethods.RECT LastAppRect;
            public NativeMethods.RECT LastTrayRect;
            public NativeMethods.RECT LastTaskbarRect;
            public int ShrinkReads;
            public int MissingReads;

            public void Accept(TaskbarInfo taskbar)
            {
                HasLast = true;
                LastAppRect = taskbar.AppListRect;
                LastTrayRect = taskbar.TrayRect;
                LastTaskbarRect = taskbar.Rect;
                ShrinkReads = 0;
                MissingReads = 0;
            }
        }

        private sealed class HoverSegmentState
        {
            public double Progress;
            public double Target;
            private double _from;
            private long _startedAtTicks;
            private int _durationMs;

            public bool IsAnimating
            {
                get { return Math.Abs(Progress - Target) > 0.001; }
            }

            public void Start(double target, long nowTicks)
            {
                Update(nowTicks);
                _from = Progress;
                Target = target;
                _startedAtTicks = nowTicks;
                _durationMs = target > _from ? 220 : 250;
            }

            public void Update(long nowTicks)
            {
                if (_durationMs <= 0)
                {
                    Progress = Target;
                    return;
                }

                double elapsed = ((double)(nowTicks - _startedAtTicks) * 1000.0) / Stopwatch.Frequency;
                if (elapsed >= _durationMs)
                {
                    Progress = Target;
                    return;
                }

                if (elapsed <= 0)
                {
                    Progress = _from;
                    return;
                }

                double t = elapsed / _durationMs;
                double eased = Target > _from ? EaseOutQuart(t) : EaseInOutSine(t);
                Progress = _from + ((Target - _from) * eased);
            }

            private static double EaseOutQuart(double t)
            {
                double p = 1.0 - t;
                return 1.0 - (p * p * p * p);
            }

            private static double EaseInOutSine(double t)
            {
                return -(Math.Cos(Math.PI * t) - 1.0) / 2.0;
            }
        }
    }
}
