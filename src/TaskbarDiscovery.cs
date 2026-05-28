using System;
using System.Collections.Generic;
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
        public static bool IsWindows11
        {
            get { return GetWindowsBuildNumber() >= 21996; }
        }

        public static List<TaskbarInfo> FindTaskbars(bool includeSecondary)
        {
            List<TaskbarInfo> result = new List<TaskbarInfo>();
            bool isWindows11 = IsWindows11;

            IntPtr primary = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            TaskbarInfo primaryInfo = CreateInfo(primary, true, isWindows11);
            if (primaryInfo != null)
            {
                result.Add(primaryInfo);
            }

            if (!includeSecondary)
            {
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

                TaskbarInfo info = CreateInfo(current, false, isWindows11);
                if (info != null)
                {
                    result.Add(info);
                }

                previous = current;
            }

            return result;
        }

        private static TaskbarInfo CreateInfo(IntPtr hwnd, bool isPrimary, bool isWindows11)
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

            NativeMethods.RECT automationAppRect;
            if (TryGetAutomationAppBounds(hwnd, rect, trayRect, out automationAppRect))
            {
                appListRect = automationAppRect;
            }

            TaskbarInfo info = new TaskbarInfo();
            info.Hwnd = hwnd;
            info.TrayHwnd = tray;
            info.AppListHwnd = appList;
            info.Rect = rect;
            info.TrayRect = trayRect;
            info.AppListRect = appListRect;
            info.MonitorRect = monitorRect;
            info.ScaleFactor = GetScaleFactor(hwnd);
            info.IsPrimary = isPrimary;
            info.IsWindows11 = isWindows11;
            info.Edge = InferEdge(rect, monitorRect);
            info.RefreshRateHz = GetRefreshRate(hwnd);
            return info;
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

                AutomationElementCollection descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
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
                if (element.Current.ControlType != ControlType.Button)
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
    }
}
