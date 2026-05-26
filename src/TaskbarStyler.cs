using System;
using System.Collections.Generic;

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

            List<IntPtr> taskbars = FindTaskbars(_settings.ApplyToSecondaryTaskbars);
            HashSet<IntPtr> live = new HashSet<IntPtr>();
            int styled = 0;

            for (int i = 0; i < taskbars.Count; i++)
            {
                IntPtr hwnd = taskbars[i];
                live.Add(hwnd);

                if (ApplyToWindow(hwnd, force))
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
            List<IntPtr> taskbars = FindTaskbars(true);
            for (int i = 0; i < taskbars.Count; i++)
            {
                RestoreWindow(taskbars[i]);
            }

            _applied.Clear();
        }

        private bool ApplyToWindow(IntPtr hwnd, bool force)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return false;
            }

            int width = rect.Width;
            int height = rect.Height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Margins margins = ClampMargins(width, height);
            int radius = Math.Max(0, Math.Min(_settings.CornerRadius, Math.Min(width, height) / 2));
            List<RegionRect> regions = BuildRegions(width, height, margins);
            if (regions.Count == 0)
            {
                RestoreWindow(hwnd);
                _applied.Remove(hwnd);
                return false;
            }

            string fingerprint = width + "x" + height + ":" + _settings.ShapeMode + ":" + radius + ":" +
                margins.Left + "," + margins.Top + "," + margins.Right + "," + margins.Bottom;

            for (int i = 0; i < regions.Count; i++)
            {
                fingerprint += ":" + regions[i].Left + "," + regions[i].Top + "," + regions[i].Right + "," + regions[i].Bottom;
            }

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
            return true;
        }

        private List<RegionRect> BuildRegions(int width, int height, Margins margins)
        {
            List<RegionRect> regions = new List<RegionRect>();

            if (String.Equals(_settings.ShapeMode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase))
            {
                int usableTop = margins.Top;
                int usableBottom = height - margins.Bottom + 1;
                int centerWidth = Clamp(_settings.CenterWidth, 80, Math.Max(80, width - margins.Left - margins.Right));
                int centerLeft = (width - centerWidth) / 2 + _settings.CenterOffset;
                int systemWidth = Clamp(_settings.SystemWidth, 80, Math.Max(80, width - 24));
                int systemRightInset = Clamp(_settings.SystemRightInset, 0, Math.Max(0, width - 80));
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
                regions.Add(ClampRegion(new RegionRect(
                    margins.Left,
                    margins.Top,
                    width - margins.Right + 1,
                    height - margins.Bottom + 1), width, height));
            }

            for (int i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Width < 18 || regions[i].Height < 12)
                {
                    regions.RemoveAt(i);
                }
            }

            return regions;
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
        }

        private Margins ClampMargins(int width, int height)
        {
            int maxHorizontal = Math.Max(0, (width - 24) / 2);
            int maxVertical = Math.Max(0, (height - 16) / 2);

            return new Margins(
                Clamp(_settings.MarginLeft, 0, maxHorizontal),
                Clamp(_settings.MarginTop, 0, maxVertical),
                Clamp(_settings.MarginRight, 0, maxHorizontal),
                Clamp(_settings.MarginBottom, 0, maxVertical));
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

        private static List<IntPtr> FindTaskbars(bool includeSecondary)
        {
            List<IntPtr> windows = new List<IntPtr>();
            IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                windows.Add(primary);
            }

            if (!includeSecondary)
            {
                return windows;
            }

            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                string className = NativeMethods.GetWindowClass(hwnd);
                if (String.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal) && !windows.Contains(hwnd))
                {
                    windows.Add(hwnd);
                }

                return true;
            }, IntPtr.Zero);

            return windows;
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
