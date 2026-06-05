using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Automation;

namespace RoundedTask
{
    internal enum TaskbarEdge
    {
        Unknown,
        Left,
        Top,
        Right,
        Bottom
    }

    internal sealed class TaskbarInfo
    {
        public IntPtr Hwnd;
        public IntPtr TrayHwnd;
        public IntPtr AppListHwnd;
        public NativeMethods.RECT Rect;
        public NativeMethods.RECT TrayRect;
        public NativeMethods.RECT AppListRect;
        public NativeMethods.RECT NativeAppListRect;
        public NativeMethods.RECT MonitorRect;
        public double ScaleFactor;
        public bool IsPrimary;
        public bool IsWindows11;
        public TaskbarEdge Edge;
        public int RefreshRateHz;

        public bool IsHorizontal
        {
            get { return Edge == TaskbarEdge.Top || Edge == TaskbarEdge.Bottom || Rect.Width >= Rect.Height; }
        }
    }

    internal static class TaskbarDiscovery
    {
        private const int AutomationSuccessCacheMs = 2000;
        private const int AutomationFailureRetryMs = 3000;
        private static readonly object AutomationCacheLock = new object();
        private static readonly Dictionary<IntPtr, AutomationBoundsCache> AutomationCache = new Dictionary<IntPtr, AutomationBoundsCache>();

        public static bool IsWindows11
        {
            get { return GetWindowsBuildNumber() >= 21996; }
        }

        public static List<TaskbarInfo> FindTaskbars(bool includeSecondary)
        {
            return FindTaskbars(includeSecondary, false);
        }

        public static List<TaskbarInfo> FindTaskbars(bool includeSecondary, bool useAutomationAppBounds)
        {
            List<TaskbarInfo> result = new List<TaskbarInfo>();
            HashSet<IntPtr> liveTaskbars = new HashSet<IntPtr>();
            bool isWindows11 = IsWindows11;

            IntPtr primary = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            TaskbarInfo primaryInfo = CreateInfo(primary, true, isWindows11, useAutomationAppBounds);
            if (primaryInfo != null)
            {
                result.Add(primaryInfo);
                liveTaskbars.Add(primaryInfo.Hwnd);
            }

            if (!includeSecondary)
            {
                PruneAutomationCache(liveTaskbars);
                return result;
            }

            IntPtr previous = IntPtr.Zero;
            while (true)
            {
                IntPtr current = NativeMethods.FindWindowEx(IntPtr.Zero, previous, "Shell_SecondaryTrayWnd", null);
                if (current == IntPtr.Zero)
                {
                    break;
                }

                TaskbarInfo info = CreateInfo(current, false, isWindows11, useAutomationAppBounds);
                if (info != null)
                {
                    result.Add(info);
                    liveTaskbars.Add(info.Hwnd);
                }

                previous = current;
            }

            PruneAutomationCache(liveTaskbars);
            return result;
        }

        public static void InvalidateAutomationCache()
        {
            lock (AutomationCacheLock)
            {
                AutomationCache.Clear();
            }
        }

        private static TaskbarInfo CreateInfo(IntPtr hwnd, bool isPrimary, bool isWindows11, bool useAutomationAppBounds)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            {
                return null;
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect) || rect.IsEmpty)
            {
                return null;
            }

            NativeMethods.RECT monitorRect = GetMonitorRect(hwnd, rect);
            IntPtr tray = FindTrayWindow(hwnd, rect, isWindows11);
            IntPtr appList = FindAppListWindow(hwnd);

            NativeMethods.RECT trayRect = new NativeMethods.RECT();
            if (tray != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(tray, out trayRect);
            }

            NativeMethods.RECT appListRect = new NativeMethods.RECT();
            if (appList != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(appList, out appListRect);
            }
            NativeMethods.RECT nativeAppListRect = appListRect;

            NativeMethods.RECT automationAppRect;
            if (useAutomationAppBounds && TryGetCachedAutomationAppBounds(hwnd, rect, trayRect, out automationAppRect))
            {
                appListRect = MergeAppListRects(nativeAppListRect, automationAppRect, rect, trayRect);
            }

            TaskbarInfo info = new TaskbarInfo();
            info.Hwnd = hwnd;
            info.TrayHwnd = tray;
            info.AppListHwnd = appList;
            info.Rect = rect;
            info.TrayRect = trayRect;
            info.AppListRect = appListRect;
            info.NativeAppListRect = nativeAppListRect;
            info.MonitorRect = monitorRect;
            info.ScaleFactor = GetScaleFactor(hwnd);
            info.IsPrimary = isPrimary;
            info.IsWindows11 = isWindows11;
            info.Edge = InferEdge(rect, monitorRect);
            info.RefreshRateHz = GetRefreshRate(hwnd);
            return info;
        }

        private static NativeMethods.RECT MergeAppListRects(
            NativeMethods.RECT nativeRect,
            NativeMethods.RECT automationRect,
            NativeMethods.RECT taskbarRect,
            NativeMethods.RECT trayRect)
        {
            bool hasNative = IsUsefulAbsoluteChildRect(nativeRect, taskbarRect);
            bool hasAutomation = IsUsefulAbsoluteChildRect(automationRect, taskbarRect);

            if (hasNative && hasAutomation)
            {
                NativeMethods.RECT merged = new NativeMethods.RECT();
                merged.Left = Math.Min(nativeRect.Left, automationRect.Left);
                merged.Top = Math.Min(nativeRect.Top, automationRect.Top);
                merged.Right = Math.Max(nativeRect.Right, automationRect.Right);
                merged.Bottom = Math.Max(nativeRect.Bottom, automationRect.Bottom);
                return ClampAbsoluteChildRect(merged, taskbarRect, trayRect);
            }

            if (hasAutomation)
            {
                return ClampAbsoluteChildRect(automationRect, taskbarRect, trayRect);
            }

            if (hasNative)
            {
                return ClampAbsoluteChildRect(nativeRect, taskbarRect, trayRect);
            }

            return nativeRect;
        }

        private static NativeMethods.RECT ClampAbsoluteChildRect(
            NativeMethods.RECT rect,
            NativeMethods.RECT taskbarRect,
            NativeMethods.RECT trayRect)
        {
            NativeMethods.RECT clamped = rect;
            clamped.Left = Math.Max(taskbarRect.Left, clamped.Left);
            clamped.Top = Math.Max(taskbarRect.Top, clamped.Top);
            clamped.Right = Math.Min(taskbarRect.Right, clamped.Right);
            clamped.Bottom = Math.Min(taskbarRect.Bottom, clamped.Bottom);

            if (!trayRect.IsEmpty && trayRect.Left > taskbarRect.Left)
            {
                clamped.Right = Math.Min(clamped.Right, trayRect.Left);
            }

            if (clamped.Right <= clamped.Left || clamped.Bottom <= clamped.Top)
            {
                return rect;
            }

            return clamped;
        }

        private static bool IsUsefulAbsoluteChildRect(NativeMethods.RECT child, NativeMethods.RECT parent)
        {
            if (child.IsEmpty || parent.IsEmpty)
            {
                return false;
            }

            int width = child.Width;
            int height = child.Height;
            if (width < 24 || height < 8)
            {
                return false;
            }

            if (child.Right <= parent.Left || child.Bottom <= parent.Top || child.Left >= parent.Right || child.Top >= parent.Bottom)
            {
                return false;
            }

            if (width >= parent.Width - 2 && height >= parent.Height - 2)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetCachedAutomationAppBounds(IntPtr taskbar, NativeMethods.RECT taskbarRect, NativeMethods.RECT trayRect, out NativeMethods.RECT bounds)
        {
            bounds = new NativeMethods.RECT();

            long now = Stopwatch.GetTimestamp();
            AutomationBoundsCache cache;
            lock (AutomationCacheLock)
            {
                if (AutomationCache.TryGetValue(taskbar, out cache))
                {
                    int ageMs = TicksToMilliseconds(now - cache.UpdatedAtTicks);
                    if (cache.HasBounds && ageMs < AutomationSuccessCacheMs)
                    {
                        bounds = cache.Bounds;
                        return true;
                    }

                    if (!cache.HasBounds && ageMs < AutomationFailureRetryMs)
                    {
                        return false;
                    }
                }
            }

            bool found = TryGetAutomationAppBounds(taskbar, taskbarRect, trayRect, out bounds);
            lock (AutomationCacheLock)
            {
                AutomationCache[taskbar] = new AutomationBoundsCache(bounds, found, now);
            }

            return found;
        }

        private static void PruneAutomationCache(HashSet<IntPtr> liveTaskbars)
        {
            lock (AutomationCacheLock)
            {
                List<IntPtr> stale = new List<IntPtr>();
                foreach (IntPtr hwnd in AutomationCache.Keys)
                {
                    if (!liveTaskbars.Contains(hwnd) || !NativeMethods.IsWindow(hwnd))
                    {
                        stale.Add(hwnd);
                    }
                }

                for (int i = 0; i < stale.Count; i++)
                {
                    AutomationCache.Remove(stale[i]);
                }
            }
        }

        private static int TicksToMilliseconds(long ticks)
        {
            if (ticks <= 0)
            {
                return 0;
            }

            return Convert.ToInt32(Math.Min(Int32.MaxValue, (ticks * 1000L) / Stopwatch.Frequency));
        }

        private static int GetWindowsBuildNumber()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("CurrentBuildNumber") ?? key.GetValue("CurrentBuild");
                        int build;
                        if (value != null && Int32.TryParse(Convert.ToString(value), out build))
                        {
                            return build;
                        }
                    }
                }
            }
            catch
            {
            }

            return Environment.OSVersion.Version.Build;
        }

        private static double GetScaleFactor(IntPtr hwnd)
        {
            try
            {
                int dpi = NativeMethods.GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
            catch
            {
            }

            return 1.0;
        }

        private static int GetRefreshRate(IntPtr hwnd)
        {
            try
            {
                Screen screen = Screen.FromHandle(hwnd);
                NativeMethods.DEVMODE mode = new NativeMethods.DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
                if (NativeMethods.EnumDisplaySettings(screen.DeviceName, NativeMethods.EnumCurrentSettings, ref mode) &&
                    mode.dmDisplayFrequency > 0 && mode.dmDisplayFrequency < 1000)
                {
                    return mode.dmDisplayFrequency;
                }
            }
            catch
            {
            }

            return 60;
        }

        private static NativeMethods.RECT GetMonitorRect(IntPtr hwnd, NativeMethods.RECT fallback)
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return fallback;
            }

            NativeMethods.MONITORINFO info = new NativeMethods.MONITORINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return fallback;
            }

            return info.rcMonitor;
        }

        private static TaskbarEdge InferEdge(NativeMethods.RECT taskbar, NativeMethods.RECT monitor)
        {
            if (taskbar.Width >= taskbar.Height)
            {
                int topDistance = Math.Abs(taskbar.Top - monitor.Top);
                int bottomDistance = Math.Abs(taskbar.Bottom - monitor.Bottom);
                return topDistance <= bottomDistance ? TaskbarEdge.Top : TaskbarEdge.Bottom;
            }

            int leftDistance = Math.Abs(taskbar.Left - monitor.Left);
            int rightDistance = Math.Abs(taskbar.Right - monitor.Right);
            return leftDistance <= rightDistance ? TaskbarEdge.Left : TaskbarEdge.Right;
        }

        private static IntPtr FindTrayWindow(IntPtr taskbar, NativeMethods.RECT taskbarRect, bool isWindows11)
        {
            IntPtr tray = FindBestDescendantByClass(taskbar, "TrayNotifyWnd", true);
            if (tray != IntPtr.Zero)
            {
                return tray;
            }

            if (!isWindows11)
            {
                return IntPtr.Zero;
            }

            return FindRightmostBridge(taskbar, taskbarRect);
        }

        private static IntPtr FindAppListWindow(IntPtr taskbar)
        {
            IntPtr appList = FindBestDescendantByClass(taskbar, "MSTaskSwWClass", false);
            if (appList != IntPtr.Zero)
            {
                return appList;
            }

            return FindBestDescendantByClass(taskbar, "MSTaskListWClass", false);
        }

        private static bool TryGetAutomationAppBounds(IntPtr taskbar, NativeMethods.RECT taskbarRect, NativeMethods.RECT trayRect, out NativeMethods.RECT bounds)
        {
            bounds = new NativeMethods.RECT();

            try
            {
                AutomationElement root = AutomationElement.FromHandle(taskbar);
                if (root == null)
                {
                    return false;
                }

                Condition buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                Condition listItemCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
                Condition candidateCondition = new OrCondition(buttonCondition, listItemCondition);
                AutomationElementCollection descendants = root.FindAll(TreeScope.Descendants, candidateCondition);
                bool found = false;
                int left = Int32.MaxValue;
                int top = Int32.MaxValue;
                int right = Int32.MinValue;
                int bottom = Int32.MinValue;
                int trayLeft = trayRect.IsEmpty ? taskbarRect.Right : trayRect.Left;

                for (int i = 0; i < descendants.Count; i++)
                {
                    AutomationElement element = descendants[i];
                    if (!IsTaskbarAppButton(element, taskbarRect, trayLeft))
                    {
                        continue;
                    }

                    System.Windows.Rect rect = element.Current.BoundingRectangle;
                    left = Math.Min(left, Convert.ToInt32(Math.Floor(rect.Left)));
                    top = Math.Min(top, Convert.ToInt32(Math.Floor(rect.Top)));
                    right = Math.Max(right, Convert.ToInt32(Math.Ceiling(rect.Right)));
                    bottom = Math.Max(bottom, Convert.ToInt32(Math.Ceiling(rect.Bottom)));
                    found = true;
                }

                if (!found || right <= left || bottom <= top)
                {
                    return false;
                }

                bounds.Left = left;
                bounds.Top = top;
                bounds.Right = right;
                bounds.Bottom = bottom;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTaskbarAppButton(AutomationElement element, NativeMethods.RECT taskbarRect, int trayLeft)
        {
            try
            {
                ControlType controlType = element.Current.ControlType;
                if (controlType != ControlType.Button &&
                    controlType != ControlType.ListItem)
                {
                    return false;
                }

                System.Windows.Rect rect = element.Current.BoundingRectangle;
                if (rect.Width < 20 || rect.Height < 20)
                {
                    return false;
                }

                if (rect.Bottom <= taskbarRect.Top || rect.Top >= taskbarRect.Bottom)
                {
                    return false;
                }

                if (rect.Left < taskbarRect.Left || rect.Right > trayLeft)
                {
                    return false;
                }

                string className = element.Current.ClassName ?? String.Empty;
                if (className.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    return false;
                }

                if (className.IndexOf("Tray", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                if (String.Equals(className, "Taskbar.TaskListButtonAutomationPeer", StringComparison.Ordinal))
                {
                    return true;
                }

                string name = element.Current.Name ?? String.Empty;
                if (String.Equals(className, "ToggleButton", StringComparison.Ordinal) &&
                    (name.IndexOf("Iniciar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                return true;
            }
            catch
            {
            }

            return false;
        }

        private static IntPtr FindBestDescendantByClass(IntPtr root, string className, bool preferRightmost)
        {
            List<IntPtr> windows = GetDescendants(root);
            IntPtr best = IntPtr.Zero;
            int bestScore = Int32.MinValue;

            for (int i = 0; i < windows.Count; i++)
            {
                IntPtr hwnd = windows[i];
                if (!String.Equals(NativeMethods.GetWindowClass(hwnd), className, StringComparison.Ordinal))
                {
                    continue;
                }

                NativeMethods.RECT rect;
                if (!NativeMethods.GetWindowRect(hwnd, out rect) || rect.IsEmpty)
                {
                    continue;
                }

                int score = preferRightmost ? rect.Right : rect.Width * rect.Height;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hwnd;
                }
            }

            return best;
        }

        private static IntPtr FindRightmostBridge(IntPtr root, NativeMethods.RECT taskbarRect)
        {
            List<IntPtr> windows = GetDescendants(root);
            IntPtr best = IntPtr.Zero;
            int bestRight = Int32.MinValue;

            for (int i = 0; i < windows.Count; i++)
            {
                IntPtr hwnd = windows[i];
                if (!String.Equals(NativeMethods.GetWindowClass(hwnd), "Windows.UI.Composition.DesktopWindowContentBridge", StringComparison.Ordinal))
                {
                    continue;
                }

                NativeMethods.RECT rect;
                if (!NativeMethods.GetWindowRect(hwnd, out rect) || rect.IsEmpty)
                {
                    continue;
                }

                if (rect.Width > taskbarRect.Width - 8 || rect.Height > taskbarRect.Height - 8)
                {
                    continue;
                }

                if (rect.Right > bestRight)
                {
                    bestRight = rect.Right;
                    best = hwnd;
                }
            }

            return best;
        }

        private static List<IntPtr> GetDescendants(IntPtr root)
        {
            List<IntPtr> result = new List<IntPtr>();
            NativeMethods.EnumChildWindowsProc callback = delegate(IntPtr hwnd, IntPtr lParam)
            {
                result.Add(hwnd);
                return true;
            };

            NativeMethods.EnumChildWindows(root, callback, IntPtr.Zero);
            return result;
        }

        private sealed class AutomationBoundsCache
        {
            public readonly NativeMethods.RECT Bounds;
            public readonly bool HasBounds;
            public readonly long UpdatedAtTicks;

            public AutomationBoundsCache(NativeMethods.RECT bounds, bool hasBounds, long updatedAtTicks)
            {
                Bounds = bounds;
                HasBounds = hasBounds;
                UpdatedAtTicks = updatedAtTicks;
            }
        }
    }
}
