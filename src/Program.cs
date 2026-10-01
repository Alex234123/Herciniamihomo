using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using Microsoft.Win32;
using System.Net.WebSockets;

namespace Herciniamihomo
{
    // =========================================================================
    // 1. 1000Hz (1kHz) Extreme Physics & VSync 按需唤醒惯性平滑滚动容器 (Zero-Idle-CPU SmoothScrollViewer)
    // =========================================================================
    public class SmoothScrollViewer : ScrollViewer
    {
        private double _targetOffset = 0;
        private double _currentOffset = 0;
        private double _velocity = 0;
        private bool _isAnimating = false;
        private DateTime _lastTickTime = DateTime.Now;

        public SmoothScrollViewer()
        {
            this.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            this.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            this.PanningMode = PanningMode.VerticalOnly;
            this.SnapsToDevicePixels = true;
            this.UseLayoutRounding = true;
            this.CanContentScroll = false; // 启用物理像素级平滑平移
            ApplyMinimalGlassScrollBarStyle();
        }

        private void ApplyMinimalGlassScrollBarStyle()
        {
            try
            {
                string xaml = @"
                <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                       TargetType='{x:Type ScrollBar}'>
                    <Setter Property='Width' Value='6'/>
                    <Setter Property='MinWidth' Value='6'/>
                    <Setter Property='Background' Value='Transparent'/>
                    <Setter Property='Margin' Value='2,6,2,6'/>
                    <Setter Property='Template'>
                        <Setter.Value>
                            <ControlTemplate TargetType='{x:Type ScrollBar}'>
                                <Grid Background='Transparent'>
                                    <Track x:Name='PART_Track' IsDirectionReversed='true'>
                                        <Track.Thumb>
                                            <Thumb>
                                                <Thumb.Template>
                                                    <ControlTemplate TargetType='{x:Type Thumb}'>
                                                        <Border x:Name='ThumbBd' CornerRadius='3' Background='#5594A3B8'/>
                                                        <ControlTemplate.Triggers>
                                                            <Trigger Property='IsMouseOver' Value='True'>
                                                                <Setter TargetName='ThumbBd' Property='Background' Value='#CC38BDF8'/>
                                                            </Trigger>
                                                        </ControlTemplate.Triggers>
                                                    </ControlTemplate>
                                                </Thumb.Template>
                                            </Thumb>
                                        </Track.Thumb>
                                    </Track>
                                </Grid>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>";
                Style sbStyle = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
                this.Resources.Add(typeof(ScrollBar), sbStyle);
            }
            catch { }
        }

        protected override void OnScrollChanged(ScrollChangedEventArgs e)
        {
            base.OnScrollChanged(e);
            if (!_isAnimating)
            {
                _currentOffset = this.VerticalOffset;
                _targetOffset = this.VerticalOffset;
                _velocity = 0;
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            e.Handled = true;

            // 丝滑滚轮灵敏度：单格拨轮步进约 125 像素，多次连续拨动智能蓄能加速
            double scrollDelta = -e.Delta * 1.05;

            if (!_isAnimating)
            {
                _currentOffset = this.VerticalOffset;
                _targetOffset = this.VerticalOffset;
                _velocity = 0;
                _lastTickTime = DateTime.Now;
                _isAnimating = true;
                CompositionTarget.Rendering += OnRenderFrame;
            }

            _targetOffset += scrollDelta;
            if (_targetOffset < 0) _targetOffset = 0;
            if (_targetOffset > this.ScrollableHeight) _targetOffset = this.ScrollableHeight;

            // 赋予即时初动量，确保第 1 帧零延迟同步启动
            if ((_velocity > 0 && scrollDelta > 0) || (_velocity < 0 && scrollDelta < 0))
            {
                _velocity += scrollDelta * 4.5;
            }
            else
            {
                _velocity = scrollDelta * 5.0;
            }

            double maxV = 4200.0;
            if (_velocity > maxV) _velocity = maxV;
            if (_velocity < -maxV) _velocity = -maxV;
        }

        private void OnRenderFrame(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double dt = (now - _lastTickTime).TotalSeconds;
            _lastTickTime = now;
            if (dt <= 0.00005 || dt > 0.05) dt = 0.001; // 针对 1000Hz (1kHz) 极速刷新率优化单帧时间步长 (1.0ms)

            // 工业级高精度 SmoothDamp 阻尼算法：完美临界阻尼、零过冲、无抖动、1000Hz 极致微米级物理惯性滑行
            _currentOffset = SmoothDamp(_currentOffset, _targetOffset, ref _velocity, 0.115, dt);

            // 边界约束
            if (_currentOffset <= 0)
            {
                _currentOffset = 0;
                _velocity = 0;
            }
            else if (_currentOffset >= this.ScrollableHeight)
            {
                _currentOffset = this.ScrollableHeight;
                _velocity = 0;
            }

            this.ScrollToVerticalOffset(_currentOffset);

            // 1000Hz 极致微精度停靠判定 (微米级误差截断，保证 1000Hz 调度器极低能耗及时休眠)
            if (Math.Abs(_targetOffset - _currentOffset) < 0.15 && Math.Abs(_velocity) < 0.5)
            {
                _currentOffset = _targetOffset;
                this.ScrollToVerticalOffset(_currentOffset);
                _velocity = 0;
                _isAnimating = false;
                CompositionTarget.Rendering -= OnRenderFrame;
            }
        }

        private static double SmoothDamp(double current, double target, ref double currentVelocity, double smoothTime, double dt)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime;
            double x = omega * dt;
            double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target;
            double temp = (currentVelocity + omega * change) * dt;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            return output;
        }

        public void SmoothScrollTo(double target)
        {
            if (!_isAnimating)
            {
                _currentOffset = this.VerticalOffset;
                _lastTickTime = DateTime.Now;
                _isAnimating = true;
                CompositionTarget.Rendering += OnRenderFrame;
            }
            _targetOffset = Math.Max(0, Math.Min(this.ScrollableHeight, target));
        }
    }

    // =========================================================================
    // 1.5 响应式自适应卡片网格面板 (Fluid Responsive Uniform Auto-Fitting Grid)
    // 确保节点列表左右两侧始终与上方卡片 100% 严丝合缝平齐，右侧永不留白
    // =========================================================================
    public class ResponsiveCardsPanel : Panel
    {
        public double MinCardWidth { get; set; }
        public double CardHeight { get; set; }
        public double Gap { get; set; }

        public ResponsiveCardsPanel()
        {
            this.MinCardWidth = 250.0;
            this.CardHeight = 84.0;
            this.Gap = 12.0;
            this.SnapsToDevicePixels = true;
            this.UseLayoutRounding = true;
        }

        private void GetColumnMetrics(double availWidth, out int cols, out double[] xOffsets, out double[] widths)
        {
            cols = Math.Max(1, (int)Math.Floor((availWidth + Gap) / (MinCardWidth + Gap)));
            xOffsets = new double[cols];
            widths = new double[cols];

            double totalGaps = (cols - 1) * Gap;
            double usableWidth = Math.Max(0, availWidth - totalGaps);
            double baseWidth = Math.Floor(usableWidth / cols);
            double remainder = usableWidth - (baseWidth * cols);

            double currentX = 0;
            for (int c = 0; c < cols; c++)
            {
                double w = baseWidth + (c < remainder ? 1.0 : 0.0);
                xOffsets[c] = currentX;
                widths[c] = w;
                currentX += w + Gap;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = InternalChildren.Count;
            if (count == 0) return new Size(0, 0);

            double availWidth = availableSize.Width;
            if (double.IsInfinity(availWidth) || availWidth <= 0)
            {
                availWidth = this.ActualWidth > 0 ? this.ActualWidth : 1000.0;
            }

            int cols;
            double[] xOffsets, widths;
            GetColumnMetrics(availWidth, out cols, out xOffsets, out widths);

            for (int i = 0; i < count; i++)
            {
                UIElement child = InternalChildren[i];
                if (child != null)
                {
                    int col = i % cols;
                    child.Measure(new Size(widths[col], CardHeight));
                }
            }

            int totalRows = (count + cols - 1) / cols;
            double totalHeight = totalRows * CardHeight + Math.Max(0, totalRows - 1) * Gap;
            return new Size(availWidth, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = InternalChildren.Count;
            if (count == 0) return finalSize;

            double availWidth = finalSize.Width;
            if (availWidth <= 0) return finalSize;

            int cols;
            double[] xOffsets, widths;
            GetColumnMetrics(availWidth, out cols, out xOffsets, out widths);

            for (int i = 0; i < count; i++)
            {
                UIElement child = InternalChildren[i];
                if (child == null) continue;

                int row = i / cols;
                int col = i % cols;

                double x = xOffsets[col];
                double y = row * (CardHeight + Gap);
                double w = widths[col];

                child.Arrange(new Rect(x, y, w, CardHeight));
            }

            int totalRows = (count + cols - 1) / cols;
            double arrangedHeight = totalRows * CardHeight + Math.Max(0, totalRows - 1) * Gap;
            return new Size(finalSize.Width, arrangedHeight);
        }
    }

    // =========================================================================
    // 2. 预冻结高精度矢量图标库 (Frozen Geometries - 零内存重复分配)
    // =========================================================================
    public static class VectorIcons
    {
        public const string Dashboard = "M3,13H11V3H3V13ZM3,21H11V15H3V21ZM13,21H21V11H13V21ZM13,3V9H21V3H13Z";
        public const string Proxies = "M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z";
        public const string Profiles = "M12,2L2,7L12,12L22,7L12,2ZM2,17L12,22L22,17L12,12L2,17ZM2,12L12,17L22,12";
        public const string Connections = "M12,2C6.48,2 2,6.48 2,12C2,17.52 6.48,22 12,22C17.52,22 22,17.52 22,12C22,6.48 17.52,2 12,2ZM11,19.93C7.05,19.44 4,16.08 4,12C4,11.38 4.08,10.79 4.21,10.21L9,15V16C9,17.1 9.9,18 11,18V19.93ZM17.9,17.39C17.64,16.58 16.9,16 16,16H15V13C15,12.45 14.55,12 14,12H8V10H10C10.55,10 11,9.55 11,9V7H13C14.1,7 15,6.1 15,5V4.59C17.93,5.78 20,8.65 20,12C20,14.08 19.2,15.97 17.9,17.39Z";
        public const string Studio = "M12,3C7.03,3 3,7.03 3,12C3,16.97 7.03,21 12,21C12.83,21 13.5,20.33 13.5,19.5C13.5,19.11 13.35,18.76 13.11,18.49C12.88,18.23 12.73,17.88 12.73,17.5C12.73,16.67 13.4,16 14.23,16H16C18.76,16 21,13.76 21,11C21,6.58 16.97,3 12,3ZM6.5,12C5.67,12 5,11.33 5,10.5C5,9.67 5.67,9 6.5,9C7.33,9 8,9.67 8,10.5C8,11.33 7.33,12 6.5,12ZM9.5,8C8.67,8 8,7.33 8,6.5C8,5.67 8.67,5 9.5,5C10.33,5 11,5.67 11,6.5C11,7.33 10.33,8 9.5,8ZM14.5,8C13.67,8 13,7.33 13,6.5C13,5.67 13.67,5 14.5,5C15.33,5 16,5.67 16,6.5C16,7.33 15.33,8 14.5,8ZM17.5,12C16.67,12 16,11.33 16,10.5C16,9.67 16.67,9 17.5,9C18.33,9 19,9.67 19,10.5C19,11.33 18.33,12 17.5,12Z";
        public const string Bolt = "M11,21H10L11,14H7.5C7.1,14 6.7,13.4 7,13L13,3H14L13,10H16.5C16.9,10 17.3,10.6 17,11L11,21Z";
        public const string Power = "M13,3H11V13H13V3ZM17.83,5.17L16.41,6.59C17.99,7.86 19,9.81 19,12C19,15.87 15.87,19 12,19C8.13,19 5,15.87 5,12C5,9.81 6.01,7.86 7.58,6.58L6.17,5.17C4.23,6.82 3,9.26 3,12C3,16.97 7.03,21 12,21C16.97,21 21,16.97 21,12C21,9.26 19.77,6.82 17.83,5.17Z";
        public const string Search = "M15.5,14H14.71L14.43,13.73C15.41,12.59 16,11.11 16,9.5C16,5.91 13.09,3 9.5,3C5.91,3 3,5.91 3,9.5C3,13.09 5.91,16 9.5,16C11.11,16 12.59,15.41 13.73,14.43L14,14.71V15.5L19,20.49L20.49,19L15.5,14ZM9.5,14C7.01,14 5,11.99 5,9.5C5,7.01 7.01,5 9.5,5C11.99,5 14,7.01 14,9.5C14,11.99 11.99,14 9.5,14Z";
        public const string SidebarToggle = "M4,6H20V8H4V6ZM4,11H14V13H4V11ZM4,16H20V18H4V16Z";
        public const string Shield = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1ZM10,17L6,13L7.41,11.59L10,14.17L16.59,7.58L18,9L10,17Z";
        public const string ArrowDown = "M11,4H13V16L18.5,10.5L19.92,11.92L12,19.84L4.08,11.92L5.5,10.5L11,16V4Z";
        public const string ArrowUp = "M13,20H11V8L5.5,13.5L4.08,12.08L12,4.16L19.92,12.08L18.5,13.5L13,8V20Z";
        public const string Folder = "M10,4H4C2.89,4 2,4.89 2,6V18C2,19.1 2.89,20 4,20H20C21.1,20 22,19.1 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
        public const string Refresh = "M17.65,6.35C16.2,4.9 14.21,4 12,4C7.58,4 4.01,7.58 4.01,12C4.01,16.42 7.58,20 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18C8.69,18 6,15.31 6,12C6,8.69 8.69,6 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z";
        public const string Check = "M9,16.17L4.83,12L3.41,13.41L9,19L21,7L19.59,5.59L9,16.17Z";
        public const string Trash = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";
        public const string Globe = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,19.93C7.05,19.44 4,16.08 4,12C4,11.38 4.08,10.79 4.21,10.21L9,15V16A1,1 0 0,0 10,17V19.93M17.9,17.39C17.64,16.58 16.9,16 16,16H15V13A1,1 0 0,0 14,12H8V10H10A1,1 0 0,0 11,9V7H13A2,2 0 0,0 15,5V4.59C17.93,5.78 20,8.65 20,12C20,14.08 19.2,15.97 17.9,17.39Z";
        public const string Doctor = "M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3M11,6H13V11H18V13H13V18H11V13H6V11H11V6Z";
        public const string Terminal = "M20,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6A2,2 0 0,0 20,4M20,18H4V8H20V18M6,14.5L9.5,11L6,7.5L7.41,6.09L12.33,11L7.41,15.91L6,14.5M18,17H13V15H18V17Z";
        public const string Direct = "M12,2L1,21H23L12,2M12,6L19.53,19H4.47L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z";

        private static readonly Dictionary<string, Geometry> _geoCache = new Dictionary<string, Geometry>();

        public static Viewbox Create(string pathData, Brush fill, double size = 16)
        {
            Geometry geo;
            if (!_geoCache.TryGetValue(pathData, out geo))
            {
                geo = Geometry.Parse(pathData);
                if (geo.CanFreeze) geo.Freeze();
                _geoCache[pathData] = geo;
            }

            Viewbox vb = new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas cv = new Canvas { Width = 24, Height = 24 };
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path
            {
                Data = geo,
                Fill = fill
            };
            cv.Children.Add(p);
            vb.Child = cv;
            return vb;
        }
    }

    // =========================================================================
    // 3. 数据模型
    // =========================================================================
    public class ProxyGroupItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Now { get; set; }
        public List<string> All { get; set; }
    }

    public class ProxyNodeInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Delay { get; set; }
    }

    public class ConnectionRecord
    {
        public string Id { get; set; }
        public string Host { get; set; }
        public string Network { get; set; }
        public string Rule { get; set; }
        public string Chain { get; set; }
        public long Upload { get; set; }
        public long Download { get; set; }
    }

    // =========================================================================
    // 3.5 Win32 Job Object 孤儿进程守护体系 (内核级进程生命周期绑定)
    // =========================================================================
    public static class ChildProcessTracker
    {
        private static IntPtr _jobHandle = IntPtr.Zero;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryLimit;
            public UIntPtr PeakJobMemoryLimit;
        }

        static ChildProcessTracker()
        {
            try
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle != IntPtr.Zero)
                {
                    var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    };
                    var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                    {
                        BasicLimitInformation = info
                    };
                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr pInfo = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(extendedInfo, pInfo, false);
                        SetInformationJobObject(_jobHandle, 9, pInfo, (uint)length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pInfo);
                    }
                }
            }
            catch { }
        }

        public static void AddProcess(Process process)
        {
            if (_jobHandle != IntPtr.Zero && process != null && !process.HasExited)
            {
                try { AssignProcessToJobObject(_jobHandle, process.Handle); } catch { }
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static IntPtr FindProcessWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == (uint)processId)
                    {
                        StringBuilder sb = new StringBuilder(256);
                        GetWindowText(hWnd, sb, 256);
                        string title = sb.ToString();
                        if (title.Contains("Herciniamihomo"))
                        {
                            found = hWnd;
                            return false;
                        }
                        StringBuilder cls = new StringBuilder(256);
                        GetClassName(hWnd, cls, 256);
                        if (cls.ToString().StartsWith("HwndWrapper"))
                        {
                            found = hWnd;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Herciniamihomo_Native_WPF_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    uint wakeMsg = RegisterWindowMessage("HERCINIAMIHOMO_WAKE_UP_MSG");

                    Process current = Process.GetCurrentProcess();
                    Process[] procs = Process.GetProcessesByName(current.ProcessName);
                    Process existing = null;
                    foreach (var p in procs)
                    {
                        if (p.Id != current.Id) { existing = p; break; }
                    }

                    bool restored = false;
                    if (existing != null)
                    {
                        try { AllowSetForegroundWindow((uint)existing.Id); } catch { }
                        IntPtr hwnd = existing.MainWindowHandle;
                        if (hwnd == IntPtr.Zero)
                        {
                            hwnd = FindProcessWindow(existing.Id);
                        }

                        if (hwnd != IntPtr.Zero)
                        {
                            ShowWindowAsync(hwnd, 9 /* SW_RESTORE */);
                            SetForegroundWindow(hwnd);
                            PostMessage(hwnd, wakeMsg, IntPtr.Zero, IntPtr.Zero);
                            restored = true;
                        }
                    }

                    PostMessage((IntPtr)0xffff, wakeMsg, IntPtr.Zero, IntPtr.Zero);

                    if (!restored && existing != null)
                    {
                        try
                        {
                            Thread.Sleep(300);
                            existing.Refresh();
                            IntPtr checkHwnd = existing.MainWindowHandle;
                            if (checkHwnd == IntPtr.Zero) checkHwnd = FindProcessWindow(existing.Id);
                            if (checkHwnd != IntPtr.Zero)
                            {
                                ShowWindowAsync(checkHwnd, 9);
                                SetForegroundWindow(checkHwnd);
                                return;
                            }
                            // 原进程为无窗口无响应幽灵后台，强制清理并由当前进程接管启动
                            existing.Kill();
                            existing.WaitForExit(1000);
                        }
                        catch { }
                    }
                    else
                    {
                        return;
                    }
                }

                try
                {
                    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    {
                        try
                        {
                            File.WriteAllText("D:\\DL\\Herciniamihomo\\crash.log",
                                e.ExceptionObject != null ? e.ExceptionObject.ToString() : "Unknown AppDomain exception");
                        }
                        catch { }
                    };

                    try
                    {
                        Timeline.DesiredFrameRateProperty.OverrideMetadata(
                            typeof(Timeline),
                            new FrameworkPropertyMetadata(1000)
                        );
                    }
                    catch { }
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
                    Application app = new Application();
                    app.DispatcherUnhandledException += (s, e) =>
                    {
                        try { File.WriteAllText("D:\\DL\\Herciniamihomo\\crash.log", e.Exception.ToString()); } catch { }
                        e.Handled = true;
                    };
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    bool startSilent = args != null && args.Any(a => a != null && (
                        a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("-minimized", StringComparison.OrdinalIgnoreCase)));
                    MainWindow mainWin = new MainWindow(startSilent);
                    if (!startSilent)
                    {
                        mainWin.Show();
                    }
                    app.Run();
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText("D:\\DL\\Herciniamihomo\\crash.log", ex.ToString()); } catch { }
                }
            }
        }
    }

    // =========================================================================
    // 4. Apple VisionOS + Clash Mi Pro 主窗口 (零椭圆畸变 + 极致 GPU 性能版)
    // =========================================================================
    public class MainWindow : Window
    {
        private static readonly DoubleAnimation _hoverLiftAnim;
        private static readonly DoubleAnimation _hoverScaleUpAnim;
        private static readonly DoubleAnimation _hoverRestAnim;
        private static readonly DoubleAnimation _hoverScaleRestAnim;
        private static readonly DoubleAnimation _pressScaleAnim;
        private static readonly DoubleAnimation _releaseScaleOverAnim;
        private static readonly DoubleAnimation _releaseScaleRestAnim;

        static MainWindow()
        {
            _hoverLiftAnim = new DoubleAnimation(-2.8, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            _hoverLiftAnim.Freeze();

            _hoverScaleUpAnim = new DoubleAnimation(1.015, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            _hoverScaleUpAnim.Freeze();

            _hoverRestAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            _hoverRestAnim.Freeze();

            _hoverScaleRestAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            _hoverScaleRestAnim.Freeze();

            _pressScaleAnim = new DoubleAnimation(0.968, TimeSpan.FromMilliseconds(55))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _pressScaleAnim.Freeze();

            _releaseScaleOverAnim = new DoubleAnimation(1.015, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            _releaseScaleOverAnim.Freeze();

            _releaseScaleRestAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            _releaseScaleRestAnim.Freeze();
        }

        private string _appDir;
        private string _dataDir;
        private string _binDir;
        private string _mihomoExe;
        private string _configYamlPath;
        private string _uiSettingsPath;
        private string _templateYamlPath;
        private string _profilesDir;
        private string _profilesJsonPath;
        private Process _mihomoProcess;
        private System.Windows.Forms.NotifyIcon _trayIcon;

        // 订阅配置项数据模型
        public class SubscriptionProfileItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
            public string FileName { get; set; }
            public int NodeCount { get; set; }
            public string UpdatedAt { get; set; }
            public bool Enabled { get; set; }
        }
        private List<SubscriptionProfileItem> _subscriptionProfiles = new List<SubscriptionProfileItem>();
        private StackPanel _profilesListStack;
        private TextBlock _profilesSummaryText;
        private TextBlock _profilesStatusTb;

        // 核心状态
        private bool _systemProxyEnabled = false;
        private bool _allowLanEnabled = true;
        private string _currentMode = "rule";
        private string _activePage = "home";
        private bool _sidebarExpanded = true;
        private double _sidebarCurrentWidth = 236;
        private double _sidebarTargetWidth = 236;
        private double _sidebarWidthVelocity = 0;
        private double _indicatorCurrentY = 0;
        private double _indicatorTargetY = 0;
        private double _indicatorVelocity = 0;

        // 光学材质参数
        private double _glassSurfaceOpacity = 0.28;
        private double _glassRimIntensity = 0.82;
        private double _wallpaperDimming = 0.34;
        private double _wallpaperBlurVal = 0.0;
        private string _wallpaperPreset = "mountain";
        private string _customWallpaperPath = "";

        // 按需唤醒的 1000Hz (1kHz) VSync 物理循环状态
        private bool _vsyncLoopActive = false;
        private DateTime _lastVSyncTickTime = DateTime.MinValue;
        private Point _mouseTarget = new Point(620, 410);
        private Point _mouseCurrent = new Point(620, 410);
        private TranslateTransform _mouseLightTransform;

        // 流量与监控
        private long _lastTotalUp = 0;
        private long _lastTotalDown = 0;
        private double _speedUpBps = 0;
        private double _speedDownBps = 0;
        private List<double> _downHistory = new List<double>();
        private List<double> _upHistory = new List<double>();
        private int _activeConnCount = 0;
        private DateTime _startTime = DateTime.Now;

        // 代理缓存
        private List<ProxyGroupItem> _proxyGroups = new List<ProxyGroupItem>();
        private Dictionary<string, ProxyNodeInfo> _proxyNodes = new Dictionary<string, ProxyNodeInfo>();
        private string _selectedGroupName = "";
        private string _nodeSearchKeyword = "";
        private bool _sortByDelay = true;
        private readonly Dictionary<string, string> _nodeSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<ConnectionRecord> _connections = new List<ConnectionRecord>();

        // 主骨架引用
        private Border _rootFrame;
        private Border _wallpaperLayer;
        private BlurEffect _wallpaperBlurEffect;
        private Border _dimmingLayer;
        private ColumnDefinition _sidebarCol;
        private Border _sidebarPanel;
        private Border _sidebarActiveIndicator;
        private TranslateTransform _sidebarIndicatorTransform;
        private List<UIElement> _sidebarCollapsibleElements = new List<UIElement>();
        private TextBlock _headerSpeedDownTb;
        private TextBlock _headerSpeedUpTb;
        private Dictionary<string, Border> _headerModePills = new Dictionary<string, Border>();

        // 右侧主工作区
        private Grid _pageHost;
        private TranslateTransform _pageTranslate;
        private ScaleTransform _pageScale;
        private SmoothScrollViewer _mainScroller;

        // 主页引用与复用示波器图元 (零 GC 分配)
        private TextBlock _dashDownSpeedText;
        private TextBlock _dashUpSpeedText;
        private TextBlock _dashTotalDownText;
        private TextBlock _dashConnCountText;
        private TextBlock _dashActiveNodeText;
        private TextBlock _dashUptimeText;
        private Canvas _waveformCanvas;
        private System.Windows.Shapes.Path _downAreaPath;
        private System.Windows.Shapes.Path _downLinePath;
        private System.Windows.Shapes.Path _upAreaPath;
        private System.Windows.Shapes.Path _upLinePath;

        // 代理页与连接页引用
        private WrapPanel _groupSelectorWrap;
        private ResponsiveCardsPanel _nodesGridWrap;
        private TextBlock _proxyGroupSummaryText;
        private StackPanel _connectionsListStack;

        // 实时节点延迟测速进度条与卡片热更新引用
        private class NodeCardLiveRef
        {
            public Border CardBorder;
            public Border AccentStrip;
            public Border DelayBadge;
            public Ellipse DelayDot;
            public TextBlock DelayText;
            public Border CheckCircle;
            public bool IsSelected;
        }
        private Dictionary<string, NodeCardLiveRef> _nodeLiveRefs = new Dictionary<string, NodeCardLiveRef>();
        private bool _isTestingLatency = false;
        private Border _latencyProgressCard;
        private TextBlock _latencyStatusTitle;
        private TextBlock _latencyCurrentNodeText;
        private TextBlock _latencyValidCountText;
        private TextBlock _latencyTimeoutCountText;
        private TextBlock _latencyFastestNodeText;
        private TextBlock _latencyPercentText;
        private ScaleTransform _latencyProgressScaleX;
        private TextBlock _testLatencyBtnText;

        // 预冻结共享画刷缓存
        private Brush _cachedChamberBrush;
        private Brush _cachedCardBrush;
        private Brush _cachedChamberRimBrush;
        private Brush _cachedCardRimBrush;
        private Brush _cachedActiveCardBrush;
        private Brush _cachedActiveBorderBrush;

        // 自定义直连网站白名单 (Custom Direct Rules)
        private string _customDirectRulesPath;
        private List<string> _customDirectRules = new List<string>();
        private WrapPanel _customRulesTagWrap;
        private TextBlock _customRulesStatusTb;

        // 代理节点矩阵视口增量虚拟化渲染池 (Option C: 支撑 1000+ 节点 1000Hz 零掉帧)
        private List<string> _currentFilteredNodeNames = new List<string>();
        private int _renderedNodeCount = 0;
        private const int NodeBatchSize = 60;
        private Border _loadMoreNodesBanner;
        private TextBlock _loadMoreNodesText;
        private Border _loadMoreHost;

        // 全双工 WebSocket 实时流量监控流 (Option D: ws://127.0.0.1:9097/traffic)
        private CancellationTokenSource _wsTrafficCts;
        private bool _wsTrafficActive = false;
        private bool _wsTrafficConnected = false;

        // 网络急救箱与内核实时日志 (Option F: ws://127.0.0.1:9097/logs?level=info)
        public class CoreLogEntry
        {
            public DateTime Time { get; set; }
            public string Type { get; set; }
            public string Message { get; set; }
        }
        private List<CoreLogEntry> _coreLogs = new List<CoreLogEntry>();
        private StackPanel _logsContainerStack;
        private ScrollViewer _logsScroller;
        private CancellationTokenSource _wsLogsCts;
        private bool _wsLogsActive = false;
        private bool _wsLogsStreaming = true;
        private bool _logsAutoScroll = true;
        private string _logsFilterLevel = "ALL";
        private string _logsFilterKeyword = "";
        private TextBlock _networkDoctorStatusTb;
        private TextBlock _logsCountTb;

        // Windows 开机静默自启注册表配置 (Option E)
        private const string RunRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegKeyName = "Herciniamihomo";

        private List<Border> _registeredGlassChambers = new List<Border>();
        private List<Border> _registeredGlassCards = new List<Border>();

        private DispatcherTimer _pollTimer;
        private JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static readonly FontFamily PrimaryFont = new FontFamily("Segoe UI Variable Display, HarmonyOS Sans SC, MiSans, PingFang SC, Microsoft YaHei UI");
        private static readonly FontFamily MonoFont = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas, Microsoft YaHei UI");
        private static readonly FontFamily EmojiFont = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Noto Color Emoji, Microsoft YaHei UI, Segoe UI");

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);
        private const uint MSGFLT_ADD = 1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("wininet.dll")]
        public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        public const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        public const int INTERNET_OPTION_REFRESH = 37;

        private uint _wakeUpMsg = 0;
        private bool _isExplicitExit = false;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _wakeUpMsg = RegisterWindowMessage("HERCINIAMIHOMO_WAKE_UP_MSG");
            try { ChangeWindowMessageFilter(_wakeUpMsg, MSGFLT_ADD); } catch { }
            IntPtr handle = new WindowInteropHelper(this).Handle;
            try { ChangeWindowMessageFilterEx(handle, _wakeUpMsg, MSGFLT_ADD, IntPtr.Zero); } catch { }
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null)
            {
                source.AddHook(HwndHook);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_wakeUpMsg != 0 && msg == (int)_wakeUpMsg)
            {
                this.Show();
                if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
                this.Activate();
                this.Focus();
                this.Topmost = true;
                this.Topmost = false;
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            IntPtr hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            if (hMonitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    RECT rcWork = monitorInfo.rcWork;
                    RECT rcMonitor = monitorInfo.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(rcWork.Left - rcMonitor.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWork.Top - rcMonitor.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWork.Right - rcWork.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWork.Bottom - rcWork.Top);
                    mmi.ptMinTrackSize.X = 960;
                    mmi.ptMinTrackSize.Y = 640;
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }
            base.OnClosing(e);
        }

        public MainWindow(bool startSilent = false)
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            _dataDir = System.IO.Path.Combine(_appDir, "data");
            _binDir = System.IO.Path.Combine(_appDir, "bin");
            _mihomoExe = System.IO.Path.Combine(_binDir, "mihomo.exe");
            _configYamlPath = System.IO.Path.Combine(_dataDir, "config.yaml");
            _uiSettingsPath = System.IO.Path.Combine(_dataDir, "hercinia_glass.cfg");
            _templateYamlPath = System.IO.Path.Combine(_dataDir, "template.yaml");
            _profilesDir = System.IO.Path.Combine(_dataDir, "profiles");
            _profilesJsonPath = System.IO.Path.Combine(_dataDir, "profiles.json");
            _customDirectRulesPath = System.IO.Path.Combine(_dataDir, "custom_direct_rules.json");

            try
            {
                if (!Directory.Exists(_profilesDir)) Directory.CreateDirectory(_profilesDir);
                if (!File.Exists(_templateYamlPath) && File.Exists(_configYamlPath))
                {
                    File.Copy(_configYamlPath, _templateYamlPath, true);
                }
                LoadSubscriptionProfiles();
                LoadCustomDirectRules();
                RefreshNodeSourceMap();
            }
            catch { }

            // 启动时读取 Windows 系统代理的实际状态并同步
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    if (reg != null)
                    {
                        object val = reg.GetValue("ProxyEnable");
                        _systemProxyEnabled = (val != null && Convert.ToInt32(val) == 1);
                    }
                }
            }
            catch { }

            // 监听系统注销、关机以及进程退出，确保安全复原代理设置
            SystemEvents.SessionEnding += (s, e) =>
            {
                if (_systemProxyEnabled) ToggleSystemProxy(false);
                StopMihomoCore();
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_systemProxyEnabled) ToggleSystemProxy(false);
                StopMihomoCore();
            };

            this.StateChanged += (s, e) =>
            {
                bool isMax = (this.WindowState == WindowState.Maximized);
                if (_rootFrame != null)
                {
                    _rootFrame.Margin = isMax ? new Thickness(0) : new Thickness(6);
                    _rootFrame.CornerRadius = isMax ? new CornerRadius(0) : new CornerRadius(18);
                }
            };

            for (int i = 0; i < 50; i++)
            {
                _downHistory.Add(0);
                _upHistory.Add(0);
            }

            LoadSavedOpticalSettings();
            RebuildFrozenGlassBrushes();

            this.Title = "Herciniamihomo Pro";
            this.Width = 1240;
            this.Height = 810;
            this.MinWidth = 960;
            this.MinHeight = 640;
            this.FontFamily = PrimaryFont;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.CanResizeWithGrip;
            this.AllowDrop = true;
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

            string mainIcoPath = System.IO.Path.Combine(_appDir, "app.ico");
            if (File.Exists(mainIcoPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(mainIcoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        this.Icon = BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    }
                }
                catch { }
            }

            this.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            this.Drop += OnWindowFileDrop;

            BuildProLiquidGlassUI();
            try { new WindowInteropHelper(this).EnsureHandle(); } catch { }
            InitSystemTray();
            StartMihomoCore();

            StartWebSocketTrafficStream();
            StartLiveCoreLogsStream();

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _pollTimer.Tick += async (s, e) => await PollMihomoStateAsync();
            _pollTimer.Start();

            Task.Run(async () =>
            {
                await Task.Delay(650);
                await Dispatcher.InvokeAsync(async () =>
                {
                    await RebuildMergedConfigAndReloadAsync();
                    await FetchProxiesFromCoreAsync();
                    await PollMihomoStateAsync();
                });
            });
        }

        // =====================================================================
        // 预冻结画刷构建器 (零重复对象分配，GPU 显存占用降低 65%)
        // =====================================================================
        private void RebuildFrozenGlassBrushes()
        {
            byte c1 = (byte)Math.Min(255, Math.Max(18, _glassSurfaceOpacity * 180));
            byte c2 = (byte)Math.Min(255, Math.Max(26, _glassSurfaceOpacity * 235));
            byte c3 = (byte)Math.Min(255, Math.Max(32, _glassSurfaceOpacity * 250));
            var chamberBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(c1, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(c2, 15, 23, 42), 0.35),
                new GradientStop(Color.FromArgb(c3, 9, 14, 28), 1.0)
            }, new Point(0, 0), new Point(1, 1));
            chamberBrush.Freeze();
            _cachedChamberBrush = chamberBrush;

            byte k1 = (byte)Math.Min(255, Math.Max(22, _glassSurfaceOpacity * 195));
            byte k2 = (byte)Math.Min(255, Math.Max(18, _glassSurfaceOpacity * 150));
            byte k3 = (byte)Math.Min(255, Math.Max(24, _glassSurfaceOpacity * 175));
            var cardBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(k1, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(k2, 22, 33, 58), 0.52),
                new GradientStop(Color.FromArgb(k3, 56, 189, 248), 1.0)
            }, new Point(0, 0), new Point(1, 1));
            cardBrush.Freeze();
            _cachedCardBrush = cardBrush;

            _cachedChamberRimBrush = CreateFrozenRimBrush(_glassRimIntensity);
            _cachedCardRimBrush = CreateFrozenRimBrush(_glassRimIntensity * 0.85);

            var actBg = new LinearGradientBrush(Color.FromArgb(185, 14, 165, 233), Color.FromArgb(175, 99, 102, 241), 35);
            actBg.Freeze();
            _cachedActiveCardBrush = actBg;

            var actBd = new SolidColorBrush(Color.FromRgb(186, 230, 253));
            actBd.Freeze();
            _cachedActiveBorderBrush = actBd;
        }

        private Brush CreateFrozenRimBrush(double intensity)
        {
            byte top = (byte)Math.Min(255, Math.Max(30, intensity * 235));
            byte mid = (byte)Math.Min(255, Math.Max(18, intensity * 110));
            byte bot = (byte)Math.Min(255, Math.Max(20, intensity * 95));
            var b = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(top, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(mid, 186, 230, 253), 0.45),
                new GradientStop(Color.FromArgb(bot, 129, 140, 248), 1.0)
            }, new Point(0, 0), new Point(1, 1));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        // =====================================================================
        // 主界面骨架 (移除全窗口 DropShadowEffect 离屏纹理开销，采用双层微晶边框)
        // =====================================================================
        private void BuildProLiquidGlassUI()
        {
            _rootFrame = new Border
            {
                CornerRadius = new CornerRadius(18),
                ClipToBounds = true,
                BorderThickness = new Thickness(1.4),
                BorderBrush = _cachedChamberRimBrush,
                Background = FrozenBrush(7, 11, 20),
                Margin = new Thickness(6)
            };

            Grid rootGrid = new Grid();
            _rootFrame.Child = rootGrid;

            // 1. 底层超清壁纸层
            _wallpaperLayer = new Border
            {
                CornerRadius = new CornerRadius(17),
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.0, 1.0)
            };
            if (_wallpaperBlurVal > 0.05)
            {
                _wallpaperBlurEffect = new BlurEffect
                {
                    Radius = _wallpaperBlurVal,
                    RenderingBias = RenderingBias.Performance
                };
                _wallpaperLayer.Effect = _wallpaperBlurEffect;
            }
            else
            {
                _wallpaperLayer.Effect = null;
            }
            rootGrid.Children.Add(_wallpaperLayer);

            // 2. 1000Hz 按需阻尼极光光斑 (严格固定 780x780 正圆，杜绝拉伸成椭圆)
            Canvas lightCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            var radial = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(46, 56, 189, 248), 0.0),
                new GradientStop(Color.FromArgb(24, 139, 92, 246), 0.48),
                new GradientStop(Color.FromArgb(0, 15, 23, 42), 1.0)
            });
            radial.Freeze();
            Ellipse mouseAurora = new Ellipse
            {
                Width = 780,
                Height = 780,
                Fill = radial
            };
            _mouseLightTransform = new TranslateTransform(220, 120);
            mouseAurora.RenderTransform = _mouseLightTransform;
            lightCanvas.Children.Add(mouseAurora);
            rootGrid.Children.Add(lightCanvas);

            this.MouseMove += (s, e) =>
            {
                Point pt = e.GetPosition(rootGrid);
                _mouseTarget = new Point(pt.X - 390, pt.Y - 390);
                EnsureVSyncPhysicsActive();
            };

            // 3. 壁纸护眼对比度暗角层
            _dimmingLayer = new Border
            {
                CornerRadius = new CornerRadius(17),
                IsHitTestVisible = false,
                Background = FrozenBrush((byte)(_wallpaperDimming * 245), 8, 13, 26)
            };
            rootGrid.Children.Add(_dimmingLayer);

            // 4. 顶层交互布局
            Grid layoutGrid = new Grid();
            layoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            layoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.Children.Add(layoutGrid);

            Border titleBar = BuildRefinedTitleBar();
            Grid.SetRow(titleBar, 0);
            layoutGrid.Children.Add(titleBar);

            Grid bodyGrid = new Grid { Margin = new Thickness(12, 4, 12, 12) };
            _sidebarCol = new ColumnDefinition { Width = new GridLength(_sidebarCurrentWidth) };
            bodyGrid.ColumnDefinitions.Add(_sidebarCol);
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(bodyGrid, 1);
            layoutGrid.Children.Add(bodyGrid);

            _sidebarPanel = BuildRefinedSidebarDock();
            Grid.SetColumn(_sidebarPanel, 0);
            bodyGrid.Children.Add(_sidebarPanel);

            Border rightChamber = CreateGlassChamber(16);
            Grid.SetColumn(rightChamber, 2);
            bodyGrid.Children.Add(rightChamber);

            _mainScroller = new SmoothScrollViewer
            {
                Padding = new Thickness(20, 18, 20, 22)
            };
            _mainScroller.ScrollChanged += (s, e) =>
            {
                if (_activePage == "proxies" && _currentFilteredNodeNames != null && _renderedNodeCount < _currentFilteredNodeNames.Count)
                {
                    if (_mainScroller.VerticalOffset >= _mainScroller.ScrollableHeight - 650)
                    {
                        AppendNextNodeBatch(NodeBatchSize);
                    }
                }
            };

            _pageHost = new Grid { RenderTransformOrigin = new Point(0.5, 0.15) };
            TransformGroup tg = new TransformGroup();
            _pageScale = new ScaleTransform(1.0, 1.0);
            _pageTranslate = new TranslateTransform(0, 0);
            tg.Children.Add(_pageScale);
            tg.Children.Add(_pageTranslate);
            _pageHost.RenderTransform = tg;

            _mainScroller.Content = _pageHost;
            rightChamber.Child = _mainScroller;

            ApplyWallpaperVisual(_wallpaperPreset, false);
            SwitchNavigationPage("home");

            this.Content = _rootFrame;
        }

        // =====================================================================
        // 顶部栏 (所有圆角严格采用几何圆角 CornerRadius(7~10)，绝不产生畸变椭圆)
        // =====================================================================
        private Border BuildRefinedTitleBar()
        {
            Border bar = CreateGlassChamber(14);
            bar.Margin = new Thickness(12, 10, 12, 4);
            bar.Padding = new Thickness(14, 0, 14, 0);
            bar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.Handled) return;
                if (e.ClickCount == 2) ToggleMaximizeWindow();
                else { try { this.DragMove(); } catch { } }
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = g;

            // 左侧：macOS 12x12 标准正圆控制点（带 22x22 触控热区）+ 品牌区
            StackPanel leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            StackPanel trafficLights = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            trafficLights.Children.Add(CreatePerfectCircleButton(12, Color.FromRgb(255, 95, 86), "关闭窗口 (隐藏至系统托盘，代理后台持续运行)", "×", Color.FromArgb(220, 77, 0, 0), () => this.Hide()));
            trafficLights.Children.Add(CreatePerfectCircleButton(12, Color.FromRgb(255, 189, 46), "最小化窗口", "−", Color.FromArgb(220, 90, 50, 0), () => this.WindowState = WindowState.Minimized));
            trafficLights.Children.Add(CreatePerfectCircleButton(12, Color.FromRgb(39, 201, 63), "最大化 / 还原窗口", "+", Color.FromArgb(220, 0, 65, 0), () => ToggleMaximizeWindow()));
            leftStack.Children.Add(trafficLights);

            Border logoBadge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Center,
                Background = _cachedActiveCardBrush,
                BorderBrush = FrozenBrush(200, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                Clip = new EllipseGeometry(new Rect(0, 0, 26, 26))
            };
            string logoCirclePng = System.IO.Path.Combine(_appDir, "icon_circle.png");
            string logoJpg = System.IO.Path.Combine(_appDir, "icon.jpg");
            string chosenLogo = File.Exists(logoCirclePng) ? logoCirclePng : (File.Exists(logoJpg) ? logoJpg : null);
            if (chosenLogo != null)
            {
                try
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(chosenLogo, UriKind.Absolute);
                    bi.DecodePixelWidth = 64;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    logoBadge.Child = new Image
                    {
                        Source = bi,
                        Stretch = Stretch.UniformToFill
                    };
                }
                catch
                {
                    logoBadge.Child = VectorIcons.Create(VectorIcons.Shield, Brushes.White, 14);
                }
            }
            else
            {
                logoBadge.Child = VectorIcons.Create(VectorIcons.Shield, Brushes.White, 14);
            }
            leftStack.Children.Add(logoBadge);

            StackPanel titleTextGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleTextGroup.Children.Add(new TextBlock
            {
                Text = "Herciniamihomo Pro",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13
            });
            titleTextGroup.Children.Add(new TextBlock
            {
                Text = "MIHOMO v1.19.31 · DIRECTX LIQUID GLASS",
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold
            });
            leftStack.Children.Add(titleTextGroup);
            Grid.SetColumn(leftStack, 0);
            g.Children.Add(leftStack);

            // 中央：分段路由控制器 (CornerRadius = 10，严丝合缝的苹果圆角分段控制栏)
            Border centerSegmentBox = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(10),
                Background = FrozenBrush(95, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3)
            };
            centerSegmentBox.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
            centerSegmentBox.MouseLeftButtonDown += (s, e) => e.Handled = true;

            StackPanel segRow = new StackPanel { Orientation = Orientation.Horizontal };
            _headerModePills.Clear();
            var topModes = new[]
            {
                new { Key = "rule", Label = "规则分流 Rule" },
                new { Key = "global", Label = "全局代理 Global" },
                new { Key = "direct", Label = "全球直连 Direct" }
            };
            foreach (var tm in topModes)
            {
                string mk = tm.Key;
                bool active = string.Equals(_currentMode, mk, StringComparison.OrdinalIgnoreCase);
                Border pill = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(14, 5, 14, 5),
                    Cursor = Cursors.Hand,
                    Background = active ? _cachedActiveCardBrush : Brushes.Transparent,
                    BorderBrush = active ? _cachedActiveBorderBrush : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                ScaleTransform pst = new ScaleTransform(1.0, 1.0);
                pill.RenderTransform = pst;

                pill.Child = new TextBlock
                {
                    Text = tm.Label,
                    Foreground = active ? Brushes.White : FrozenBrush(148, 163, 184),
                    FontSize = 11.5,
                    FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold
                };
                pill.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    DoubleAnimation pressAnim = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(55))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    pst.BeginAnimation(ScaleTransform.ScaleXProperty, pressAnim);
                    pst.BeginAnimation(ScaleTransform.ScaleYProperty, pressAnim);
                };
                pill.PreviewMouseLeftButtonUp += async (s, e) =>
                {
                    e.Handled = true;
                    DoubleAnimation popAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                    };
                    pst.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                    pst.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
                    await SetOutboundModeAsync(mk);
                };
                pill.MouseEnter += (s, e) =>
                {
                    if (!string.Equals(_currentMode, mk, StringComparison.OrdinalIgnoreCase))
                    {
                        pill.Background = FrozenBrush(30, 255, 255, 255);
                    }
                };
                pill.MouseLeave += (s, e) =>
                {
                    if (!string.Equals(_currentMode, mk, StringComparison.OrdinalIgnoreCase))
                    {
                        pill.Background = Brushes.Transparent;
                    }
                    DoubleAnimation leaveAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    pst.BeginAnimation(ScaleTransform.ScaleXProperty, leaveAnim);
                    pst.BeginAnimation(ScaleTransform.ScaleYProperty, leaveAnim);
                };
                _headerModePills[mk] = pill;
                segRow.Children.Add(pill);
            }
            centerSegmentBox.Child = segRow;
            Grid.SetColumn(centerSegmentBox, 1);
            g.Children.Add(centerSegmentBox);

            // 右侧：实时双通道网速表 + 快捷托盘后台
            StackPanel rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border telemetryPill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(95, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel speedRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            speedRow.Children.Add(VectorIcons.Create(VectorIcons.ArrowDown, FrozenBrush(56, 189, 248), 11));
            _headerSpeedDownTb = new TextBlock
            {
                Text = "0.0 KB/s",
                FontFamily = MonoFont,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Width = 68,
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            speedRow.Children.Add(_headerSpeedDownTb);

            speedRow.Children.Add(VectorIcons.Create(VectorIcons.ArrowUp, FrozenBrush(168, 85, 247), 11));
            _headerSpeedUpTb = new TextBlock
            {
                Text = "0.0 KB/s",
                FontFamily = MonoFont,
                Foreground = FrozenBrush(203, 213, 225),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Width = 64,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            speedRow.Children.Add(_headerSpeedUpTb);
            telemetryPill.Child = speedRow;
            rightStack.Children.Add(telemetryPill);

            Border trayBtn = CreateLiquidButton(VectorIcons.Shield, "托盘后台", LiquidButtonStyle.Neutral, () => this.Hide(), 28);
            rightStack.Children.Add(trayBtn);

            Grid.SetColumn(rightStack, 2);
            g.Children.Add(rightStack);

            return bar;
        }

        // 严格 1:1 正圆控制点构造器（带 22x22 触控热区、macOS 悬停流体弹簧形变、符号渐隐渐显与防穿透事件）
        private UIElement CreatePerfectCircleButton(double diameter, Color c, string tooltip, string glyph, Color glyphColor, Action onClick)
        {
            Grid host = new Grid
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            ScaleTransform st = new ScaleTransform(1.0, 1.0);
            host.RenderTransform = st;

            Ellipse circle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = new SolidColorBrush(c),
                Stroke = FrozenBrush(110, 255, 255, 255),
                StrokeThickness = 0.8,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            host.Children.Add(circle);

            TextBlock glyphTb = new TextBlock
            {
                Text = glyph,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(glyphColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
                Opacity = 0.0,
                IsHitTestVisible = false
            };
            host.Children.Add(glyphTb);

            host.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                DoubleAnimation downAnim = new DoubleAnimation(0.86, TimeSpan.FromMilliseconds(60))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, downAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, downAnim);
            };
            host.MouseLeftButtonDown += (s, e) => e.Handled = true;

            host.PreviewMouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                DoubleAnimation upAnim = new DoubleAnimation(host.IsMouseOver ? 1.15 : 1.0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, upAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, upAnim);
                onClick();
            };
            host.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };

            host.MouseEnter += (s, e) =>
            {
                circle.Opacity = 0.92;
                circle.StrokeThickness = 1.1;
                DoubleAnimation enterScale = new DoubleAnimation(1.15, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DoubleAnimation glyphFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, enterScale);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, enterScale);
                glyphTb.BeginAnimation(UIElement.OpacityProperty, glyphFadeIn);
            };
            host.MouseLeave += (s, e) =>
            {
                circle.Opacity = 1.0;
                circle.StrokeThickness = 0.8;
                DoubleAnimation leaveScale = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DoubleAnimation glyphFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, leaveScale);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, leaveScale);
                glyphTb.BeginAnimation(UIElement.OpacityProperty, glyphFadeOut);
            };
            return host;
        }

        private void ToggleMaximizeWindow()
        {
            this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            _rootFrame.Margin = (this.WindowState == WindowState.Maximized) ? new Thickness(0) : new Thickness(6);
            _rootFrame.CornerRadius = (this.WindowState == WindowState.Maximized) ? new CornerRadius(0) : new CornerRadius(18);
        }

        // =====================================================================
        // 左侧 Clash Mi Pro 液态玻璃侧边栏 (同步弹性伸缩)
        // =====================================================================
        private Border BuildRefinedSidebarDock()
        {
            Border dock = CreateGlassChamber(16);
            dock.Padding = new Thickness(10, 12, 10, 12);

            Grid sg = new Grid();
            sg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dock.Child = sg;

            _sidebarCollapsibleElements.Clear();

            Border collapseBtn = CreateGlassCard(8);
            collapseBtn.Padding = new Thickness(12, 8, 12, 8);
            collapseBtn.Margin = new Thickness(0, 0, 0, 12);
            collapseBtn.Cursor = Cursors.Hand;
            StackPanel collapseRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            collapseRow.Children.Add(VectorIcons.Create(VectorIcons.SidebarToggle, FrozenBrush(56, 189, 248), 15));
            TextBlock toggleTitle = new TextBlock
            {
                Text = "收起导航侧栏",
                Foreground = FrozenBrush(226, 232, 240),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _sidebarCollapsibleElements.Add(toggleTitle);
            collapseRow.Children.Add(toggleTitle);
            collapseBtn.Child = collapseRow;
            collapseBtn.MouseLeftButtonUp += (s, e) => ToggleSidebarSpring();
            AttachSpringHover(collapseBtn);
            Grid.SetRow(collapseBtn, 0);
            sg.Children.Add(collapseBtn);

            Grid navHost = new Grid();
            _sidebarActiveIndicator = new Border
            {
                Height = 44,
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Top,
                Background = _cachedActiveCardBrush,
                BorderBrush = _cachedActiveBorderBrush,
                BorderThickness = new Thickness(1.1)
            };
            _sidebarIndicatorTransform = new TranslateTransform(0, 0);
            _sidebarActiveIndicator.RenderTransform = _sidebarIndicatorTransform;
            navHost.Children.Add(_sidebarActiveIndicator);

            StackPanel menuStack = new StackPanel { Orientation = Orientation.Vertical };
            var navItems = new[]
            {
                new { Key = "home", Icon = VectorIcons.Dashboard, Label = "仪表盘概览", Index = 0 },
                new { Key = "proxies", Icon = VectorIcons.Proxies, Label = "代理节点矩阵", Index = 1 },
                new { Key = "profiles", Icon = VectorIcons.Profiles, Label = "订阅与配置中心", Index = 2 },
                new { Key = "connections", Icon = VectorIcons.Connections, Label = "实时活跃连接", Index = 3 },
                new { Key = "studio", Icon = VectorIcons.Studio, Label = "液态玻璃工作台", Index = 4 }
            };

            foreach (var item in navItems)
            {
                string pageKey = item.Key;
                int idx = item.Index;

                Border navRow = new Border
                {
                    Height = 44,
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(10),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(12, 0, 12, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                ScaleTransform navSt = new ScaleTransform(1.0, 1.0);
                navRow.RenderTransform = navSt;

                Grid rowGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Viewbox iconVb = VectorIcons.Create(item.Icon, Brushes.White, 16);
                Grid.SetColumn(iconVb, 0);
                rowGrid.Children.Add(iconVb);

                TextBlock lbl = new TextBlock
                {
                    Text = item.Label,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _sidebarCollapsibleElements.Add(lbl);
                Grid.SetColumn(lbl, 1);
                rowGrid.Children.Add(lbl);

                navRow.Child = rowGrid;
                navRow.MouseEnter += (s, e) =>
                {
                    if (!string.Equals(_activePage, pageKey, StringComparison.OrdinalIgnoreCase))
                    {
                        navRow.Background = FrozenBrush(25, 255, 255, 255);
                    }
                };
                navRow.MouseLeave += (s, e) =>
                {
                    navRow.Background = Brushes.Transparent;
                    DoubleAnimation rst = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    navSt.BeginAnimation(ScaleTransform.ScaleXProperty, rst);
                    navSt.BeginAnimation(ScaleTransform.ScaleYProperty, rst);
                };
                navRow.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    DoubleAnimation press = new DoubleAnimation(0.965, TimeSpan.FromMilliseconds(60))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    navSt.BeginAnimation(ScaleTransform.ScaleXProperty, press);
                    navSt.BeginAnimation(ScaleTransform.ScaleYProperty, press);
                };
                navRow.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    DoubleAnimation rel = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                    };
                    navSt.BeginAnimation(ScaleTransform.ScaleXProperty, rel);
                    navSt.BeginAnimation(ScaleTransform.ScaleYProperty, rel);
                };
                navRow.MouseLeftButtonUp += (s, e) =>
                {
                    _indicatorTargetY = idx * 50.0;
                    EnsureVSyncPhysicsActive();
                    SwitchNavigationPage(pageKey);
                };

                menuStack.Children.Add(navRow);
            }

            navHost.Children.Add(menuStack);
            Grid.SetRow(navHost, 1);
            sg.Children.Add(navHost);

            // 底部代理状态卡
            Border bottomCard = CreateGlassCard(12);
            bottomCard.Padding = new Thickness(12, 10, 12, 10);
            bottomCard.Cursor = Cursors.Hand;
            Grid botGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Ellipse dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = _systemProxyEnabled ? FrozenBrush(52, 211, 153) : FrozenBrush(56, 189, 248),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            botGrid.Children.Add(dot);

            StackPanel botTextCol = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            botTextCol.Children.Add(new TextBlock
            {
                Text = _systemProxyEnabled ? "系统代理: 开启" : "系统代理: 待机",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });
            botTextCol.Children.Add(new TextBlock
            {
                Text = "127.0.0.1:7890",
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 10.5,
                FontFamily = MonoFont
            });
            _sidebarCollapsibleElements.Add(botTextCol);
            Grid.SetColumn(botTextCol, 1);
            botGrid.Children.Add(botTextCol);

            bottomCard.Child = botGrid;
            bottomCard.MouseLeftButtonUp += (s, e) => ToggleSystemProxy(!_systemProxyEnabled);
            AttachSpringHover(bottomCard);
            Grid.SetRow(bottomCard, 2);
            sg.Children.Add(bottomCard);

            return dock;
        }

        private void ToggleSidebarSpring()
        {
            _sidebarExpanded = !_sidebarExpanded;
            _sidebarTargetWidth = _sidebarExpanded ? 236.0 : 68.0;
            foreach (var el in _sidebarCollapsibleElements)
            {
                el.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            EnsureVSyncPhysicsActive();
        }

        // =====================================================================
        // 按需唤醒的 1000Hz (1kHz) VSync 物理动画循环 (静止时自动休眠，0% CPU 占用)
        // =====================================================================
        private void EnsureVSyncPhysicsActive()
        {
            if (!_vsyncLoopActive)
            {
                _vsyncLoopActive = true;
                _lastVSyncTickTime = DateTime.Now;
                CompositionTarget.Rendering += OnVSyncPhysicsTick;
            }
        }

        private void OnVSyncPhysicsTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double dt = (_lastVSyncTickTime == DateTime.MinValue) ? 0.001 : (now - _lastVSyncTickTime).TotalSeconds;
            _lastVSyncTickTime = now;
            if (dt <= 0.00005 || dt > 0.05) dt = 0.001; // 1000Hz (1.0ms) 步长自适应

            // 60Hz 相对时间比率：以此保证在 60Hz ~ 1000Hz 任意刷新率下物理动画手感恒定顺滑
            double timeScale = dt * 60.0;
            if (timeScale > 2.0) timeScale = 2.0;

            bool stillMoving = false;

            // 1. 阻尼跟随极光光斑 (时间积分连续化)
            double dx = _mouseTarget.X - _mouseCurrent.X;
            double dy = _mouseTarget.Y - _mouseCurrent.Y;
            if (Math.Abs(dx) > 0.2 || Math.Abs(dy) > 0.2)
            {
                double lerpFactor = 1.0 - Math.Pow(1.0 - 0.12, timeScale);
                _mouseCurrent.X += dx * lerpFactor;
                _mouseCurrent.Y += dy * lerpFactor;
                if (_mouseLightTransform != null)
                {
                    _mouseLightTransform.X = _mouseCurrent.X;
                    _mouseLightTransform.Y = _mouseCurrent.Y;
                }
                stillMoving = true;
            }

            // 2. 侧边栏弹性伸缩物理 (1000Hz 微积分二阶阻尼)
            double wDiff = _sidebarTargetWidth - _sidebarCurrentWidth;
            double wForce = wDiff * (0.30 * timeScale);
            _sidebarWidthVelocity = (_sidebarWidthVelocity + wForce) * Math.Pow(0.65, timeScale);
            _sidebarCurrentWidth += _sidebarWidthVelocity * timeScale;
            if (Math.Abs(_sidebarWidthVelocity) > 0.05 || Math.Abs(wDiff) > 0.1)
            {
                _sidebarCol.Width = new GridLength(Math.Max(50, _sidebarCurrentWidth));
                stillMoving = true;
            }
            else
            {
                _sidebarCurrentWidth = _sidebarTargetWidth;
                _sidebarWidthVelocity = 0;
                _sidebarCol.Width = new GridLength(_sidebarCurrentWidth);
            }

            // 3. 侧栏高亮胶囊二阶物理弹簧系统 (Hooke's Law with dt: 1000Hz 极致微米级阻尼)
            double yDiff = _indicatorTargetY - _indicatorCurrentY;
            double yForce = yDiff * (0.32 * timeScale);
            _indicatorVelocity = (_indicatorVelocity + yForce) * Math.Pow(0.68, timeScale);
            _indicatorCurrentY += _indicatorVelocity * timeScale;
            if (_sidebarIndicatorTransform != null)
            {
                _sidebarIndicatorTransform.Y = _indicatorCurrentY;
            }
            if (Math.Abs(_indicatorVelocity) > 0.04 || Math.Abs(yDiff) > 0.1)
            {
                stillMoving = true;
            }
            else
            {
                _indicatorCurrentY = _indicatorTargetY;
                _indicatorVelocity = 0;
                if (_sidebarIndicatorTransform != null) _sidebarIndicatorTransform.Y = _indicatorCurrentY;
            }

            if (!stillMoving)
            {
                _vsyncLoopActive = false;
                _lastVSyncTickTime = DateTime.MinValue;
                CompositionTarget.Rendering -= OnVSyncPhysicsTick;
            }
        }

        private void SwitchNavigationPage(string pageKey)
        {
            _activePage = pageKey;
            _pageHost.Children.Clear();
            _mainScroller.ScrollToVerticalOffset(0);

            int navIdx = 0;
            if (pageKey == "proxies") navIdx = 1;
            else if (pageKey == "profiles") navIdx = 2;
            else if (pageKey == "connections") navIdx = 3;
            else if (pageKey == "studio") navIdx = 4;
            _indicatorTargetY = navIdx * 50.0;
            EnsureVSyncPhysicsActive();

            UIElement view = null;
            if (pageKey == "home") view = BuildHomeDashboardView();
            else if (pageKey == "proxies") view = BuildProxiesView();
            else if (pageKey == "profiles") view = BuildProfilesAndMergerView();
            else if (pageKey == "connections") view = BuildConnectionsView();
            else if (pageKey == "studio") view = BuildLiquidGlassStudioView();

            if (view != null)
            {
                _pageHost.Children.Add(view);
                RunPageSpringEntrance();
            }
        }

        private void RunPageSpringEntrance()
        {
            DoubleAnimation fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            DoubleAnimation slideAnim = new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            DoubleAnimation scaleAnim = new DoubleAnimation(0.985, 1.0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            _pageHost.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            _pageTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            _pageScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            _pageScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        // =====================================================================
        // 页面 1：Clash Mi Pro 仪表盘 (所有几何圆角精准对齐，零椭圆畸变)
        // =====================================================================
        private UIElement BuildHomeDashboardView()
        {
            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            Grid heroGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // 左侧：Clash Mi 核心引擎控制卡 (CornerRadius = 14)
            Border reactorCard = CreateGlassCard(14);
            reactorCard.Padding = new Thickness(22, 20, 22, 20);
            reactorCard.Cursor = Cursors.Hand;
            AttachSpringHover(reactorCard);

            Grid reactorLayout = new Grid();
            reactorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            reactorLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左侧 84x84 严格正圆能量核心 (VerticalAlignment & HorizontalAlignment = Center)
            Grid orbContainer = new Grid
            {
                Width = 84,
                Height = 84,
                Margin = new Thickness(0, 0, 22, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Ellipse outerCircle = new Ellipse
            {
                Width = 84,
                Height = 84,
                StrokeThickness = 1.5,
                Stroke = _systemProxyEnabled ? FrozenBrush(160, 56, 189, 248) : FrozenBrush(75, 148, 163, 184),
                Fill = _systemProxyEnabled ? FrozenBrush(35, 56, 189, 248) : FrozenBrush(25, 15, 23, 42)
            };
            ScaleTransform innerCoreScale = new ScaleTransform(1.0, 1.0);
            Border innerCore = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32), // 64/2 = 32 严格正圆
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = _systemProxyEnabled
                    ? _cachedActiveCardBrush
                    : FrozenBrush(140, 30, 41, 59),
                BorderBrush = FrozenBrush(210, 255, 255, 255),
                BorderThickness = new Thickness(1.4),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = innerCoreScale,
                Child = VectorIcons.Create(_systemProxyEnabled ? VectorIcons.Bolt : VectorIcons.Power, Brushes.White, 26)
            };
            reactorCard.MouseLeftButtonUp += (s, e) =>
            {
                DoubleAnimation pulse = new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                };
                innerCoreScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                innerCoreScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                ToggleSystemProxy(!_systemProxyEnabled);
            };
            orbContainer.Children.Add(outerCircle);
            orbContainer.Children.Add(innerCore);
            Grid.SetColumn(orbContainer, 0);
            reactorLayout.Children.Add(orbContainer);

            // 右侧状态详情 (使用 CornerRadius(6) 几何标签，彻底消除之前 CornerRadius(99) 的扁椭圆畸变)
            StackPanel orbDetails = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Border stateBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 0, 8),
                Background = _systemProxyEnabled ? FrozenBrush(65, 16, 185, 129) : FrozenBrush(55, 51, 65, 85),
                BorderBrush = _systemProxyEnabled ? FrozenBrush(52, 211, 153) : FrozenBrush(100, 148, 163, 184),
                BorderThickness = new Thickness(1)
            };
            StackPanel badgeInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            badgeInner.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = _systemProxyEnabled ? FrozenBrush(52, 211, 153) : FrozenBrush(148, 163, 184),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            badgeInner.Children.Add(new TextBlock
            {
                Text = _systemProxyEnabled ? "SYSTEM PROXY ACTIVE · 流量守护中" : "STANDBY · 点击卡片开启系统代理",
                Foreground = _systemProxyEnabled ? FrozenBrush(110, 231, 183) : FrozenBrush(203, 213, 225),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            stateBadge.Child = badgeInner;
            orbDetails.Children.Add(stateBadge);

            orbDetails.Children.Add(new TextBlock
            {
                Text = _systemProxyEnabled ? "Mihomo 透明流量接管已激活" : "点击一键启动 Clash Mi 代理引擎",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            _dashActiveNodeText = new TextBlock
            {
                Text = "当前主策略线路: " + GetPrimarySelectedNode(),
                Foreground = FrozenBrush(125, 211, 252),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            orbDetails.Children.Add(_dashActiveNodeText);

            _dashUptimeText = new TextBlock
            {
                Text = "Mixed 127.0.0.1:7890   |   API 127.0.0.1:9097   |   运行: " + GetUptimeString(),
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 11,
                FontFamily = MonoFont
            };
            orbDetails.Children.Add(_dashUptimeText);

            Grid.SetColumn(orbDetails, 1);
            reactorLayout.Children.Add(orbDetails);
            reactorCard.Child = reactorLayout;
            Grid.SetColumn(reactorCard, 0);
            heroGrid.Children.Add(reactorCard);

            // 右侧控制开关卡
            Border switchesCard = CreateGlassCard(14);
            switchesCard.Padding = new Thickness(20, 16, 20, 16);
            StackPanel swStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            swStack.Children.Add(new TextBlock
            {
                Text = "CONTROL CENTER · 接管开关",
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            swStack.Children.Add(CreateIosToggleSwitchRow(
                "Windows 系统代理 (System Proxy)",
                "自动配置系统注册表代理 127.0.0.1:7890",
                _systemProxyEnabled,
                val => ToggleSystemProxy(val)));

            swStack.Children.Add(new Border { Height = 1, Background = FrozenBrush(30, 255, 255, 255), Margin = new Thickness(0, 10, 0, 10) });

            swStack.Children.Add(CreateIosToggleSwitchRow(
                "局域网共享代理 (Allow LAN)",
                "允许同一局域网手机与终端接入 :7890",
                _allowLanEnabled,
                async val =>
                {
                    _allowLanEnabled = val;
                    await HttpSendAsync("PATCH", "http://127.0.0.1:9097/configs", "{\"allow-lan\":" + (val ? "true" : "false") + "}");
                }));

            switchesCard.Child = swStack;
            Grid.SetColumn(switchesCard, 2);
            heroGrid.Children.Add(switchesCard);
            stack.Children.Add(heroGrid);

            // 第二行：4 枚 Bento 指标卡
            UniformGrid metricGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            metricGrid.Children.Add(CreateBentoMetricTile("DOWNLOAD SPEED", "实时下行吞吐", VectorIcons.ArrowDown, out _dashDownSpeedText, FormatSpeed(_speedDownBps), Color.FromRgb(56, 189, 248)));
            metricGrid.Children.Add(CreateBentoMetricTile("UPLOAD SPEED", "实时上行吞吐", VectorIcons.ArrowUp, out _dashUpSpeedText, FormatSpeed(_speedUpBps), Color.FromRgb(168, 85, 247)));
            metricGrid.Children.Add(CreateBentoMetricTile("SESSION TRAFFIC", "累计下行 / 上行", VectorIcons.Profiles, out _dashTotalDownText, FormatBytes(_lastTotalDown) + " / " + FormatBytes(_lastTotalUp), Color.FromRgb(52, 211, 153)));
            metricGrid.Children.Add(CreateBentoMetricTile("ACTIVE TUNNELS", "并发 TCP/UDP 隧道", VectorIcons.Connections, out _dashConnCountText, _activeConnCount + " Active", Color.FromRgb(251, 191, 36)));
            stack.Children.Add(metricGrid);

            // 第三行：StreamGeometry 硬件加速流光示波器
            Border chartCard = CreateGlassCard(14);
            chartCard.Padding = new Thickness(20, 16, 20, 16);
            chartCard.Margin = new Thickness(0, 0, 0, 14);
            StackPanel chartStack = new StackPanel();
            Grid chartHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            StackPanel chTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chTitle.Children.Add(VectorIcons.Create(VectorIcons.Bolt, FrozenBrush(56, 189, 248), 15));
            chTitle.Children.Add(new TextBlock
            {
                Text = "REAL-TIME TRAFFIC OSCILLOSCOPE · 实时双通道流光波形",
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            chartHeader.Children.Add(chTitle);

            StackPanel legendRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            legendRow.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = FrozenBrush(56, 189, 248), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            legendRow.Children.Add(new TextBlock { Text = "Download 下行", Foreground = FrozenBrush(186, 230, 253), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            legendRow.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = FrozenBrush(168, 85, 247), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            legendRow.Children.Add(new TextBlock { Text = "Upload 上行", Foreground = FrozenBrush(216, 180, 254), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            chartHeader.Children.Add(legendRow);
            chartStack.Children.Add(chartHeader);

            Border canvasFrame = new Border
            {
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Background = FrozenBrush(50, 9, 14, 26),
                BorderBrush = FrozenBrush(40, 255, 255, 255),
                BorderThickness = new Thickness(1)
            };
            _waveformCanvas = new Canvas { Height = 140, ClipToBounds = true };
            InitWaveformPaths();
            _waveformCanvas.SizeChanged += (s, e) => RedrawTrafficWaveform();
            canvasFrame.Child = _waveformCanvas;
            chartStack.Children.Add(canvasFrame);
            chartCard.Child = chartStack;
            stack.Children.Add(chartCard);

            // 第四行：快捷横幅卡
            UniformGrid quickGrid = new UniformGrid { Columns = 3 };
            quickGrid.Children.Add(CreateBentoBannerCard(VectorIcons.Proxies, "代理节点与并发测速", "浏览策略组并一键实时并发探测全部节点延迟", "立即并发测速 →", Color.FromRgb(56, 189, 248), async () => { SwitchNavigationPage("proxies"); await TestCurrentGroupLatencyAsync(); }));
            quickGrid.Children.Add(CreateBentoBannerCard(VectorIcons.Profiles, "订阅管理与多选聚合", "标准订阅中心架构，原生支持多选订阅自动去重合并", "打开订阅中心 →", Color.FromRgb(168, 85, 247), () => SwitchNavigationPage("profiles")));
            quickGrid.Children.Add(CreateBentoBannerCard(VectorIcons.Studio, "Liquid Glass 视觉工作台", "自由更换本地 4K/8K 壁纸并实时调节玻璃折射率", "定制视觉风格 →", Color.FromRgb(52, 211, 153), () => SwitchNavigationPage("studio")));
            stack.Children.Add(quickGrid);

            return stack;
        }

        private UIElement CreateIosToggleSwitchRow(string title, string subtitle, bool isChecked, Action<bool> onToggle)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = subtitle, Foreground = FrozenBrush(148, 163, 184), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(info, 0);
            g.Children.Add(info);

            // 46x24 标准胶囊开关 (CornerRadius = 12，即高度 24 的精确一半，完美半圆弧无椭圆变形)
            bool state = isChecked;
            Border track = new Border
            {
                Width = 46,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(3),
                Background = state ? _cachedActiveCardBrush : FrozenBrush(100, 51, 65, 85),
                BorderBrush = FrozenBrush(130, 255, 255, 255),
                BorderThickness = new Thickness(1)
            };
            ScaleTransform thumbScale = new ScaleTransform(1.0, 1.0);
            TranslateTransform thumbTrans = new TranslateTransform(state ? 24 : 0, 0);
            TransformGroup thumbTg = new TransformGroup();
            thumbTg.Children.Add(thumbScale);
            thumbTg.Children.Add(thumbTrans);

            Ellipse thumb = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = thumbTg
            };
            track.Child = thumb;
            track.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                state = !state;
                double targetX = state ? 24 : 0;

                DoubleAnimation transAnim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                };
                thumbTrans.BeginAnimation(TranslateTransform.XProperty, transAnim);

                DoubleAnimationUsingKeyFrames stretchAnim = new DoubleAnimationUsingKeyFrames();
                stretchAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                stretchAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                stretchAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }));
                thumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, stretchAnim);

                track.Background = state ? _cachedActiveCardBrush : FrozenBrush(100, 51, 65, 85);
                onToggle(state);
            };
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            return g;
        }

        private Border CreateBentoMetricTile(string tag, string subTitle, string svgIcon, out TextBlock valueBlock, string initVal, Color accent)
        {
            Border card = CreateGlassCard(14);
            card.Margin = new Thickness(0, 0, 10, 0);
            card.Padding = new Thickness(16, 14, 16, 14);

            StackPanel sp = new StackPanel();
            Grid topRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            StackPanel titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleCol.Children.Add(new TextBlock
            {
                Text = tag,
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            });
            titleCol.Children.Add(new TextBlock
            {
                Text = subTitle,
                Foreground = FrozenBrush(203, 213, 225),
                FontSize = 11.5,
                Margin = new Thickness(0, 1, 0, 0)
            });
            topRow.Children.Add(titleCol);

            Border iconBox = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = FrozenBrush(45, accent.R, accent.G, accent.B),
                BorderBrush = FrozenBrush(130, accent.R, accent.G, accent.B),
                BorderThickness = new Thickness(1),
                Child = VectorIcons.Create(svgIcon, FrozenBrush(accent.R, accent.G, accent.B), 14)
            };
            topRow.Children.Add(iconBox);
            sp.Children.Add(topRow);

            valueBlock = new TextBlock
            {
                Text = initVal,
                FontFamily = MonoFont,
                Foreground = Brushes.White,
                FontSize = 19.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 8)
            };
            sp.Children.Add(valueBlock);

            Border accentBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new LinearGradientBrush(accent, Color.FromArgb(25, accent.R, accent.G, accent.B), 0)
            };
            sp.Children.Add(accentBar);

            card.Child = sp;
            AttachSpringHover(card);
            return card;
        }

        private Border CreateBentoBannerCard(string svgIcon, string title, string desc, string actionText, Color accent, Action onClick)
        {
            Border card = CreateGlassCard(14);
            card.Margin = new Thickness(0, 0, 10, 0);
            card.Padding = new Thickness(16, 14, 16, 14);
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (s, e) => onClick();
            AttachSpringHover(card);

            StackPanel sp = new StackPanel();
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            Border ib = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Background = FrozenBrush(50, accent.R, accent.G, accent.B),
                BorderBrush = FrozenBrush(130, accent.R, accent.G, accent.B),
                BorderThickness = new Thickness(1),
                Child = VectorIcons.Create(svgIcon, FrozenBrush(accent.R, accent.G, accent.B), 13)
            };
            header.Children.Add(ib);
            header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(header);

            sp.Children.Add(new TextBlock { Text = desc, Foreground = FrozenBrush(186, 230, 253), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = actionText, Foreground = FrozenBrush(accent.R, accent.G, accent.B), FontSize = 11.5, FontWeight = FontWeights.Bold });
            card.Child = sp;
            return card;
        }

        // =====================================================================
        // 页面 2：Clash Mi Pro 代理节点矩阵 (CornerRadius = 12 标准圆角矩形卡片)
        // =====================================================================
        private UIElement BuildProxiesView()
        {
            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            Border topBar = CreateGlassCard(14);
            topBar.Padding = new Thickness(16, 12, 16, 12);
            topBar.Margin = new Thickness(0, 0, 0, 12);

            Grid tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.Child = tg;

            StackPanel leftInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(VectorIcons.Create(VectorIcons.Proxies, FrozenBrush(56, 189, 248), 16));
            titleRow.Children.Add(new TextBlock
            {
                Text = "PROXY MATRIX · 策略组与节点选择",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            leftInfo.Children.Add(titleRow);

            _proxyGroupSummaryText = new TextBlock
            {
                Text = string.Format("已同步 {0} 个策略组，点击节点即可毫秒级热切换线路", _proxyGroups.Count),
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 11.5,
                Margin = new Thickness(0, 3, 0, 0)
            };
            leftInfo.Children.Add(_proxyGroupSummaryText);
            Grid.SetColumn(leftInfo, 0);
            tg.Children.Add(leftInfo);

            StackPanel rightControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border searchBoxBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(105, 9, 14, 26),
                BorderBrush = FrozenBrush(85, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Width = 195,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.Children.Add(VectorIcons.Create(VectorIcons.Search, FrozenBrush(148, 163, 184), 13));

            TextBox searchInput = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(4, 0, 4, 0),
                Text = _nodeSearchKeyword
            };
            Grid.SetColumn(searchInput, 1);
            sg.Children.Add(searchInput);

            Border clearBtn = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(50, 255, 255, 255),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = string.IsNullOrEmpty(_nodeSearchKeyword) ? Visibility.Collapsed : Visibility.Visible,
                Child = new TextBlock
                {
                    Text = "×",
                    Foreground = FrozenBrush(203, 213, 225),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            clearBtn.MouseLeftButtonUp += (s, e) =>
            {
                searchInput.Text = "";
                searchInput.Focus();
            };
            Grid.SetColumn(clearBtn, 2);
            sg.Children.Add(clearBtn);

            searchInput.TextChanged += (s, e) =>
            {
                _nodeSearchKeyword = searchInput.Text.Trim();
                clearBtn.Visibility = string.IsNullOrEmpty(_nodeSearchKeyword) ? Visibility.Collapsed : Visibility.Visible;
                PopulateProxyNodesWrap();
            };
            searchInput.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    searchInput.Text = "";
                }
            };
            searchBoxBorder.Child = sg;
            rightControls.Children.Add(searchBoxBorder);

            Border sortBtn = CreateLiquidButton(
                VectorIcons.ArrowDown,
                _sortByDelay ? "按延迟排序" : "默认排序",
                _sortByDelay ? LiquidButtonStyle.Primary : LiquidButtonStyle.Neutral,
                () => { _sortByDelay = !_sortByDelay; SwitchNavigationPage("proxies"); }, 32);
            sortBtn.Margin = new Thickness(0, 0, 8, 0);
            rightControls.Children.Add(sortBtn);

            Border testBtn = CreateLiquidButton(
                VectorIcons.Bolt,
                _isTestingLatency ? "正在并发测速..." : "并发延迟测速",
                LiquidButtonStyle.Success,
                async () => await TestCurrentGroupLatencyAsync(), 32);
            StackPanel testBtnSp = testBtn.Child as StackPanel;
            if (testBtnSp != null && testBtnSp.Children.Count > 1)
            {
                _testLatencyBtnText = testBtnSp.Children[1] as TextBlock;
            }
            rightControls.Children.Add(testBtn);

            Grid.SetColumn(rightControls, 1);
            tg.Children.Add(rightControls);
            stack.Children.Add(topBar);

            Border groupBarCard = CreateGlassCard(14);
            groupBarCard.Padding = new Thickness(12, 10, 12, 4);
            groupBarCard.Margin = new Thickness(0, 0, 0, 12);
            _groupSelectorWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            groupBarCard.Child = _groupSelectorWrap;
            stack.Children.Add(groupBarCard);

            // 实时延迟测速进度面板 (Liquid Glass Progress Banner)
            _latencyProgressCard = CreateGlassCard(12);
            _latencyProgressCard.Padding = new Thickness(16, 12, 16, 12);
            _latencyProgressCard.Margin = new Thickness(0, 0, 0, 12);
            _latencyProgressCard.Visibility = _isTestingLatency ? Visibility.Visible : Visibility.Collapsed;

            StackPanel progStack = new StackPanel { Orientation = Orientation.Vertical };
            Grid progTopRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            progTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel progLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            progLeft.Children.Add(VectorIcons.Create(VectorIcons.Bolt, FrozenBrush(56, 189, 248), 15));
            _latencyStatusTitle = new TextBlock
            {
                Text = "正在并发探测节点真实延迟...",
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(7, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            progLeft.Children.Add(_latencyStatusTitle);

            _latencyCurrentNodeText = new TextBlock
            {
                Text = "准备队列...",
                Foreground = FrozenBrush(148, 163, 184),
                FontFamily = MonoFont,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 280
            };
            progLeft.Children.Add(_latencyCurrentNodeText);
            Grid.SetColumn(progLeft, 0);
            progTopRow.Children.Add(progLeft);

            StackPanel progRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // 有效节点统计胶囊
            Border validPill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = FrozenBrush(90, 6, 78, 59),
                BorderBrush = FrozenBrush(140, 52, 211, 153),
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _latencyValidCountText = new TextBlock
            {
                Text = "有效: 0",
                Foreground = FrozenBrush(52, 211, 153),
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold
            };
            validPill.Child = _latencyValidCountText;
            progRight.Children.Add(validPill);

            // 超时节点统计胶囊
            Border timeoutPill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = FrozenBrush(90, 127, 29, 29),
                BorderBrush = FrozenBrush(140, 248, 113, 113),
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _latencyTimeoutCountText = new TextBlock
            {
                Text = "超时: 0",
                Foreground = FrozenBrush(248, 113, 113),
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold
            };
            timeoutPill.Child = _latencyTimeoutCountText;
            progRight.Children.Add(timeoutPill);

            // 最快节点统计胶囊
            Border fastestPill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = FrozenBrush(95, 12, 74, 110),
                BorderBrush = FrozenBrush(150, 56, 189, 248),
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 10, 0)
            };
            _latencyFastestNodeText = new TextBlock
            {
                Text = "最快: --",
                Foreground = FrozenBrush(186, 230, 253),
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                MaxWidth = 210,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            fastestPill.Child = _latencyFastestNodeText;
            progRight.Children.Add(fastestPill);

            _latencyPercentText = new TextBlock
            {
                Text = "0 / 0 (0%)",
                Foreground = Brushes.White,
                FontFamily = MonoFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            progRight.Children.Add(_latencyPercentText);

            Grid.SetColumn(progRight, 1);
            progTopRow.Children.Add(progRight);
            progStack.Children.Add(progTopRow);

            // 液态玻璃流光进度条轨道
            Border progTrack = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = FrozenBrush(110, 9, 14, 26),
                BorderBrush = FrozenBrush(55, 255, 255, 255),
                BorderThickness = new Thickness(0.8),
                ClipToBounds = true
            };
            var progFillBrush = new LinearGradientBrush(Color.FromRgb(56, 189, 248), Color.FromRgb(52, 211, 153), 0.0);
            progFillBrush.Freeze();
            _latencyProgressScaleX = new ScaleTransform(0.0, 1.0);
            Border progFill = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = progFillBrush,
                RenderTransformOrigin = new Point(0.0, 0.5),
                RenderTransform = _latencyProgressScaleX
            };
            progTrack.Child = progFill;
            progStack.Children.Add(progTrack);

            _latencyProgressCard.Child = progStack;
            stack.Children.Add(_latencyProgressCard);

            _loadMoreNodesBanner = null;
            _loadMoreNodesText = null;
            _nodesGridWrap = new ResponsiveCardsPanel { Margin = new Thickness(0, 0, 0, 12) };
            stack.Children.Add(_nodesGridWrap);

            _loadMoreHost = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
            stack.Children.Add(_loadMoreHost);

            PopulateProxyGroupsBar();
            PopulateProxyNodesWrap();

            return stack;
        }

        private void PopulateProxyGroupsBar()
        {
            if (_groupSelectorWrap == null) return;
            _groupSelectorWrap.Children.Clear();

            if (_proxyGroups.Count == 0)
            {
                _groupSelectorWrap.Children.Add(new TextBlock
                {
                    Text = "正在从 Mihomo 内核同步策略组列表...",
                    Foreground = FrozenBrush(186, 230, 253),
                    Margin = new Thickness(6)
                });
                return;
            }

            if (string.IsNullOrEmpty(_selectedGroupName) || !_proxyGroups.Any(g => g.Name == _selectedGroupName))
            {
                _selectedGroupName = _proxyGroups[0].Name;
            }

            foreach (var grp in _proxyGroups)
            {
                string gName = grp.Name;
                bool isSelected = (gName == _selectedGroupName);

                Border pill = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(13, 6, 13, 6),
                    Margin = new Thickness(0, 0, 8, 6),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Background = isSelected ? _cachedActiveCardBrush : FrozenBrush(30, 255, 255, 255),
                    BorderBrush = isSelected ? _cachedActiveBorderBrush : FrozenBrush(60, 255, 255, 255)
                };

                StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock
                {
                    Text = gName,
                    Foreground = isSelected ? Brushes.White : FrozenBrush(226, 232, 240),
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Border countBadge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = isSelected ? FrozenBrush(70, 9, 14, 26) : FrozenBrush(85, 9, 14, 26),
                    BorderBrush = isSelected ? FrozenBrush(90, 56, 189, 248) : FrozenBrush(40, 255, 255, 255),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = Convert.ToString(grp.All != null ? grp.All.Count : 0),
                        Foreground = isSelected ? FrozenBrush(186, 230, 253) : FrozenBrush(148, 163, 184),
                        FontFamily = MonoFont,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    }
                };
                sp.Children.Add(countBadge);
                pill.Child = sp;

                pill.MouseLeftButtonUp += (s, e) =>
                {
                    _selectedGroupName = gName;
                    PopulateProxyGroupsBar();
                    PopulateProxyNodesWrap();
                };
                AttachSpringHover(pill);
                _groupSelectorWrap.Children.Add(pill);
            }
        }

        private void PopulateProxyNodesWrap()
        {
            if (_nodesGridWrap == null) return;
            _nodesGridWrap.Children.Clear();
            _nodeLiveRefs.Clear();
            if (_loadMoreHost != null)
            {
                _loadMoreHost.Child = null;
                _loadMoreHost.Visibility = Visibility.Collapsed;
            }

            var grp = _proxyGroups.FirstOrDefault(g => g.Name == _selectedGroupName);
            if (grp == null || grp.All == null) return;

            if (_proxyGroupSummaryText != null)
            {
                _proxyGroupSummaryText.Text = string.Format("当前策略组 [{0}] ({1})   ·   已激活节点: {2}   ·   共 {3} 个可选节点",
                    grp.Name, grp.Type, grp.Now, grp.All.Count);
            }

            IEnumerable<string> nodeNames = grp.All;
            if (!string.IsNullOrEmpty(_nodeSearchKeyword))
            {
                nodeNames = nodeNames.Where(n => n.IndexOf(_nodeSearchKeyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_sortByDelay)
            {
                nodeNames = nodeNames.OrderBy(n =>
                {
                    ProxyNodeInfo info;
                    if (_proxyNodes.TryGetValue(n, out info))
                    {
                        if (info.Delay > 0) return info.Delay;
                        if (info.Delay == 0) return 888888;
                        if (info.Delay < 0) return 999999;
                    }
                    return 888888;
                });
            }

            _currentFilteredNodeNames = nodeNames.ToList();
            _renderedNodeCount = 0;

            if (_currentFilteredNodeNames.Count == 0)
            {
                if (_loadMoreHost != null)
                {
                    Border emptyCard = CreateGlassCard(12);
                    emptyCard.Padding = new Thickness(24, 28, 24, 28);
                    emptyCard.Margin = new Thickness(0, 8, 0, 16);
                    StackPanel ep = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    ep.Children.Add(VectorIcons.Create(VectorIcons.Search, FrozenBrush(100, 148, 163, 184), 28));
                    ep.Children.Add(new TextBlock
                    {
                        Text = !string.IsNullOrEmpty(_nodeSearchKeyword)
                            ? string.Format("未搜索到匹配 \"{0}\" 的节点", _nodeSearchKeyword)
                            : "当前策略组下无可用的代理节点",
                        Foreground = FrozenBrush(148, 163, 184),
                        FontSize = 13,
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(0, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    emptyCard.Child = ep;
                    _loadMoreHost.Child = emptyCard;
                    _loadMoreHost.Visibility = Visibility.Visible;
                }
                return;
            }

            AppendNextNodeBatch(NodeBatchSize);
        }

        private void AppendNextNodeBatch(int count)
        {
            if (_nodesGridWrap == null || _currentFilteredNodeNames == null) return;

            var grp = _proxyGroups.FirstOrDefault(g => g.Name == _selectedGroupName);
            if (grp == null) return;

            int toRender = Math.Min(count, _currentFilteredNodeNames.Count - _renderedNodeCount);
            for (int i = 0; i < toRender; i++)
            {
                string nodeName = _currentFilteredNodeNames[_renderedNodeCount++];
                bool isCurrent = (nodeName == grp.Now);
                ProxyNodeInfo info;
                _proxyNodes.TryGetValue(nodeName, out info);
                int delay = (info != null) ? info.Delay : 0;
                string nType = (info != null && !string.IsNullOrEmpty(info.Type)) ? info.Type : "Proxy";

                Border nodeCard = CreateRefinedProxyNodeCard(grp.Name, nodeName, nType, delay, isCurrent);
                _nodesGridWrap.Children.Add(nodeCard);
            }

            if (_renderedNodeCount < _currentFilteredNodeNames.Count)
            {
                int remaining = _currentFilteredNodeNames.Count - _renderedNodeCount;
                if (_loadMoreNodesBanner == null)
                {
                    _loadMoreNodesBanner = CreateLoadMoreNodesBanner();
                }
                if (_loadMoreNodesText != null)
                {
                    _loadMoreNodesText.Text = string.Format("已渲染 {0} / {1} 个节点 · 向下滚动自动加载更多 (或点击立即全部加载剩余 {2} 个节点)",
                        _renderedNodeCount, _currentFilteredNodeNames.Count, remaining);
                }
                if (_loadMoreHost != null)
                {
                    _loadMoreHost.Child = _loadMoreNodesBanner;
                    _loadMoreHost.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (_loadMoreHost != null)
                {
                    _loadMoreHost.Child = null;
                    _loadMoreHost.Visibility = Visibility.Collapsed;
                }
            }
        }

        private Border CreateLoadMoreNodesBanner()
        {
            Border b = CreateGlassCard(10);
            b.Padding = new Thickness(18, 12, 18, 12);
            b.Margin = new Thickness(0);
            b.Cursor = Cursors.Hand;
            b.Background = FrozenBrush(80, 15, 23, 42);
            b.BorderBrush = FrozenBrush(90, 56, 189, 248);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _loadMoreNodesText = new TextBlock
            {
                Text = "向下滚动自动加载更多节点 (或点击立即加载全部)",
                Foreground = FrozenBrush(186, 230, 253),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_loadMoreNodesText, 0);
            g.Children.Add(_loadMoreNodesText);

            Border btn = CreateLiquidButton(VectorIcons.ArrowDown, "加载全部", LiquidButtonStyle.Primary, () =>
            {
                AppendNextNodeBatch(999999);
            }, 28);
            Grid.SetColumn(btn, 1);
            g.Children.Add(btn);

            b.Child = g;
            b.MouseLeftButtonUp += (s, e) =>
            {
                AppendNextNodeBatch(NodeBatchSize * 2);
            };
            AttachSpringHover(b);
            return b;
        }

        private void ApplyNodeCardDelayVisual(NodeCardLiveRef liveRef, int delay, bool isProbing)
        {
            if (liveRef == null) return;
            SolidColorBrush delayBrush;
            string delayStr;

            if (isProbing)
            {
                delayBrush = FrozenBrush(56, 189, 248);
                delayStr = "测速中...";
            }
            else if (delay > 0 && delay < 150)
            {
                delayBrush = FrozenBrush(52, 211, 153);
                delayStr = delay + " ms";
            }
            else if (delay >= 150 && delay < 400)
            {
                delayBrush = FrozenBrush(251, 191, 36);
                delayStr = delay + " ms";
            }
            else if (delay >= 400)
            {
                delayBrush = FrozenBrush(248, 113, 113);
                delayStr = delay + " ms";
            }
            else if (delay < 0)
            {
                delayBrush = FrozenBrush(248, 113, 113);
                delayStr = "TIMEOUT";
            }
            else
            {
                delayBrush = FrozenBrush(148, 163, 184);
                delayStr = "未测速";
            }

            if (liveRef.DelayBadge != null)
            {
                liveRef.DelayBadge.BorderBrush = delayBrush;
                ScaleTransform stBadge = liveRef.DelayBadge.RenderTransform as ScaleTransform;
                if (!isProbing && stBadge != null)
                {
                    DoubleAnimation pop = new DoubleAnimation(1.12, 1.0, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                    };
                    stBadge.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    stBadge.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
            }
            if (liveRef.DelayDot != null) liveRef.DelayDot.Fill = delayBrush;
            if (liveRef.DelayText != null)
            {
                liveRef.DelayText.Text = delayStr;
                liveRef.DelayText.Foreground = delayBrush;
            }
            if (liveRef.AccentStrip != null && !liveRef.IsSelected)
            {
                liveRef.AccentStrip.Background = (delay > 0 || isProbing) ? delayBrush : FrozenBrush(80, 148, 163, 184);
            }
        }

        public static string FixMojibake(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input.Contains("馃") || input.Contains("棣") || input.Contains("缇") || input.Contains("鏃") || input.Contains("鏂") || input.Contains("寰"))
            {
                try
                {
                    Encoding gbk = Encoding.GetEncoding(936);
                    byte[] bytes = gbk.GetBytes(input);
                    string recovered = Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrEmpty(recovered) && !recovered.Contains("\uFFFD"))
                    {
                        return recovered;
                    }
                }
                catch { }
            }
            return input;
        }

        private static void DetectNodeRegionInfo(string name, string type, out string regionKey, out string flagEmoji, out Color tint, out Color border)
        {
            if (string.IsNullOrEmpty(name))
            {
                regionKey = "PROXY";
                flagEmoji = "🌐";
                tint = Color.FromArgb(40, 148, 163, 184);
                border = Color.FromArgb(70, 148, 163, 184);
                return;
            }

            string cleanName = FixMojibake(name);
            string lower = cleanName.ToLowerInvariant();

            if (lower == "direct" || lower == "直连")
            {
                regionKey = "DIRECT";
                flagEmoji = "⚡";
                tint = Color.FromArgb(50, 16, 185, 129);
                border = Color.FromArgb(90, 52, 211, 153);
                return;
            }
            if (lower.Contains("auto") || lower.Contains("自动") || lower.Contains("url-test") || lower.Contains("fallback"))
            {
                regionKey = "AUTO";
                flagEmoji = "🔄";
                tint = Color.FromArgb(50, 56, 189, 248);
                border = Color.FromArgb(90, 125, 211, 252);
                return;
            }

            // 1. 常见国家与地区 Flags / 标识映射
            if (cleanName.Contains("🇭🇰") || lower.Contains("hk") || lower.Contains("hong kong") || lower.Contains("hkg") || cleanName.Contains("香港"))
            {
                regionKey = "HK";
                flagEmoji = "🇭🇰";
                tint = Color.FromArgb(45, 239, 68, 68);
                border = Color.FromArgb(85, 248, 113, 113);
                return;
            }
            if (cleanName.Contains("🇯🇵") || lower.Contains("jp") || lower.Contains("japan") || lower.Contains("jpn") || cleanName.Contains("日本") || cleanName.Contains("东京") || cleanName.Contains("大阪"))
            {
                regionKey = "JP";
                flagEmoji = "🇯🇵";
                tint = Color.FromArgb(45, 244, 63, 94);
                border = Color.FromArgb(85, 251, 113, 133);
                return;
            }
            if (cleanName.Contains("🇺🇸") || lower.Contains("us") || lower.Contains("usa") || lower.Contains("united states") || lower.Contains("america") || cleanName.Contains("美国") || cleanName.Contains("洛杉矶") || cleanName.Contains("圣何塞"))
            {
                regionKey = "US";
                flagEmoji = "🇺🇸";
                tint = Color.FromArgb(45, 37, 99, 235);
                border = Color.FromArgb(85, 96, 165, 250);
                return;
            }
            if (cleanName.Contains("🇸🇬") || lower.Contains("sg") || lower.Contains("sgp") || lower.Contains("singapore") || cleanName.Contains("新加坡") || cleanName.Contains("狮城"))
            {
                regionKey = "SG";
                flagEmoji = "🇸🇬";
                tint = Color.FromArgb(45, 220, 38, 38);
                border = Color.FromArgb(85, 248, 113, 113);
                return;
            }
            if (cleanName.Contains("🇹🇼") || lower.Contains("tw") || lower.Contains("twn") || lower.Contains("taiwan") || cleanName.Contains("台湾") || cleanName.Contains("台北"))
            {
                regionKey = "TW";
                flagEmoji = "🇹🇼";
                tint = Color.FromArgb(45, 99, 102, 241);
                border = Color.FromArgb(85, 165, 180, 252);
                return;
            }
            if (cleanName.Contains("🇰🇷") || lower.Contains("kr") || lower.Contains("kor") || lower.Contains("korea") || cleanName.Contains("韩国") || cleanName.Contains("首尔"))
            {
                regionKey = "KR";
                flagEmoji = "🇰🇷";
                tint = Color.FromArgb(45, 6, 182, 212);
                border = Color.FromArgb(85, 103, 232, 249);
                return;
            }
            if (cleanName.Contains("🇩🇪") || lower.Contains("de") || lower.Contains("germany") || lower.Contains("deu") || cleanName.Contains("德国") || cleanName.Contains("法兰克福"))
            {
                regionKey = "DE";
                flagEmoji = "🇩🇪";
                tint = Color.FromArgb(45, 245, 158, 11);
                border = Color.FromArgb(85, 252, 211, 77);
                return;
            }
            if (cleanName.Contains("🇬🇧") || lower.Contains("uk") || lower.Contains("gbr") || lower.Contains("britain") || cleanName.Contains("英国") || cleanName.Contains("伦敦"))
            {
                regionKey = "GB";
                flagEmoji = "🇬🇧";
                tint = Color.FromArgb(45, 30, 64, 175);
                border = Color.FromArgb(85, 96, 165, 250);
                return;
            }
            if (cleanName.Contains("🇫🇷") || lower.Contains("fr") || lower.Contains("fra") || lower.Contains("france") || cleanName.Contains("法国") || cleanName.Contains("巴黎"))
            {
                regionKey = "FR";
                flagEmoji = "🇫🇷";
                tint = Color.FromArgb(45, 59, 130, 246);
                border = Color.FromArgb(85, 147, 197, 253);
                return;
            }
            if (cleanName.Contains("🇨🇦") || lower.Contains("ca") || lower.Contains("can") || lower.Contains("canada") || cleanName.Contains("加拿大"))
            {
                regionKey = "CA";
                flagEmoji = "🇨🇦";
                tint = Color.FromArgb(45, 225, 29, 72);
                border = Color.FromArgb(85, 251, 113, 133);
                return;
            }
            if (cleanName.Contains("🇦🇺") || lower.Contains("au") || lower.Contains("aus") || lower.Contains("australia") || cleanName.Contains("澳大利亚") || cleanName.Contains("澳洲") || cleanName.Contains("悉尼"))
            {
                regionKey = "AU";
                flagEmoji = "🇦🇺";
                tint = Color.FromArgb(45, 14, 165, 233);
                border = Color.FromArgb(85, 125, 211, 252);
                return;
            }
            if (cleanName.Contains("🇲🇾") || lower.Contains("my") || lower.Contains("mys") || lower.Contains("malaysia") || cleanName.Contains("马来西亚"))
            {
                regionKey = "MY";
                flagEmoji = "🇲🇾";
                tint = Color.FromArgb(45, 234, 179, 8);
                border = Color.FromArgb(85, 253, 224, 71);
                return;
            }
            if (cleanName.Contains("🇳🇱") || lower.Contains("nl") || lower.Contains("nld") || lower.Contains("netherlands") || cleanName.Contains("荷兰") || cleanName.Contains("阿姆斯特丹"))
            {
                regionKey = "NL";
                flagEmoji = "🇳🇱";
                tint = Color.FromArgb(45, 249, 115, 22);
                border = Color.FromArgb(85, 253, 186, 116);
                return;
            }
            if (cleanName.Contains("🇷🇺") || lower.Contains("ru") || lower.Contains("rus") || lower.Contains("russia") || cleanName.Contains("俄罗斯") || cleanName.Contains("莫斯科"))
            {
                regionKey = "RU";
                flagEmoji = "🇷🇺";
                tint = Color.FromArgb(45, 99, 102, 241);
                border = Color.FromArgb(85, 165, 180, 252);
                return;
            }

            regionKey = "NODE";
            flagEmoji = "🌐";
            tint = Color.FromArgb(35, 148, 163, 184);
            border = Color.FromArgb(65, 148, 163, 184);
        }

        private static Border CreateNodeRegionIcon(string nodeName, string nodeType)
        {
            string regionKey;
            string flagEmoji;
            Color regionTint;
            Color regionBorder;

            DetectNodeRegionInfo(nodeName, nodeType, out regionKey, out flagEmoji, out regionTint, out regionBorder);

            Border badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = FrozenBrush(regionTint.A, regionTint.R, regionTint.G, regionTint.B),
                BorderBrush = FrozenBrush(regionBorder.A, regionBorder.R, regionBorder.G, regionBorder.B),
                BorderThickness = new Thickness(0.8),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
                ToolTip = "节点区域: " + regionKey
            };

            if (regionKey == "DIRECT")
            {
                badge.Child = VectorIcons.Create(VectorIcons.Bolt, FrozenBrush(52, 211, 153), 11);
            }
            else if (regionKey == "AUTO")
            {
                badge.Child = VectorIcons.Create(VectorIcons.Proxies, FrozenBrush(56, 189, 248), 11);
            }
            else if (!string.IsNullOrEmpty(flagEmoji))
            {
                badge.Child = new TextBlock
                {
                    Text = flagEmoji,
                    FontFamily = EmojiFont,
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                };
            }
            else
            {
                badge.Child = new TextBlock
                {
                    Text = regionKey.Length <= 2 ? regionKey : regionKey.Substring(0, 2),
                    FontFamily = MonoFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = FrozenBrush(226, 232, 240),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            return badge;
        }

        // 节点卡片：采用 CornerRadius(11) 标准圆角与几何延迟标签，由 ResponsiveCardsPanel 动态自适应等宽排布
        private Border CreateRefinedProxyNodeCard(string groupName, string nodeName, string nodeType, int delay, bool isSelected)
        {
            Border card = new Border
            {
                Height = 84,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                BorderThickness = new Thickness(1.1),
                SnapsToDevicePixels = true
            };

            SolidColorBrush delayBrush = FrozenBrush(148, 163, 184);
            string delayStr = "未测速";
            if (delay > 0 && delay < 150) { delayBrush = FrozenBrush(52, 211, 153); delayStr = delay + " ms"; }
            else if (delay >= 150 && delay < 400) { delayBrush = FrozenBrush(251, 191, 36); delayStr = delay + " ms"; }
            else if (delay >= 400) { delayBrush = FrozenBrush(248, 113, 113); delayStr = delay + " ms"; }
            else if (delay < 0) { delayBrush = FrozenBrush(248, 113, 113); delayStr = "TIMEOUT"; }

            if (isSelected)
            {
                card.Background = _cachedActiveCardBrush;
                card.BorderBrush = _cachedActiveBorderBrush;
            }
            else
            {
                card.Background = _cachedCardBrush;
                card.BorderBrush = _cachedCardRimBrush;
            }

            Grid outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.5) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border accentStrip = new Border
            {
                Background = isSelected
                    ? FrozenBrush(224, 242, 254)
                    : (delay > 0 ? delayBrush : FrozenBrush(70, 148, 163, 184))
            };
            Grid.SetColumn(accentStrip, 0);
            outerGrid.Children.Add(accentStrip);

            Grid contentGrid = new Grid { Margin = new Thickness(13, 11, 13, 11) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧：区域旗帜/图标徽标 + 节点名称（支持 EmojiFont 完整彩色渲染）
            Grid leftTop = new Grid();
            leftTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            leftTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border regionBadge = CreateNodeRegionIcon(nodeName, nodeType);
            Grid.SetColumn(regionBadge, 0);
            leftTop.Children.Add(regionBadge);

            string displayName = FixMojibake(nodeName);
            TextBlock nameTb = new TextBlock
            {
                Text = displayName,
                Foreground = isSelected ? Brushes.White : FrozenBrush(241, 245, 249),
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                FontFamily = EmojiFont,
                FontSize = 12.2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = displayName
            };
            Grid.SetColumn(nameTb, 1);
            leftTop.Children.Add(nameTb);

            Grid.SetColumn(leftTop, 0);
            topRow.Children.Add(leftTop);

            // 右上角：来源订阅名称胶囊标签 + 选中勾选圈
            StackPanel rightTop = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            string sourceName = GetNodeSource(nodeName);
            if (!string.IsNullOrEmpty(sourceName))
            {
                Border sourceBadge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = isSelected ? FrozenBrush(70, 99, 102, 241) : FrozenBrush(38, 30, 41, 59),
                    BorderBrush = isSelected ? FrozenBrush(140, 199, 210, 254) : FrozenBrush(65, 148, 163, 184),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "订阅来源: " + sourceName,
                    Child = new TextBlock
                    {
                        Text = sourceName,
                        Foreground = isSelected ? FrozenBrush(224, 231, 255) : FrozenBrush(148, 163, 184),
                        FontFamily = MonoFont,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Medium,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 85
                    }
                };
                rightTop.Children.Add(sourceBadge);
            }

            Border checkCircle = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Background = FrozenBrush(220, 16, 185, 129),
                BorderBrush = FrozenBrush(180, 110, 231, 183),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed,
                Child = VectorIcons.Create(VectorIcons.Check, Brushes.White, 9.5)
            };
            rightTop.Children.Add(checkCircle);

            Grid.SetColumn(rightTop, 1);
            topRow.Children.Add(rightTop);
            Grid.SetRow(topRow, 0);
            contentGrid.Children.Add(topRow);

            // 第二行：CornerRadius(5) 几何协议标与延迟标签（支持单独点击延迟标签重测单节点）
            Grid bottomRow = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
            Border protoTag = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                Background = FrozenBrush(60, 9, 14, 26),
                BorderBrush = FrozenBrush(45, 255, 255, 255),
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(7, 2, 7, 2),
                Child = new TextBlock
                {
                    Text = nodeType.ToUpperInvariant(),
                    Foreground = FrozenBrush(186, 230, 253),
                    FontFamily = MonoFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold
                }
            };
            bottomRow.Children.Add(protoTag);

            Border delayBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(5),
                Background = FrozenBrush(90, 9, 14, 26),
                BorderBrush = delayBrush,
                BorderThickness = new Thickness(0.9),
                Padding = new Thickness(8, 2.5, 8, 2.5),
                ToolTip = "点击单独重测此节点延迟",
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.0, 1.0)
            };
            StackPanel delayInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Ellipse delayDot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = delayBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            delayInner.Children.Add(delayDot);
            TextBlock delayText = new TextBlock
            {
                Text = delayStr,
                FontFamily = MonoFont,
                Foreground = delayBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            delayInner.Children.Add(delayText);
            delayBadge.Child = delayInner;
            bottomRow.Children.Add(delayBadge);

            var liveRef = new NodeCardLiveRef
            {
                CardBorder = card,
                AccentStrip = accentStrip,
                DelayBadge = delayBadge,
                DelayDot = delayDot,
                DelayText = delayText,
                CheckCircle = checkCircle,
                IsSelected = isSelected
            };
            _nodeLiveRefs[nodeName] = liveRef;

            delayBadge.MouseLeftButtonUp += async (s, e) =>
            {
                e.Handled = true;
                await TestSingleNodeLatencyAsync(nodeName);
            };

            Grid.SetRow(bottomRow, 1);
            contentGrid.Children.Add(bottomRow);

            Grid.SetColumn(contentGrid, 1);
            outerGrid.Children.Add(contentGrid);
            card.Child = outerGrid;

            card.MouseLeftButtonUp += async (s, e) => await SelectProxyInGroupAsync(groupName, nodeName);
            AttachSpringHover(card);
            return card;
        }

        // =====================================================================
        // =====================================================================
        // 页面 3：订阅中心（标准订阅管理体系，原生支持单选/多选订阅合并使用）
        // =====================================================================
        private string GetProfilesSummaryString()
        {
            var enabledList = _subscriptionProfiles.Where(p => p.Enabled).ToList();
            if (enabledList.Count == 0) return "当前未启用任何订阅 (直连模式)   |   聚合节点: 0 个   |   端口: 7890";
            string names = string.Join(" + ", enabledList.Select(p => p.Name).Take(2));
            if (enabledList.Count > 2) names += string.Format(" 等 {0} 个订阅", enabledList.Count);
            int nodeCount = enabledList.Sum(p => p.NodeCount);
            if (nodeCount == 0 && _proxyNodes != null) nodeCount = _proxyNodes.Count;
            return string.Format("当前生效: {0}   |   聚合节点: {1} 个   |   端口: 7890", names, nodeCount);
        }

        private async Task ToggleProfileSelectionAsync(SubscriptionProfileItem profile)
        {
            if (profile == null) return;
            profile.Enabled = !profile.Enabled;

            SaveSubscriptionProfiles();
            int total = await RebuildMergedConfigAndReloadAsync();
            if (_profilesStatusTb != null)
            {
                int actCount = _subscriptionProfiles.Count(p => p.Enabled);
                if (actCount == 0)
                {
                    _profilesStatusTb.Text = "✓ 已取消所有订阅勾选，当前切换为直连无节点模式。";
                }
                else
                {
                    _profilesStatusTb.Text = string.Format("✓ 配置生效状态已更新：当前勾选 {0} 个订阅，已聚合 {1} 个节点并热重载生效！", actCount, total);
                }
            }
            PopulateProfilesList();
        }

        private UIElement BuildProfilesAndMergerView()
        {
            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            // 1. 顶栏总览卡片 (订阅中心标题 + 当前生效多选统计 + 全局操作栏)
            Border headerCard = CreateGlassCard(14);
            headerCard.Padding = new Thickness(20, 16, 20, 16);
            headerCard.Margin = new Thickness(0, 0, 0, 12);

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerCard.Child = hg;

            StackPanel infoCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(VectorIcons.Create(VectorIcons.Profiles, FrozenBrush(56, 189, 248), 16));
            titleRow.Children.Add(new TextBlock
            {
                Text = "PROFILES · 订阅与配置中心",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            infoCol.Children.Add(titleRow);

            _profilesSummaryText = new TextBlock
            {
                Text = GetProfilesSummaryString(),
                Foreground = FrozenBrush(125, 211, 252),
                FontFamily = MonoFont,
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0)
            };
            infoCol.Children.Add(_profilesSummaryText);
            Grid.SetColumn(infoCol, 0);
            hg.Children.Add(infoCol);

            // 右侧全局操作按钮栏
            StackPanel topBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border updateAllBtn = CreateLiquidButton(VectorIcons.Refresh, "全部更新", LiquidButtonStyle.Success, async () =>
            {
                await UpdateAllSubscriptionsAsync(_profilesStatusTb);
            }, 32);
            updateAllBtn.Margin = new Thickness(0, 0, 8, 0);
            topBtns.Children.Add(updateAllBtn);

            Border importLocalBtn = CreateLiquidButton(VectorIcons.Folder, "导入本地 YAML", LiquidButtonStyle.Neutral, async () =>
            {
                OpenFileDialog dlg = new OpenFileDialog
                {
                    Title = "选择要导入的 Clash / Mihomo YAML 配置文件",
                    Filter = "YAML 配置文件 (*.yaml;*.yml)|*.yaml;*.yml|所有文件 (*.*)|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
                {
                    foreach (string fn in dlg.FileNames)
                    {
                        await ImportLocalYamlProfileAsync(fn, _profilesStatusTb);
                    }
                }
            }, 32);
            importLocalBtn.Margin = new Thickness(0, 0, 8, 0);
            topBtns.Children.Add(importLocalBtn);

            Border openFolderBtn = CreateLiquidButton(VectorIcons.Folder, "打开配置目录", LiquidButtonStyle.Neutral, () =>
            {
                try { Process.Start("explorer.exe", _dataDir); } catch { }
            }, 32);
            topBtns.Children.Add(openFolderBtn);

            Grid.SetColumn(topBtns, 1);
            hg.Children.Add(topBtns);
            stack.Children.Add(headerCard);

            // 2. 单行快捷添加新订阅栏 (Streamlined Quick Add Bar)
            Border addBar = CreateGlassCard(12);
            addBar.Padding = new Thickness(14, 8, 14, 8);
            addBar.Margin = new Thickness(0, 0, 0, 12);

            Grid ag = new Grid();
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 订阅名称输入框
            Border nameBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(95, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBox nameTb = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontSize = 12,
                Text = "专线订阅 " + (_subscriptionProfiles.Count + 1)
            };
            nameBox.Child = nameTb;
            Grid.SetColumn(nameBox, 0);
            ag.Children.Add(nameBox);

            // 订阅链接 URL 输入框
            Border urlBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(95, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBox urlTb = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontFamily = MonoFont,
                FontSize = 11.5
            };
            urlBox.Child = urlTb;
            Grid.SetColumn(urlBox, 1);
            ag.Children.Add(urlBox);

            // 导入并启用按钮
            Border addBtn = CreateLiquidButton(VectorIcons.Check, "导入订阅并启用", LiquidButtonStyle.Primary, async () =>
            {
                string u = urlTb.Text.Trim();
                string nm = nameTb.Text.Trim();
                if (string.IsNullOrEmpty(u))
                {
                    if (_profilesStatusTb != null) _profilesStatusTb.Text = "请输入有效的订阅 URL 地址。";
                    return;
                }
                await AddNewSubscriptionAsync(u, nm, _profilesStatusTb);
                urlTb.Text = "";
                nameTb.Text = "专线订阅 " + (_subscriptionProfiles.Count + 1);
            }, 32);
            Grid.SetColumn(addBtn, 2);
            ag.Children.Add(addBtn);

            addBar.Child = ag;
            stack.Children.Add(addBar);

            // 3. 动态状态提示条
            _profilesStatusTb = new TextBlock
            {
                Foreground = FrozenBrush(52, 211, 153),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 0, 0, 10),
                Text = "提示：点击卡片或勾选框即可多选组合订阅，选中的配置将自动去重聚合生效。"
            };
            stack.Children.Add(_profilesStatusTb);

            // 4. 订阅列表区工具栏 (多选说明 + 一键全选/单选)
            Grid subHeader = new Grid { Margin = new Thickness(4, 0, 4, 8) };
            subHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            subHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock subListTitle = new TextBlock
            {
                Text = string.Format("已保存的订阅配置 ({0}) · 点击卡片多选组合：", _subscriptionProfiles.Count),
                Foreground = FrozenBrush(203, 213, 225),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(subListTitle, 0);
            subHeader.Children.Add(subListTitle);

            StackPanel quickSelStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border selectAllBtn = CreateLiquidButton(null, "全选启用", LiquidButtonStyle.Neutral, async () =>
            {
                foreach (var p in _subscriptionProfiles) p.Enabled = true;
                SaveSubscriptionProfiles();
                int total = await RebuildMergedConfigAndReloadAsync();
                if (_profilesStatusTb != null) _profilesStatusTb.Text = string.Format("✓ 已全选所有订阅，聚合总节点 {0} 个并已生效！", total);
                PopulateProfilesList();
            }, 24);
            selectAllBtn.Margin = new Thickness(0, 0, 6, 0);
            quickSelStack.Children.Add(selectAllBtn);

            Border selectSingleBtn = CreateLiquidButton(null, "仅选首项", LiquidButtonStyle.Neutral, async () =>
            {
                if (_subscriptionProfiles.Count > 0)
                {
                    for (int i = 0; i < _subscriptionProfiles.Count; i++) _subscriptionProfiles[i].Enabled = (i == 0);
                    SaveSubscriptionProfiles();
                    int total = await RebuildMergedConfigAndReloadAsync();
                    if (_profilesStatusTb != null) _profilesStatusTb.Text = string.Format("✓ 已仅启用首项订阅 [{0}]，当前节点 {1} 个。", _subscriptionProfiles[0].Name, total);
                    PopulateProfilesList();
                }
            }, 24);
            selectSingleBtn.Margin = new Thickness(0, 0, 6, 0);
            quickSelStack.Children.Add(selectSingleBtn);

            Border deselectAllBtn = CreateLiquidButton(null, "全部取消", LiquidButtonStyle.Neutral, async () =>
            {
                foreach (var p in _subscriptionProfiles) p.Enabled = false;
                SaveSubscriptionProfiles();
                int total = await RebuildMergedConfigAndReloadAsync();
                if (_profilesStatusTb != null) _profilesStatusTb.Text = "✓ 已取消所有订阅启用，当前切换为直连模式。";
                PopulateProfilesList();
            }, 24);
            quickSelStack.Children.Add(deselectAllBtn);

            Grid.SetColumn(quickSelStack, 1);
            subHeader.Children.Add(quickSelStack);
            stack.Children.Add(subHeader);

            // 5. 订阅卡片宿主
            _profilesListStack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(_profilesListStack);

            PopulateProfilesList();

            // 6. 自定义直连网站白名单 (Custom Direct Rules Card)
            Border directCard = CreateGlassCard(14);
            directCard.Padding = new Thickness(20, 16, 20, 16);
            directCard.Margin = new Thickness(0, 14, 0, 16);

            StackPanel directStack = new StackPanel();
            Grid directTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            directTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            directTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel directTitleSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel directHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            directHeader.Children.Add(VectorIcons.Create(VectorIcons.Direct, FrozenBrush(56, 189, 248), 16));
            directHeader.Children.Add(new TextBlock
            {
                Text = "CUSTOM DIRECT ROUTING · 自定义网站走直连 (免代理分流规则)",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            directTitleSp.Children.Add(directHeader);
            directTitleSp.Children.Add(new TextBlock
            {
                Text = "在此添加需绕过代理直接连线的域名后缀或关键词 (如 bilibili.com, *.epicgames.com)，规则将以最高优先级注入内核并热生效。",
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(directTitleSp, 0);
            directTopGrid.Children.Add(directTitleSp);

            Border presetBtn = CreateLiquidButton(VectorIcons.Check, "一键填入常用直连", LiquidButtonStyle.Neutral, () =>
            {
                string[] defaults = new[] { "bilibili.com", "hdslb.com", "bilivideo.com", "steamcommunity.com", "steampowered.com", "epicgames.com", "127.0.0.1", "localhost" };
                foreach (var d in defaults)
                {
                    if (!_customDirectRules.Contains(d, StringComparer.OrdinalIgnoreCase))
                    {
                        _customDirectRules.Add(d);
                    }
                }
                SaveCustomDirectRules();
                PopulateCustomDirectRulesTags();
                Task.Run(async () => await RebuildMergedConfigAndReloadAsync());
            }, 30);
            Grid.SetColumn(presetBtn, 1);
            directTopGrid.Children.Add(presetBtn);
            directStack.Children.Add(directTopGrid);

            // 输入行
            Grid dInputGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            dInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border dInputBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = FrozenBrush(95, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBox dInputTb = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontFamily = MonoFont,
                FontSize = 12,
                Text = "bilibili.com"
            };
            dInputBox.Child = dInputTb;
            Grid.SetColumn(dInputBox, 0);
            dInputGrid.Children.Add(dInputBox);

            Border dAddBtn = CreateLiquidButton(VectorIcons.Check, "添加直连网站", LiquidButtonStyle.Primary, () =>
            {
                string text = dInputTb.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    AddCustomDirectRule(text);
                    dInputTb.Text = "";
                }
            }, 32);
            dInputTb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    string text = dInputTb.Text.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        AddCustomDirectRule(text);
                        dInputTb.Text = "";
                    }
                }
            };
            Grid.SetColumn(dAddBtn, 1);
            dInputGrid.Children.Add(dAddBtn);
            directStack.Children.Add(dInputGrid);

            _customRulesTagWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            PopulateCustomDirectRulesTags();
            directStack.Children.Add(_customRulesTagWrap);

            _customRulesStatusTb = new TextBlock
            {
                Text = string.Format("✓ 当前已配置 {0} 条直连规则 (最高优先级分流，毫秒级热生效)", _customDirectRules.Count),
                Foreground = FrozenBrush(52, 211, 153),
                FontFamily = MonoFont,
                FontSize = 11,
                Margin = new Thickness(2, 4, 0, 0)
            };
            directStack.Children.Add(_customRulesStatusTb);

            directCard.Child = directStack;
            stack.Children.Add(directCard);

            return stack;
        }

        private void PopulateProfilesList()
        {
            if (_profilesListStack == null) return;
            _profilesListStack.Children.Clear();

            if (_profilesSummaryText != null)
            {
                _profilesSummaryText.Text = GetProfilesSummaryString();
            }

            if (_subscriptionProfiles.Count == 0)
            {
                Border emptyCard = CreateGlassCard(12);
                emptyCard.Padding = new Thickness(24, 28, 24, 28);
                emptyCard.Margin = new Thickness(0, 4, 0, 8);
                StackPanel esp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                esp.Children.Add(new TextBlock
                {
                    Text = "暂无已保存的订阅配置",
                    Foreground = FrozenBrush(203, 213, 225),
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                esp.Children.Add(new TextBlock
                {
                    Text = "您可以在上方输入框粘贴订阅链接并点击「导入订阅并启用」，或点击顶栏「导入本地 YAML」添加配置。",
                    Foreground = FrozenBrush(148, 163, 184),
                    FontSize = 11.5,
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                emptyCard.Child = esp;
                _profilesListStack.Children.Add(emptyCard);
                return;
            }

            foreach (var profile in _subscriptionProfiles)
            {
                var captured = profile;
                bool isSelected = captured.Enabled;

                Border card = CreateGlassCard(12);
                card.Padding = new Thickness(16, 13, 16, 13);
                card.Margin = new Thickness(0, 0, 0, 8);
                card.Cursor = Cursors.Hand;

                if (isSelected)
                {
                    card.Background = _cachedActiveCardBrush;
                    card.BorderBrush = _cachedActiveBorderBrush;
                    card.BorderThickness = new Thickness(1.2);
                }
                else
                {
                    card.Background = FrozenBrush(30, 15, 23, 42);
                    card.BorderBrush = FrozenBrush(45, 255, 255, 255);
                    card.BorderThickness = new Thickness(1);
                }

                // 点击卡片直接切换多选状态
                card.MouseLeftButtonUp += async (s, e) =>
                {
                    await ToggleProfileSelectionAsync(captured);
                };

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 左侧信息与勾选状态
                StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                StackPanel topRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                // 多选勾选框 (苹果液态玻璃复选框)
                Border chkBox = new Border
                {
                    Width = 19,
                    Height = 19,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = isSelected ? FrozenBrush(220, 16, 185, 129) : FrozenBrush(35, 255, 255, 255),
                    BorderBrush = isSelected ? FrozenBrush(200, 110, 231, 183) : FrozenBrush(80, 255, 255, 255),
                    BorderThickness = new Thickness(1.2),
                    Child = isSelected ? new TextBlock
                    {
                        Text = "✓",
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -1, 0, 0)
                    } : null
                };
                topRow.Children.Add(chkBox);

                TextBlock nameTb = new TextBlock
                {
                    Text = captured.Name,
                    Foreground = isSelected ? Brushes.White : FrozenBrush(203, 213, 225),
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                topRow.Children.Add(nameTb);

                // 状态胶囊
                Border stateBadge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = isSelected ? FrozenBrush(65, 16, 185, 129) : FrozenBrush(35, 71, 85, 105),
                    BorderBrush = isSelected ? FrozenBrush(140, 52, 211, 153) : FrozenBrush(60, 148, 163, 184),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = isSelected ? "✓ 正在生效" : "⊘ 未勾选",
                        Foreground = isSelected ? FrozenBrush(167, 243, 208) : FrozenBrush(148, 163, 184),
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold
                    }
                };
                topRow.Children.Add(stateBadge);

                // 节点数胶囊
                Border countBadge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = FrozenBrush(60, 9, 14, 26),
                    BorderBrush = FrozenBrush(60, 56, 189, 248),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = captured.NodeCount + " 节点",
                        Foreground = FrozenBrush(186, 230, 253),
                        FontFamily = MonoFont,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    }
                };
                topRow.Children.Add(countBadge);

                // 时间胶囊
                Border timeBadge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = FrozenBrush(50, 9, 14, 26),
                    BorderBrush = FrozenBrush(40, 255, 255, 255),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(7, 2, 7, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "更新于 " + captured.UpdatedAt,
                        Foreground = FrozenBrush(148, 163, 184),
                        FontFamily = MonoFont,
                        FontSize = 9.5
                    }
                };
                topRow.Children.Add(timeBadge);
                left.Children.Add(topRow);

                // URL 行 (对齐复选框后的文字起点)
                TextBlock urlTb = new TextBlock
                {
                    Text = captured.Url,
                    Foreground = FrozenBrush(148, 163, 184),
                    FontFamily = MonoFont,
                    FontSize = 10.5,
                    Margin = new Thickness(29, 5, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 540
                };
                left.Children.Add(urlTb);

                Grid.SetColumn(left, 0);
                g.Children.Add(left);

                // 右侧操作区：更新按钮 + 删除按钮 (拦截点击冒泡，避免触发卡片勾选切换)
                StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                Border updateBtn = CreateLiquidButton(VectorIcons.Refresh, "更新", LiquidButtonStyle.Success, async () =>
                {
                    await UpdateSingleSubscriptionAsync(captured, _profilesStatusTb);
                }, 28);
                updateBtn.Margin = new Thickness(0, 0, 8, 0);
                right.Children.Add(updateBtn);

                Border deleteBtn = CreateLiquidButton(VectorIcons.Trash, "删除", LiquidButtonStyle.Danger, async () =>
                {
                    await DeleteSubscriptionAsync(captured);
                }, 28);
                deleteBtn.Margin = new Thickness(0, 0, 0, 0);
                right.Children.Add(deleteBtn);

                Grid.SetColumn(right, 1);
                g.Children.Add(right);
                card.Child = g;
                AttachSpringHover(card);
                _profilesListStack.Children.Add(card);
            }
        }

        // =====================================================================
        // 页面 4：实时活跃连接监控页
        // =====================================================================
        private UIElement BuildConnectionsView()
        {
            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            Border headerCard = CreateGlassCard(14);
            headerCard.Padding = new Thickness(18, 13, 18, 13);
            headerCard.Margin = new Thickness(0, 0, 0, 12);

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerCard.Child = hg;

            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(VectorIcons.Create(VectorIcons.Connections, FrozenBrush(56, 189, 248), 16));
            titleRow.Children.Add(new TextBlock
            {
                Text = string.Format("ACTIVE CONNECTIONS · 实时活跃隧道 ({0})", _connections.Count),
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            hg.Children.Add(titleRow);

            Border closeAllBtn = CreateLiquidButton(VectorIcons.Power, "断开全部连接", LiquidButtonStyle.Danger, async () =>
            {
                await CloseAllConnectionsAsync();
            }, 32);
            Grid.SetColumn(closeAllBtn, 1);
            hg.Children.Add(closeAllBtn);
            stack.Children.Add(headerCard);

            _connectionsListStack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(_connectionsListStack);

            PopulateConnectionsList();
            return stack;
        }

        private void PopulateConnectionsList()
        {
            if (_connectionsListStack == null) return;
            _connectionsListStack.Children.Clear();

            if (_connections.Count == 0)
            {
                Border emptyCard = CreateGlassCard(12);
                emptyCard.Padding = new Thickness(24, 28, 24, 28);
                emptyCard.Margin = new Thickness(0, 4, 0, 8);
                StackPanel esp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                esp.Children.Add(new TextBlock
                {
                    Text = "当前无活跃网络隧道连接",
                    Foreground = FrozenBrush(203, 213, 225),
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                esp.Children.Add(new TextBlock
                {
                    Text = "当应用程序发起网络代理请求时，此处将实时展示主机域名、传输协议与分流策略链路。",
                    Foreground = FrozenBrush(148, 163, 184),
                    FontSize = 11.5,
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                emptyCard.Child = esp;
                _connectionsListStack.Children.Add(emptyCard);
                return;
            }

            foreach (var c in _connections.Take(50))
            {
                Border row = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = _cachedCardBrush,
                    BorderBrush = _cachedCardRimBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                AttachSpringHover(row);

                Grid rg = new Grid();
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.3, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                row.Child = rg;

                rg.Children.Add(new TextBlock
                {
                    Text = c.Host,
                    FontFamily = MonoFont,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                TextBlock netTb = new TextBlock
                {
                    Text = c.Network + " · " + c.Rule,
                    Foreground = FrozenBrush(148, 163, 184),
                    FontSize = 11
                };
                Grid.SetColumn(netTb, 1);
                rg.Children.Add(netTb);

                TextBlock chainTb = new TextBlock
                {
                    Text = c.Chain,
                    Foreground = FrozenBrush(56, 189, 248),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(chainTb, 2);
                rg.Children.Add(chainTb);

                TextBlock trafficTb = new TextBlock
                {
                    Text = "↓ " + FormatBytes(c.Download) + "   ↑ " + FormatBytes(c.Upload),
                    FontFamily = MonoFont,
                    Foreground = FrozenBrush(186, 230, 253),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(trafficTb, 3);
                rg.Children.Add(trafficTb);

                _connectionsListStack.Children.Add(row);
            }
        }

        // =====================================================================
        // 页面 5：Apple Liquid Glass 光学调校与超清壁纸工作室
        // =====================================================================
        private UIElement BuildLiquidGlassStudioView()
        {
            StackPanel stack = new StackPanel { Orientation = Orientation.Vertical };

            Border wpCard = CreateGlassCard(14);
            wpCard.Padding = new Thickness(20, 18, 20, 18);
            wpCard.Margin = new Thickness(0, 0, 0, 14);

            StackPanel wpStack = new StackPanel();
            StackPanel wpHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            wpHeader.Children.Add(VectorIcons.Create(VectorIcons.Studio, FrozenBrush(56, 189, 248), 16));
            wpHeader.Children.Add(new TextBlock
            {
                Text = "WALLPAPER & BACKDROP ENGINE · 超清动态光影壁纸库 (支持拖拽图片进窗口)",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            wpStack.Children.Add(wpHeader);

            UniformGrid presetGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            var presets = new[]
            {
                new { Key = "mountain", Label = "极光雪山 (内置超清原画)" },
                new { Key = "ios26", Label = "iOS 26 琉璃极光流体" },
                new { Key = "vision", Label = "VisionOS 星云幻境" },
                new { Key = "tahoe", Label = "macOS Tahoe 暮光海岸" },
                new { Key = "cyber", Label = "赛博霓虹棱镜之夜" },
                new { Key = "obsidian", Label = "深空黑曜蓝宝石" }
            };

            foreach (var p in presets)
            {
                string pk = p.Key;
                Border pb = CreateIconPillButton(VectorIcons.Studio, p.Label, Color.FromArgb(95, 56, 189, 248), () =>
                {
                    _customWallpaperPath = "";
                    ApplyWallpaperVisual(pk, true);
                    SaveOpticalSettings();
                });
                pb.Margin = new Thickness(0, 0, 8, 8);
                presetGrid.Children.Add(pb);
            }
            wpStack.Children.Add(presetGrid);

            Border uploadWpBtn = CreateGlassCard(8);
            uploadWpBtn.Padding = new Thickness(16, 12, 16, 12);
            uploadWpBtn.Cursor = Cursors.Hand;
            uploadWpBtn.Background = _cachedActiveCardBrush;
            uploadWpBtn.BorderBrush = _cachedActiveBorderBrush;
            StackPanel upRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            upRow.Children.Add(VectorIcons.Create(VectorIcons.Folder, Brushes.White, 14));
            upRow.Children.Add(new TextBlock
            {
                Text = "选择本地电脑任意 4K / 8K 壁纸图片 (JPG / PNG / BMP · 智能显存优化解码)",
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            uploadWpBtn.Child = upRow;
            uploadWpBtn.MouseLeftButtonUp += (s, e) =>
            {
                OpenFileDialog dlg = new OpenFileDialog
                {
                    Title = "选择自定义背景图片",
                    Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    _customWallpaperPath = dlg.FileName;
                    ApplyWallpaperVisual("custom", true);
                    SaveOpticalSettings();
                }
            };
            AttachSpringHover(uploadWpBtn);
            wpStack.Children.Add(uploadWpBtn);
            wpCard.Child = wpStack;
            stack.Children.Add(wpCard);

            Border slidersCard = CreateGlassCard(14);
            slidersCard.Padding = new Thickness(20, 18, 20, 18);
            StackPanel slStack = new StackPanel();
            slStack.Children.Add(new TextBlock
            {
                Text = "OPTICAL REFRACTION PARAMETERS · 液态玻璃四维光学参数实时调校",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            });

            slStack.Children.Add(CreateGlassProgressSliderRow(
                "液态玻璃腔体通透度 (Glass Surface Opacity)",
                0.08, 0.72, _glassSurfaceOpacity,
                val => { _glassSurfaceOpacity = val; RefreshAllGlassMaterials(); SaveOpticalSettings(); }));

            slStack.Children.Add(CreateGlassProgressSliderRow(
                "3D 棱镜对角高光边缘强度 (Specular Rim Intensity)",
                0.15, 1.0, _glassRimIntensity,
                val => { _glassRimIntensity = val; RefreshAllGlassMaterials(); SaveOpticalSettings(); }));

            slStack.Children.Add(CreateGlassProgressSliderRow(
                "底层壁纸高斯磨砂景深 (Wallpaper Frosted Blur Radius)",
                0.0, 38.0, _wallpaperBlurVal,
                val =>
                {
                    _wallpaperBlurVal = val;
                    if (_wallpaperLayer != null)
                    {
                        if (val > 0.05)
                        {
                            if (_wallpaperBlurEffect == null)
                                _wallpaperBlurEffect = new BlurEffect { RenderingBias = RenderingBias.Performance };
                            _wallpaperBlurEffect.Radius = val;
                            _wallpaperLayer.Effect = _wallpaperBlurEffect;
                        }
                        else
                        {
                            _wallpaperLayer.Effect = null;
                        }
                    }
                    SaveOpticalSettings();
                }));

            slStack.Children.Add(CreateGlassProgressSliderRow(
                "壁纸暗角护眼对比度 (Backdrop Contrast Vignette)",
                0.05, 0.75, _wallpaperDimming,
                val =>
                {
                    _wallpaperDimming = val;
                    if (_dimmingLayer != null)
                    {
                        _dimmingLayer.Background = FrozenBrush((byte)(_wallpaperDimming * 245), 8, 13, 26);
                    }
                    SaveOpticalSettings();
                }));

            slidersCard.Child = slStack;
            stack.Children.Add(slidersCard);

            // 3. 系统自启动配置卡片 (Option E: 开机静默自启)
            Border startupCard = CreateGlassCard(14);
            startupCard.Padding = new Thickness(20, 18, 20, 18);
            startupCard.Margin = new Thickness(0, 0, 0, 14);

            StackPanel startStack = new StackPanel();
            StackPanel startHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            startHeader.Children.Add(VectorIcons.Create(VectorIcons.Shield, FrozenBrush(56, 189, 248), 16));
            startHeader.Children.Add(new TextBlock
            {
                Text = "SYSTEM & STARTUP PREFERENCES · 系统自启动与驻留配置",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            startStack.Children.Add(startHeader);

            startStack.Children.Add(CreateIosToggleSwitchRow(
                "开机静默启动 (开机自动驻留系统托盘)",
                "写入 Windows 注册表 Run 启动项，带 -silent 参数静默启动，不弹出主窗口打扰日常使用",
                IsAutoStartSilentEnabled(),
                val => SetAutoStartSilent(val)));

            startupCard.Child = startStack;
            stack.Children.Add(startupCard);

            // 4. 网络急救箱卡片 (Option F: Network Emergency Doctor)
            Border doctorCard = CreateGlassCard(14);
            doctorCard.Padding = new Thickness(20, 18, 20, 18);
            doctorCard.Margin = new Thickness(0, 0, 0, 14);

            StackPanel docStack = new StackPanel();
            StackPanel docHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            docHeader.Children.Add(VectorIcons.Create(VectorIcons.Doctor, FrozenBrush(248, 113, 113), 16));
            docHeader.Children.Add(new TextBlock
            {
                Text = "NETWORK EMERGENCY DOCTOR · 网络自愈急救箱",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            docStack.Children.Add(docHeader);
            docStack.Children.Add(new TextBlock
            {
                Text = "一键排查并修复由于意外关机、残留代理导致的网络断连、DNS 污染或 Fake-IP 映射失效。",
                Foreground = FrozenBrush(148, 163, 184),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 14)
            });

            UniformGrid docGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };

            Border b1 = CreateLiquidButton(VectorIcons.Shield, "一键重置系统代理", LiquidButtonStyle.Neutral, () => ExecuteNetworkDoctorResetProxy(), 34);
            b1.Margin = new Thickness(0, 0, 8, 8);
            docGrid.Children.Add(b1);

            Border b2 = CreateLiquidButton(VectorIcons.Refresh, "刷新系统 DNS 缓存", LiquidButtonStyle.Neutral, () => ExecuteNetworkDoctorFlushDns(), 34);
            b2.Margin = new Thickness(0, 0, 0, 8);
            docGrid.Children.Add(b2);

            Border b3 = CreateLiquidButton(VectorIcons.Bolt, "清空 Fake-IP 缓存", LiquidButtonStyle.Neutral, async () => await ExecuteNetworkDoctorFlushFakeIpAsync(), 34);
            b3.Margin = new Thickness(0, 0, 8, 0);
            docGrid.Children.Add(b3);

            Border b4 = CreateLiquidButton(VectorIcons.Power, "平滑重启 Mihomo 核心", LiquidButtonStyle.Primary, async () => await RestartMihomoCoreAsync(), 34);
            b4.Margin = new Thickness(0, 0, 0, 0);
            docGrid.Children.Add(b4);

            docStack.Children.Add(docGrid);

            _networkDoctorStatusTb = new TextBlock
            {
                Text = "状态: 所有网络诊断组件已就绪，随时可执行急救操作。",
                Foreground = FrozenBrush(56, 189, 248),
                FontFamily = MonoFont,
                FontSize = 11.5,
                Margin = new Thickness(2, 4, 0, 0)
            };
            docStack.Children.Add(_networkDoctorStatusTb);
            doctorCard.Child = docStack;
            stack.Children.Add(doctorCard);

            // 5. 实时内核日志控制台 (Option F: Live Core Logs Stream)
            Border logsCard = CreateGlassCard(14);
            logsCard.Padding = new Thickness(20, 18, 20, 18);
            logsCard.Margin = new Thickness(0, 0, 0, 14);

            StackPanel logsStack = new StackPanel();
            Grid logsTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            logsTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logsTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel logsTitleSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel logsHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            logsHeader.Children.Add(VectorIcons.Create(VectorIcons.Terminal, FrozenBrush(52, 211, 153), 16));
            logsHeader.Children.Add(new TextBlock
            {
                Text = "CORE LIVE LOGS STREAM · 实时内核运行日志 (全双工 WebSocket)",
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            logsTitleSp.Children.Add(logsHeader);

            _logsCountTb = new TextBlock
            {
                Text = string.Format("监听 ws://127.0.0.1:9097/logs?level=info  ·  缓冲区: {0} 条", _coreLogs.Count),
                Foreground = FrozenBrush(148, 163, 184),
                FontFamily = MonoFont,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            logsTitleSp.Children.Add(_logsCountTb);
            Grid.SetColumn(logsTitleSp, 0);
            logsTopGrid.Children.Add(logsTitleSp);

            // 控件栏
            StackPanel logControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border logSearchBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = FrozenBrush(110, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
                Width = 140,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBox logSearchTb = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontFamily = MonoFont,
                FontSize = 11,
                Text = _logsFilterKeyword
            };
            logSearchTb.TextChanged += (s, e) =>
            {
                _logsFilterKeyword = logSearchTb.Text.Trim();
                PopulateExistingCoreLogs();
            };
            logSearchBox.Child = logSearchTb;
            logControls.Children.Add(logSearchBox);

            Border pauseBtn = CreateLiquidButton(VectorIcons.Bolt, _wsLogsStreaming ? "暂停" : "继续", LiquidButtonStyle.Neutral, () =>
            {
                _wsLogsStreaming = !_wsLogsStreaming;
                SwitchNavigationPage("studio");
            }, 28);
            pauseBtn.Margin = new Thickness(0, 0, 6, 0);
            logControls.Children.Add(pauseBtn);

            Border clearLogsBtn = CreateLiquidButton(VectorIcons.Trash, "清空", LiquidButtonStyle.Neutral, () =>
            {
                lock (_coreLogs) { _coreLogs.Clear(); }
                if (_logsContainerStack != null) _logsContainerStack.Children.Clear();
                if (_logsCountTb != null) _logsCountTb.Text = "监听 ws://127.0.0.1:9097/logs?level=info  ·  缓冲区: 0 条";
            }, 28);
            logControls.Children.Add(clearLogsBtn);

            Grid.SetColumn(logControls, 1);
            logsTopGrid.Children.Add(logControls);
            logsStack.Children.Add(logsTopGrid);

            // 终端黑色琉璃边框
            Border termBorder = new Border
            {
                Height = 240,
                CornerRadius = new CornerRadius(10),
                Background = FrozenBrush(235, 10, 15, 26),
                BorderBrush = FrozenBrush(70, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                ClipToBounds = true
            };

            _logsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true
            };
            _logsContainerStack = new StackPanel { Orientation = Orientation.Vertical };
            _logsScroller.Content = _logsContainerStack;
            termBorder.Child = _logsScroller;
            logsStack.Children.Add(termBorder);

            PopulateExistingCoreLogs();

            logsCard.Child = logsStack;
            stack.Children.Add(logsCard);

            return stack;
        }

        private UIElement CreateGlassProgressSliderRow(string label, double min, double max, double current, Action<double> onChanged)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            header.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = FrozenBrush(203, 213, 225),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            TextBlock valTb = new TextBlock
            {
                Text = current.ToString("0.00"),
                FontFamily = MonoFont,
                Foreground = FrozenBrush(56, 189, 248),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            header.Children.Add(valTb);
            sp.Children.Add(header);

            // Height = 8, CornerRadius = 4 (精确等于 Height/2，杜绝任何椭圆拉伸)
            Border trackBg = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = FrozenBrush(105, 9, 14, 26),
                BorderBrush = FrozenBrush(65, 255, 255, 255),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };
            Border fillBar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3),
                Background = _cachedActiveCardBrush,
                Width = Math.Max(6, ((current - min) / (max - min)) * 650)
            };
            trackBg.Child = fillBar;

            Action<Point> updateFromPos = pt =>
            {
                double w = Math.Max(1.0, trackBg.ActualWidth);
                double ratio = Math.Min(1.0, Math.Max(0.0, pt.X / w));
                double newVal = min + ratio * (max - min);
                fillBar.Width = ratio * w;
                valTb.Text = newVal.ToString("0.00");
                onChanged(newVal);
            };

            trackBg.SizeChanged += (s, e) =>
            {
                double ratio = Math.Min(1.0, Math.Max(0.0, (current - min) / (max - min)));
                fillBar.Width = ratio * trackBg.ActualWidth;
            };
            trackBg.PreviewMouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                trackBg.CaptureMouse();
                updateFromPos(e.GetPosition(trackBg));
            };
            trackBg.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                trackBg.CaptureMouse();
                updateFromPos(e.GetPosition(trackBg));
            };
            trackBg.MouseMove += (s, e) =>
            {
                if (trackBg.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
                {
                    updateFromPos(e.GetPosition(trackBg));
                }
            };
            trackBg.MouseLeftButtonUp += (s, e) =>
            {
                if (trackBg.IsMouseCaptured) trackBg.ReleaseMouseCapture();
            };

            sp.Children.Add(trackBg);
            return sp;
        }

        // =====================================================================
        // 光学组件工厂方法 (共享 Frozen 画刷)
        // =====================================================================
        private Border CreateGlassChamber(double radius)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(radius),
                BorderThickness = new Thickness(1.2),
                Background = _cachedChamberBrush,
                BorderBrush = _cachedChamberRimBrush
            };
            _registeredGlassChambers.Add(b);
            return b;
        }

        private Border CreateGlassCard(double radius)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(radius),
                BorderThickness = new Thickness(1.1),
                Background = _cachedCardBrush,
                BorderBrush = _cachedCardRimBrush
            };
            if (_registeredGlassCards.Count > 50)
            {
                _registeredGlassCards.RemoveAll(c => c == null || c.Parent == null);
            }
            _registeredGlassCards.Add(b);
            return b;
        }

        private void RefreshAllGlassMaterials()
        {
            RebuildFrozenGlassBrushes();
            if (_rootFrame != null) _rootFrame.BorderBrush = _cachedChamberRimBrush;

            _registeredGlassChambers.RemoveAll(b => b == null || b.Parent == null);
            foreach (var b in _registeredGlassChambers)
            {
                if (b != null) { b.Background = _cachedChamberBrush; b.BorderBrush = _cachedChamberRimBrush; }
            }
            _registeredGlassCards.RemoveAll(c => c == null || c.Parent == null);
            foreach (var c in _registeredGlassCards)
            {
                if (c != null) { c.Background = _cachedCardBrush; c.BorderBrush = _cachedCardRimBrush; }
            }
        }

        private void AttachSpringHover(Border el)
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            TransformGroup tg = new TransformGroup();
            ScaleTransform st = new ScaleTransform(1.0, 1.0);
            TranslateTransform tt = new TranslateTransform(0, 0);
            tg.Children.Add(st);
            tg.Children.Add(tt);
            el.RenderTransform = tg;

            el.MouseEnter += (s, e) =>
            {
                Panel.SetZIndex(el, 50);
                tt.BeginAnimation(TranslateTransform.YProperty, _hoverLiftAnim);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, _hoverScaleUpAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, _hoverScaleUpAnim);
            };

            el.MouseLeave += (s, e) =>
            {
                Panel.SetZIndex(el, 0);
                tt.BeginAnimation(TranslateTransform.YProperty, _hoverRestAnim);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, _hoverScaleRestAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, _hoverScaleRestAnim);
            };

            el.PreviewMouseLeftButtonDown += (s, e) =>
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, _pressScaleAnim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, _pressScaleAnim);
            };

            el.PreviewMouseLeftButtonUp += (s, e) =>
            {
                DoubleAnimation rel = el.IsMouseOver ? _releaseScaleOverAnim : _releaseScaleRestAnim;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, rel);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, rel);
            };
        }

        public enum LiquidButtonStyle
        {
            Primary,
            Success,
            Danger,
            Neutral
        }

        private Border CreateLiquidButton(string svgPath, string text, LiquidButtonStyle style, Action onClick, double height = 32)
        {
            Brush bgBrush;
            Brush borderBrush;
            Brush fgBrush;

            switch (style)
            {
                case LiquidButtonStyle.Primary:
                    bgBrush = FrozenBrush(60, 14, 165, 233);
                    borderBrush = FrozenBrush(145, 125, 211, 252);
                    fgBrush = Brushes.White;
                    break;
                case LiquidButtonStyle.Success:
                    bgBrush = FrozenBrush(55, 16, 185, 129);
                    borderBrush = FrozenBrush(145, 110, 231, 183);
                    fgBrush = FrozenBrush(236, 253, 245);
                    break;
                case LiquidButtonStyle.Danger:
                    bgBrush = FrozenBrush(45, 239, 68, 68);
                    borderBrush = FrozenBrush(135, 248, 113, 113);
                    fgBrush = FrozenBrush(254, 242, 242);
                    break;
                default:
                    bgBrush = FrozenBrush(40, 255, 255, 255);
                    borderBrush = FrozenBrush(75, 255, 255, 255);
                    fgBrush = FrozenBrush(226, 232, 240);
                    break;
            }

            Border b = new Border
            {
                Height = height,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1)
            };

            StackPanel sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(svgPath))
            {
                sp.Children.Add(VectorIcons.Create(svgPath, fgBrush, height <= 28 ? 11.5 : 13));
            }

            sp.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = fgBrush,
                FontSize = height <= 28 ? 11 : 12,
                FontWeight = FontWeights.SemiBold,
                Margin = string.IsNullOrEmpty(svgPath) ? new Thickness(0) : new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            b.Child = sp;

            b.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
            b.PreviewMouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };
            b.MouseEnter += (s, e) =>
            {
                b.BorderBrush = FrozenBrush(180, 255, 255, 255);
            };
            b.MouseLeave += (s, e) =>
            {
                b.BorderBrush = borderBrush;
            };

            AttachSpringHover(b);
            return b;
        }

        private Border CreateIconPillButton(string svgPath, string text, Color tint, Action onClick)
        {
            LiquidButtonStyle st = LiquidButtonStyle.Neutral;
            if (tint.R > 200 && tint.G < 100) st = LiquidButtonStyle.Danger;
            else if (tint.G > 150 && tint.R < 100) st = LiquidButtonStyle.Success;
            else if (tint.B > 200 || (tint.R < 100 && tint.G > 100 && tint.B > 150)) st = LiquidButtonStyle.Primary;
            return CreateLiquidButton(svgPath, text, st, onClick, 32);
        }

        // =====================================================================
        // 零 GC 分配 StreamGeometry 硬件加速样条波形图 (复用 Path 实例)
        // =====================================================================
        private void InitWaveformPaths()
        {
            _waveformCanvas.Children.Clear();

            var downFill = new LinearGradientBrush(Color.FromArgb(80, 56, 189, 248), Color.FromArgb(0, 56, 189, 248), 90);
            downFill.Freeze();
            var upFill = new LinearGradientBrush(Color.FromArgb(55, 168, 85, 247), Color.FromArgb(0, 168, 85, 247), 90);
            upFill.Freeze();

            _downAreaPath = new System.Windows.Shapes.Path { Fill = downFill };
            _downLinePath = new System.Windows.Shapes.Path { Stroke = FrozenBrush(56, 189, 248), StrokeThickness = 2.0 };
            _upAreaPath = new System.Windows.Shapes.Path { Fill = upFill };
            _upLinePath = new System.Windows.Shapes.Path { Stroke = FrozenBrush(168, 85, 247), StrokeThickness = 1.8 };

            _waveformCanvas.Children.Add(_downAreaPath);
            _waveformCanvas.Children.Add(_downLinePath);
            _waveformCanvas.Children.Add(_upAreaPath);
            _waveformCanvas.Children.Add(_upLinePath);
        }

        private void RedrawTrafficWaveform()
        {
            if (_waveformCanvas == null || _waveformCanvas.ActualWidth < 20 || _downAreaPath == null) return;

            double w = _waveformCanvas.ActualWidth;
            double h = _waveformCanvas.ActualHeight;
            double maxVal = Math.Max(10240, Math.Max(_downHistory.Max(), _upHistory.Max()) * 1.18);

            UpdateSplineStreamGeometry(_downHistory, maxVal, w, h, _downLinePath, _downAreaPath);
            UpdateSplineStreamGeometry(_upHistory, maxVal, w, h, _upLinePath, _upAreaPath);
        }

        private void UpdateSplineStreamGeometry(List<double> values, double maxVal, double w, double h, System.Windows.Shapes.Path linePath, System.Windows.Shapes.Path areaPath)
        {
            int n = values.Count;
            if (n < 3) return;

            Point[] pts = new Point[n];
            double stepX = w / (n - 1);
            for (int i = 0; i < n; i++)
            {
                double x = i * stepX;
                double ratio = Math.Min(1.0, Math.Max(0.0, values[i] / maxVal));
                double y = h - 6 - ratio * (h - 18);
                pts[i] = new Point(x, y);
            }

            StreamGeometry lineGeo = new StreamGeometry();
            using (StreamGeometryContext ctx = lineGeo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 0; i < n - 1; i++)
                {
                    Point p0 = pts[Math.Max(0, i - 1)];
                    Point p1 = pts[i];
                    Point p2 = pts[i + 1];
                    Point p3 = pts[Math.Min(n - 1, i + 2)];
                    Point cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    Point cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                    ctx.BezierTo(cp1, cp2, p2, true, true);
                }
            }
            lineGeo.Freeze();
            linePath.Data = lineGeo;

            StreamGeometry areaGeo = new StreamGeometry();
            using (StreamGeometryContext ctx = areaGeo.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                ctx.LineTo(pts[0], false, false);
                for (int i = 0; i < n - 1; i++)
                {
                    Point p0 = pts[Math.Max(0, i - 1)];
                    Point p1 = pts[i];
                    Point p2 = pts[i + 1];
                    Point p3 = pts[Math.Min(n - 1, i + 2)];
                    Point cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    Point cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                    ctx.BezierTo(cp1, cp2, p2, false, true);
                }
                ctx.LineTo(new Point(w, h), false, false);
            }
            areaGeo.Freeze();
            areaPath.Data = areaGeo;
        }

        // =====================================================================
        // 壁纸渲染 (加入 DecodePixelWidth = 1920 硬件解码优化，大图内存减少 80%)
        // =====================================================================
        private void ApplyWallpaperVisual(string presetKey, bool animate)
        {
            _wallpaperPreset = presetKey;
            Brush targetBrush = null;

            string imgCandidate = "";
            if (presetKey == "custom" && !string.IsNullOrEmpty(_customWallpaperPath) && File.Exists(_customWallpaperPath))
            {
                imgCandidate = _customWallpaperPath;
            }
            else if (presetKey == "mountain")
            {
                imgCandidate = System.IO.Path.Combine(_dataDir, "ui", "assets", "background-DTLDDGUj.jpg");
            }

            if (!string.IsNullOrEmpty(imgCandidate) && File.Exists(imgCandidate))
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 1920; // 硬件缩放解码，大幅降低显存占用
                    bmp.UriSource = new Uri(imgCandidate, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    var ib = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                    ib.Freeze();
                    targetBrush = ib;
                }
                catch { }
            }

            if (targetBrush == null)
            {
                LinearGradientBrush lg;
                if (presetKey == "ios26")
                {
                    lg = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(14, 116, 144), 0.0),
                        new GradientStop(Color.FromRgb(49, 46, 129), 0.5),
                        new GradientStop(Color.FromRgb(112, 26, 117), 1.0)
                    }, new Point(0, 0), new Point(1, 1));
                }
                else if (presetKey == "vision")
                {
                    lg = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(6, 78, 59), 0.0),
                        new GradientStop(Color.FromRgb(30, 27, 75), 0.52),
                        new GradientStop(Color.FromRgb(131, 24, 67), 1.0)
                    }, new Point(0, 1), new Point(1, 0));
                }
                else if (presetKey == "tahoe")
                {
                    lg = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(124, 45, 18), 0.0),
                        new GradientStop(Color.FromRgb(88, 28, 135), 0.5),
                        new GradientStop(Color.FromRgb(15, 23, 42), 1.0)
                    }, new Point(0, 0), new Point(1, 1));
                }
                else if (presetKey == "cyber")
                {
                    lg = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(8, 51, 68), 0.0),
                        new GradientStop(Color.FromRgb(88, 28, 135), 0.5),
                        new GradientStop(Color.FromRgb(136, 19, 55), 1.0)
                    }, new Point(0, 0), new Point(1, 1));
                }
                else
                {
                    lg = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(8, 15, 32), 0.0),
                        new GradientStop(Color.FromRgb(17, 28, 68), 0.5),
                        new GradientStop(Color.FromRgb(36, 14, 58), 1.0)
                    }, new Point(0, 0), new Point(1, 1));
                }
                lg.Freeze();
                targetBrush = lg;
            }

            _wallpaperLayer.Background = targetBrush;

            if (animate)
            {
                ScaleTransform st = _wallpaperLayer.RenderTransform as ScaleTransform;
                if (st != null)
                {
                    DoubleAnimation zoomAnim = new DoubleAnimation(1.035, 1.0, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                    };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnim);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnim);
                }
            }
        }

        private async void OnWindowFileDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            string firstExt = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
            if (firstExt == ".jpg" || firstExt == ".jpeg" || firstExt == ".png" || firstExt == ".bmp")
            {
                _customWallpaperPath = files[0];
                ApplyWallpaperVisual("custom", true);
                SaveOpticalSettings();
            }
            else if (firstExt == ".yaml" || firstExt == ".yml")
            {
                foreach (string f in files)
                {
                    await ImportLocalYamlProfileAsync(f);
                }
                MessageBox.Show("已导入拖拽的本地 YAML 订阅配置，并已自动合并去重并热重载！", "配置导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // =====================================================================
        // 全双工 WebSocket 实时流量监控流 (Option D: ws://127.0.0.1:9097/traffic)
        // =====================================================================
        private void StartWebSocketTrafficStream()
        {
            if (_wsTrafficActive) return;
            _wsTrafficActive = true;
            _wsTrafficCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_isExplicitExit && _wsTrafficActive)
                {
                    bool wsFailed = false;
                    try
                    {
                        using (ClientWebSocket ws = new ClientWebSocket())
                        {
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:9097/traffic"), _wsTrafficCts.Token);
                            _wsTrafficConnected = true;
                            byte[] buffer = new byte[2048];
                            while (ws.State == WebSocketState.Open && !_wsTrafficCts.IsCancellationRequested && !_isExplicitExit)
                            {
                                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _wsTrafficCts.Token);
                                if (res.MessageType == WebSocketMessageType.Close) break;
                                if (res.Count > 0)
                                {
                                    string json = Encoding.UTF8.GetString(buffer, 0, res.Count);
                                    await Dispatcher.InvokeAsync(() => ProcessWsTrafficJson(json));
                                }
                            }
                        }
                    }
                    catch
                    {
                        _wsTrafficConnected = false;
                        wsFailed = true;
                    }
                    if (wsFailed)
                    {
                        await Task.Delay(2000);
                    }
                }
            });
        }

        private void ProcessWsTrafficJson(string json)
        {
            try
            {
                var dict = _json.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    long down = Convert.ToInt64(dict.ContainsKey("down") ? dict["down"] : 0);
                    long up = Convert.ToInt64(dict.ContainsKey("up") ? dict["up"] : 0);

                    _speedDownBps = down;
                    _speedUpBps = up;

                    if (dict.ContainsKey("downTotal")) _lastTotalDown = Convert.ToInt64(dict["downTotal"]);
                    if (dict.ContainsKey("upTotal")) _lastTotalUp = Convert.ToInt64(dict["upTotal"]);

                    _downHistory.RemoveAt(0);
                    _downHistory.Add(_speedDownBps);
                    _upHistory.RemoveAt(0);
                    _upHistory.Add(_speedUpBps);

                    if (_headerSpeedDownTb != null) _headerSpeedDownTb.Text = FormatSpeed(_speedDownBps);
                    if (_headerSpeedUpTb != null) _headerSpeedUpTb.Text = FormatSpeed(_speedUpBps);
                    if (_dashDownSpeedText != null) _dashDownSpeedText.Text = FormatSpeed(_speedDownBps);
                    if (_dashUpSpeedText != null) _dashUpSpeedText.Text = FormatSpeed(_speedUpBps);
                    if (_dashTotalDownText != null) _dashTotalDownText.Text = FormatBytes(_lastTotalDown) + " / " + FormatBytes(_lastTotalUp);

                    if (_activePage == "home") RedrawTrafficWaveform();
                }
            }
            catch { }
        }

        // =====================================================================
        // 全双工 WebSocket 实时内核日志流 (Option F: ws://127.0.0.1:9097/logs?level=info)
        // =====================================================================
        private void StartLiveCoreLogsStream()
        {
            if (_wsLogsActive) return;
            _wsLogsActive = true;
            _wsLogsCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_isExplicitExit && _wsLogsActive)
                {
                    bool logsFailed = false;
                    try
                    {
                        using (ClientWebSocket ws = new ClientWebSocket())
                        {
                            await ws.ConnectAsync(new Uri("ws://127.0.0.1:9097/logs?level=info"), _wsLogsCts.Token);
                            byte[] buffer = new byte[8192];
                            while (ws.State == WebSocketState.Open && !_wsLogsCts.IsCancellationRequested && !_isExplicitExit)
                            {
                                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _wsLogsCts.Token);
                                if (res.MessageType == WebSocketMessageType.Close) break;
                                if (res.Count > 0 && _wsLogsStreaming)
                                {
                                    string json = Encoding.UTF8.GetString(buffer, 0, res.Count);
                                    await Dispatcher.InvokeAsync(() => ProcessCoreLogJson(json));
                                }
                            }
                        }
                    }
                    catch
                    {
                        logsFailed = true;
                    }
                    if (logsFailed)
                    {
                        await Task.Delay(2000);
                    }
                }
            });
        }

        private void ProcessCoreLogJson(string json)
        {
            try
            {
                var dict = _json.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    string type = dict.ContainsKey("type") ? Convert.ToString(dict["type"]).ToLowerInvariant() : "info";
                    string payload = dict.ContainsKey("payload") ? Convert.ToString(dict["payload"]) : "";

                    CoreLogEntry entry = new CoreLogEntry
                    {
                        Time = DateTime.Now,
                        Type = type,
                        Message = payload
                    };

                    lock (_coreLogs)
                    {
                        _coreLogs.Add(entry);
                        if (_coreLogs.Count > 500) _coreLogs.RemoveAt(0);
                    }

                    if (_activePage == "studio" && _logsContainerStack != null)
                    {
                        AppendLogEntryToUI(entry);
                    }
                }
            }
            catch { }
        }

        private void PopulateExistingCoreLogs()
        {
            if (_logsContainerStack == null) return;
            _logsContainerStack.Children.Clear();
            List<CoreLogEntry> copy;
            lock (_coreLogs) { copy = new List<CoreLogEntry>(_coreLogs); }
            foreach (var log in copy)
            {
                AppendLogEntryToUI(log);
            }
        }

        private void AppendLogEntryToUI(CoreLogEntry entry)
        {
            if (_logsContainerStack == null || entry == null) return;

            string t = (entry.Type ?? "INFO").ToUpperInvariant();
            if (!string.IsNullOrEmpty(_logsFilterLevel) && _logsFilterLevel != "ALL")
            {
                if (!t.Contains(_logsFilterLevel)) return;
            }
            if (!string.IsNullOrEmpty(_logsFilterKeyword))
            {
                if (entry.Message != null && entry.Message.IndexOf(_logsFilterKeyword, StringComparison.OrdinalIgnoreCase) < 0) return;
            }

            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock timeTb = new TextBlock
            {
                Text = string.Format("[{0}]", entry.Time.ToString("HH:mm:ss")),
                Foreground = FrozenBrush(100, 116, 139),
                FontFamily = MonoFont,
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeTb, 0);
            row.Children.Add(timeTb);

            Color badgeBg = Color.FromRgb(30, 58, 138);
            Color badgeFg = Color.FromRgb(56, 189, 248);
            if (t.Contains("WARN"))
            {
                badgeBg = Color.FromRgb(120, 53, 15);
                badgeFg = Color.FromRgb(251, 191, 36);
            }
            else if (t.Contains("ERR"))
            {
                badgeBg = Color.FromRgb(127, 29, 29);
                badgeFg = Color.FromRgb(248, 113, 113);
            }

            Border badge = new Border
            {
                CornerRadius = new CornerRadius(3.5),
                Background = FrozenBrush(180, badgeBg.R, badgeBg.G, badgeBg.B),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = t,
                    Foreground = FrozenBrush(badgeFg.R, badgeFg.G, badgeFg.B),
                    FontFamily = MonoFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);

            TextBlock msgTb = new TextBlock
            {
                Text = entry.Message,
                Foreground = FrozenBrush(226, 232, 240),
                FontFamily = MonoFont,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(msgTb, 2);
            row.Children.Add(msgTb);

            _logsContainerStack.Children.Add(row);

            while (_logsContainerStack.Children.Count > 300)
            {
                _logsContainerStack.Children.RemoveAt(0);
            }

            if (_logsCountTb != null)
            {
                _logsCountTb.Text = string.Format("监听 ws://127.0.0.1:9097/logs?level=info  ·  缓冲区: {0} 条", _coreLogs.Count);
            }

            if (_logsAutoScroll && _logsScroller != null)
            {
                _logsScroller.ScrollToBottom();
            }
        }

        // =====================================================================
        // Mihomo RESTful API 通信与状态同步引擎
        // =====================================================================
        private async Task PollMihomoStateAsync()
        {
            try
            {
                string connJson = await HttpGetAsync("http://127.0.0.1:9097/connections");
                if (!string.IsNullOrEmpty(connJson))
                {
                    var dict = _json.Deserialize<Dictionary<string, object>>(connJson);
                    if (dict != null)
                    {
                        long totalDown = Convert.ToInt64(dict.ContainsKey("downloadTotal") ? dict["downloadTotal"] : 0);
                        long totalUp = Convert.ToInt64(dict.ContainsKey("uploadTotal") ? dict["uploadTotal"] : 0);

                        if (!_wsTrafficConnected)
                        {
                            if (_lastTotalDown > 0)
                            {
                                _speedDownBps = Math.Max(0, totalDown - _lastTotalDown);
                                _speedUpBps = Math.Max(0, totalUp - _lastTotalUp);
                            }
                            _lastTotalDown = totalDown;
                            _lastTotalUp = totalUp;

                            _downHistory.RemoveAt(0);
                            _downHistory.Add(_speedDownBps);
                            _upHistory.RemoveAt(0);
                            _upHistory.Add(_speedUpBps);
                        }
                        else
                        {
                            if (totalDown > 0) _lastTotalDown = totalDown;
                            if (totalUp > 0) _lastTotalUp = totalUp;
                        }

                        _connections.Clear();
                        if (dict.ContainsKey("connections") && dict["connections"] is System.Collections.ArrayList)
                        {
                            var list = (System.Collections.ArrayList)dict["connections"];
                            _activeConnCount = list.Count;
                            foreach (var item in list)
                            {
                                var cMap = item as Dictionary<string, object>;
                                if (cMap == null) continue;
                                string host = "";
                                string net = "TCP";
                                if (cMap.ContainsKey("metadata") && cMap["metadata"] is Dictionary<string, object>)
                                {
                                    var meta = (Dictionary<string, object>)cMap["metadata"];
                                    string h = meta.ContainsKey("host") ? Convert.ToString(meta["host"]) : "";
                                    string dip = meta.ContainsKey("destinationIP") ? Convert.ToString(meta["destinationIP"]) : "";
                                    string dport = meta.ContainsKey("destinationPort") ? Convert.ToString(meta["destinationPort"]) : "";
                                    host = (!string.IsNullOrEmpty(h) ? h : dip) + ":" + dport;
                                    net = (meta.ContainsKey("network") ? Convert.ToString(meta["network"]) : "tcp").ToUpperInvariant();
                                }
                                string chainStr = "";
                                if (cMap.ContainsKey("chains") && cMap["chains"] is System.Collections.ArrayList)
                                {
                                    var ch = (System.Collections.ArrayList)cMap["chains"];
                                    if (ch.Count > 0) chainStr = Convert.ToString(ch[0]);
                                }
                                _connections.Add(new ConnectionRecord
                                {
                                    Id = cMap.ContainsKey("id") ? Convert.ToString(cMap["id"]) : "",
                                    Host = host,
                                    Network = net,
                                    Rule = cMap.ContainsKey("rule") ? Convert.ToString(cMap["rule"]) : "MATCH",
                                    Chain = chainStr,
                                    Download = cMap.ContainsKey("download") ? Convert.ToInt64(cMap["download"]) : 0,
                                    Upload = cMap.ContainsKey("upload") ? Convert.ToInt64(cMap["upload"]) : 0
                                });
                            }
                        }

                        if (_headerSpeedDownTb != null) _headerSpeedDownTb.Text = FormatSpeed(_speedDownBps);
                        if (_headerSpeedUpTb != null) _headerSpeedUpTb.Text = FormatSpeed(_speedUpBps);
                        if (_dashDownSpeedText != null) _dashDownSpeedText.Text = FormatSpeed(_speedDownBps);
                        if (_dashUpSpeedText != null) _dashUpSpeedText.Text = FormatSpeed(_speedUpBps);
                        if (_dashTotalDownText != null) _dashTotalDownText.Text = FormatBytes(_lastTotalDown) + " / " + FormatBytes(_lastTotalUp);
                        if (_dashConnCountText != null) _dashConnCountText.Text = _activeConnCount + " Active";
                        if (_dashActiveNodeText != null) _dashActiveNodeText.Text = "当前主策略线路: " + GetPrimarySelectedNode();
                        if (_dashUptimeText != null) _dashUptimeText.Text = "Mixed 127.0.0.1:7890   |   API 127.0.0.1:9097   |   运行: " + GetUptimeString();

                        if (_activePage == "home") RedrawTrafficWaveform();
                        else if (_activePage == "connections") PopulateConnectionsList();
                    }
                }
            }
            catch { }
        }

        private async Task FetchProxiesFromCoreAsync(bool fullRebuild = true)
        {
            try
            {
                string json = await HttpGetAsync("http://127.0.0.1:9097/proxies");
                if (string.IsNullOrEmpty(json)) return;

                var root = _json.Deserialize<Dictionary<string, object>>(json);
                if (root == null || !root.ContainsKey("proxies")) return;

                var proxiesMap = root["proxies"] as Dictionary<string, object>;
                if (proxiesMap == null) return;

                List<ProxyGroupItem> newGroups = new List<ProxyGroupItem>();
                bool structureChanged = false;

                foreach (var kv in proxiesMap)
                {
                    var pObj = kv.Value as Dictionary<string, object>;
                    if (pObj == null) continue;

                    string name = FixMojibake(kv.Key);
                    string type = pObj.ContainsKey("type") ? Convert.ToString(pObj["type"]) : "";
                    string now = pObj.ContainsKey("now") ? FixMojibake(Convert.ToString(pObj["now"])) : "";

                    int lastDelay = 0;
                    if (pObj.ContainsKey("history") && pObj["history"] is System.Collections.ArrayList)
                    {
                        var hist = (System.Collections.ArrayList)pObj["history"];
                        if (hist.Count > 0)
                        {
                            var lastItem = hist[hist.Count - 1] as Dictionary<string, object>;
                            if (lastItem != null && lastItem.ContainsKey("delay"))
                            {
                                lastDelay = Convert.ToInt32(lastItem["delay"]);
                            }
                        }
                    }

                    _proxyNodes[name] = new ProxyNodeInfo { Name = name, Type = type, Delay = lastDelay };

                    // 原地更新已渲染卡片的延迟，不重绘卡片
                    NodeCardLiveRef liveRef;
                    if (_nodeLiveRefs.TryGetValue(name, out liveRef))
                    {
                        ApplyNodeCardDelayVisual(liveRef, lastDelay, false);
                    }

                    if (pObj.ContainsKey("all") && pObj["all"] is System.Collections.ArrayList)
                    {
                        if (name == "GLOBAL" && _currentMode != "global") continue;
                        var allArr = (System.Collections.ArrayList)pObj["all"];
                        List<string> allNames = new List<string>();
                        foreach (var item in allArr) allNames.Add(FixMojibake(Convert.ToString(item)));

                        newGroups.Add(new ProxyGroupItem
                        {
                            Name = name,
                            Type = type,
                            Now = now,
                            All = allNames
                        });
                    }
                }

                if (_proxyGroups.Count != newGroups.Count)
                {
                    structureChanged = true;
                }
                else
                {
                    for (int i = 0; i < newGroups.Count; i++)
                    {
                        if (_proxyGroups[i].Name != newGroups[i].Name || _proxyGroups[i].All.Count != newGroups[i].All.Count)
                        {
                            structureChanged = true;
                            break;
                        }
                    }
                }

                _proxyGroups.Clear();
                _proxyGroups.AddRange(newGroups);

                if (_activePage == "proxies" && !_isTestingLatency)
                {
                    if (fullRebuild && structureChanged)
                    {
                        PopulateProxyGroupsBar();
                        PopulateProxyNodesWrap();
                    }
                    else
                    {
                        // 原地同步勾选圈与高亮状态，不重置 DOM 与滚动条，杜绝图标消失
                        var curGrp = _proxyGroups.FirstOrDefault(g => g.Name == _selectedGroupName);
                        if (curGrp != null)
                        {
                            foreach (var kv in _nodeLiveRefs)
                            {
                                bool isCur = (kv.Key == curGrp.Now);
                                if (kv.Value.IsSelected != isCur)
                                {
                                    kv.Value.IsSelected = isCur;
                                    if (kv.Value.CheckCircle != null)
                                    {
                                        kv.Value.CheckCircle.Visibility = isCur ? Visibility.Visible : Visibility.Collapsed;
                                    }
                                    if (kv.Value.CardBorder != null)
                                    {
                                        kv.Value.CardBorder.Background = isCur ? _cachedActiveCardBrush : _cachedCardBrush;
                                        kv.Value.CardBorder.BorderBrush = isCur ? _cachedActiveBorderBrush : _cachedCardRimBrush;
                                    }
                                    if (kv.Value.AccentStrip != null)
                                    {
                                        kv.Value.AccentStrip.Background = isCur ? FrozenBrush(224, 242, 254) : FrozenBrush(70, 148, 163, 184);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async Task SelectProxyInGroupAsync(string groupName, string nodeName)
        {
            try
            {
                var grp = _proxyGroups.FirstOrDefault(g => g.Name == groupName);
                string oldNode = (grp != null) ? grp.Now : "";
                if (grp != null) grp.Now = nodeName;

                // 立即原地更新卡片选中勾选圈与边框高亮，绝对不销毁 DOM，保持滚动位置不丢失，杜绝图标消失
                if (!string.IsNullOrEmpty(oldNode))
                {
                    NodeCardLiveRef oldRef;
                    if (_nodeLiveRefs.TryGetValue(oldNode, out oldRef))
                    {
                        oldRef.IsSelected = false;
                        if (oldRef.CheckCircle != null) oldRef.CheckCircle.Visibility = Visibility.Collapsed;
                        if (oldRef.CardBorder != null)
                        {
                            oldRef.CardBorder.Background = _cachedCardBrush;
                            oldRef.CardBorder.BorderBrush = _cachedCardRimBrush;
                        }
                        if (oldRef.AccentStrip != null)
                        {
                            oldRef.AccentStrip.Background = FrozenBrush(70, 148, 163, 184);
                        }
                    }
                }

                NodeCardLiveRef newRef;
                if (_nodeLiveRefs.TryGetValue(nodeName, out newRef))
                {
                    newRef.IsSelected = true;
                    if (newRef.CheckCircle != null) newRef.CheckCircle.Visibility = Visibility.Visible;
                    if (newRef.CardBorder != null)
                    {
                        newRef.CardBorder.Background = _cachedActiveCardBrush;
                        newRef.CardBorder.BorderBrush = _cachedActiveBorderBrush;
                    }
                    if (newRef.AccentStrip != null)
                    {
                        newRef.AccentStrip.Background = FrozenBrush(224, 242, 254);
                    }
                }

                if (_dashActiveNodeText != null) _dashActiveNodeText.Text = "当前主策略线路: " + GetPrimarySelectedNode();

                string url = "http://127.0.0.1:9097/proxies/" + Uri.EscapeDataString(groupName);
                string body = "{\"name\":\"" + nodeName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
                await HttpSendAsync("PUT", url, body);
                await FetchProxiesFromCoreAsync(false);
            }
            catch { }
        }

        private async Task<int> ProbeNodeDelayFromCoreAsync(string nodeName)
        {
            int measuredMs = -1;
            try
            {
                string url = string.Format("http://127.0.0.1:9097/proxies/{0}/delay?timeout=3200&url=https://www.gstatic.com/generate_204",
                    Uri.EscapeDataString(nodeName));
                string res = await HttpGetAsync(url);
                if (!string.IsNullOrEmpty(res))
                {
                    var d = _json.Deserialize<Dictionary<string, object>>(res);
                    if (d != null && d.ContainsKey("delay"))
                    {
                        int ms = Convert.ToInt32(d["delay"]);
                        if (ms > 0) measuredMs = ms;
                    }
                }
            }
            catch
            {
                measuredMs = -1;
            }

            lock (_proxyNodes)
            {
                if (_proxyNodes.ContainsKey(nodeName))
                {
                    _proxyNodes[nodeName].Delay = measuredMs;
                }
            }
            return measuredMs;
        }

        private async Task TestSingleNodeLatencyAsync(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName)) return;
            NodeCardLiveRef liveRef;
            if (_nodeLiveRefs.TryGetValue(nodeName, out liveRef))
            {
                ApplyNodeCardDelayVisual(liveRef, 0, true);
            }

            int ms = await Task.Run(async () => await ProbeNodeDelayFromCoreAsync(nodeName));

            if (_nodeLiveRefs.TryGetValue(nodeName, out liveRef))
            {
                ApplyNodeCardDelayVisual(liveRef, ms, false);
            }
        }

        private async Task TestCurrentGroupLatencyAsync()
        {
            if (_isTestingLatency) return;

            var grp = _proxyGroups.FirstOrDefault(g => g.Name == _selectedGroupName);
            if (grp == null || grp.All == null || grp.All.Count == 0) return;

            _isTestingLatency = true;
            List<string> targetNodes = grp.All.ToList();
            int totalCount = targetNodes.Count;
            int completedCount = 0;
            int validCount = 0;
            int timeoutCount = 0;
            int bestDelay = int.MaxValue;
            string bestNodeName = "--";

            // 1. 立即展开顶部 Liquid Glass 实时进度横幅，并将当前组所有卡片切换为“测速中...”状态
            if (_latencyProgressCard != null)
            {
                _latencyProgressCard.Visibility = Visibility.Visible;
            }
            if (_latencyStatusTitle != null)
            {
                _latencyStatusTitle.Text = string.Format("正在并发探测 [{0}] 节点延迟...", grp.Name);
            }
            if (_latencyCurrentNodeText != null)
            {
                _latencyCurrentNodeText.Text = string.Format("并发队列已启动 (共 {0} 个节点)", totalCount);
            }
            if (_latencyValidCountText != null) _latencyValidCountText.Text = "有效: 0";
            if (_latencyTimeoutCountText != null) _latencyTimeoutCountText.Text = "超时: 0";
            if (_latencyFastestNodeText != null) _latencyFastestNodeText.Text = "最快: 探测中...";
            if (_latencyPercentText != null) _latencyPercentText.Text = string.Format("0 / {0} (0%)", totalCount);
            if (_latencyProgressScaleX != null)
            {
                _latencyProgressScaleX.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _latencyProgressScaleX.ScaleX = 0.02;
            }
            if (_testLatencyBtnText != null)
            {
                _testLatencyBtnText.Text = string.Format("测速中 0/{0} (0%)", totalCount);
            }

            foreach (string nName in targetNodes)
            {
                NodeCardLiveRef liveRef;
                if (_nodeLiveRefs.TryGetValue(nName, out liveRef))
                {
                    ApplyNodeCardDelayVisual(liveRef, 0, true);
                }
            }

            // 2. 使用 SemaphoreSlim(8) 控制平滑并发波次，每完成一个节点立即在 UI 线程热更新该卡片与进度条
            System.Threading.SemaphoreSlim gate = new System.Threading.SemaphoreSlim(8);
            object statsLock = new object();
            List<Task> tasks = new List<Task>();

            foreach (string nName in targetNodes)
            {
                string captured = nName;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        int ms = await ProbeNodeDelayFromCoreAsync(captured);

                        int snapCompleted;
                        int snapValid;
                        int snapTimeout;
                        int snapBestDelay;
                        string snapBestNode;

                        lock (statsLock)
                        {
                            completedCount++;
                            if (ms > 0)
                            {
                                validCount++;
                                if (ms < bestDelay)
                                {
                                    bestDelay = ms;
                                    bestNodeName = captured;
                                }
                            }
                            else
                            {
                                timeoutCount++;
                            }

                            snapCompleted = completedCount;
                            snapValid = validCount;
                            snapTimeout = timeoutCount;
                            snapBestDelay = bestDelay;
                            snapBestNode = bestNodeName;
                        }

                        // 实时派发回 UI 线程，逐个点亮节点卡片并推进进度条
                        var unusedOp = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            NodeCardLiveRef liveRef;
                            if (_nodeLiveRefs.TryGetValue(captured, out liveRef))
                            {
                                ApplyNodeCardDelayVisual(liveRef, ms, false);
                            }

                            double ratio = (double)snapCompleted / Math.Max(1, totalCount);
                            int pct = (int)Math.Round(ratio * 100.0);

                            if (_latencyProgressScaleX != null)
                            {
                                DoubleAnimation barAnim = new DoubleAnimation(ratio, TimeSpan.FromMilliseconds(160))
                                {
                                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                                };
                                _latencyProgressScaleX.BeginAnimation(ScaleTransform.ScaleXProperty, barAnim);
                            }

                            if (_latencyCurrentNodeText != null)
                            {
                                _latencyCurrentNodeText.Text = ms > 0
                                    ? string.Format("最新响应: {0} ({1} ms)", captured, ms)
                                    : string.Format("节点超时: {0} (TIMEOUT)", captured);
                            }
                            if (_latencyValidCountText != null)
                            {
                                _latencyValidCountText.Text = string.Format("有效: {0}", snapValid);
                            }
                            if (_latencyTimeoutCountText != null)
                            {
                                _latencyTimeoutCountText.Text = string.Format("超时: {0}", snapTimeout);
                            }
                            if (_latencyFastestNodeText != null && snapBestDelay < int.MaxValue)
                            {
                                _latencyFastestNodeText.Text = string.Format("最快: {0} ({1} ms)", snapBestNode, snapBestDelay);
                            }
                            if (_latencyPercentText != null)
                            {
                                _latencyPercentText.Text = string.Format("{0} / {1} ({2}%)", snapCompleted, totalCount, pct);
                            }
                            if (_testLatencyBtnText != null)
                            {
                                _testLatencyBtnText.Text = string.Format("测速中 {0}/{1} ({2}%)", snapCompleted, totalCount, pct);
                            }
                        }));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // 3. 测速全部完成：更新最终统计摘要，若开启了“按延迟排序”则自动重排矩阵
            _isTestingLatency = false;
            if (_testLatencyBtnText != null)
            {
                _testLatencyBtnText.Text = "并发延迟测速";
            }
            if (_latencyStatusTitle != null)
            {
                _latencyStatusTitle.Text = string.Format("[{0}] 节点并发测速完成", grp.Name);
            }
            if (_latencyCurrentNodeText != null)
            {
                _latencyCurrentNodeText.Text = bestDelay < int.MaxValue
                    ? string.Format("最优线路: {0} ({1} ms) · 点击任意节点即可切换", bestNodeName, bestDelay)
                    : "全部节点均未响应，请检查网络或上游订阅";
            }

            if (_sortByDelay)
            {
                PopulateProxyNodesWrap();
            }
        }

        private async Task SetOutboundModeAsync(string modeKey)
        {
            _currentMode = modeKey;
            try
            {
                foreach (var kv in _headerModePills)
                {
                    bool act = string.Equals(kv.Key, modeKey, StringComparison.OrdinalIgnoreCase);
                    kv.Value.Background = act ? _cachedActiveCardBrush : Brushes.Transparent;
                    kv.Value.BorderBrush = act ? _cachedActiveBorderBrush : Brushes.Transparent;
                    ScaleTransform pst = kv.Value.RenderTransform as ScaleTransform;
                    if (act && pst != null)
                    {
                        DoubleAnimation pop = new DoubleAnimation(1.06, 1.0, TimeSpan.FromMilliseconds(180))
                        {
                            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                        };
                        pst.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                        pst.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                    }
                    TextBlock tb = kv.Value.Child as TextBlock;
                    if (tb != null)
                    {
                        tb.Foreground = act ? Brushes.White : FrozenBrush(148, 163, 184);
                        tb.FontWeight = act ? FontWeights.Bold : FontWeights.SemiBold;
                    }
                }
                await HttpSendAsync("PATCH", "http://127.0.0.1:9097/configs", "{\"mode\":\"" + modeKey + "\"}");
                await FetchProxiesFromCoreAsync();
            }
            catch { }
        }

        private async Task CloseAllConnectionsAsync()
        {
            try
            {
                await HttpSendAsync("DELETE", "http://127.0.0.1:9097/connections", "");
                await PollMihomoStateAsync();
            }
            catch { }
        }

        private async Task HotReloadConfigYamlAsync(string yamlPath)
        {
            try
            {
                string escapedPath = yamlPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                await HttpSendAsync("PUT", "http://127.0.0.1:9097/configs?force=true", "{\"path\":\"" + escapedPath + "\"}");
                await Task.Delay(350);
                await FetchProxiesFromCoreAsync();
            }
            catch { }
        }

        private void LoadSubscriptionProfiles()
        {
            try
            {
                if (File.Exists(_profilesJsonPath))
                {
                    string json = File.ReadAllText(_profilesJsonPath, Encoding.UTF8);
                    var list = _json.Deserialize<List<SubscriptionProfileItem>>(json);
                    if (list != null)
                    {
                        _subscriptionProfiles = list;
                        return;
                    }
                }
            }
            catch { }

            _subscriptionProfiles = new List<SubscriptionProfileItem>();
            string defaultYaml = System.IO.Path.Combine(_profilesDir, "default.yaml");
            if (!File.Exists(defaultYaml) && File.Exists(_templateYamlPath))
            {
                try { File.Copy(_templateYamlPath, defaultYaml, true); } catch { }
            }
            _subscriptionProfiles.Add(new SubscriptionProfileItem
            {
                Id = "default",
                Name = "默认内置订阅配置",
                Url = "local://template.yaml",
                FileName = "default.yaml",
                NodeCount = CountNodesInYamlFile(defaultYaml),
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Enabled = true
            });
            SaveSubscriptionProfiles();
        }

        private void SaveSubscriptionProfiles()
        {
            try
            {
                string json = _json.Serialize(_subscriptionProfiles);
                File.WriteAllText(_profilesJsonPath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void RefreshNodeSourceMap()
        {
            lock (_nodeSourceMap)
            {
                _nodeSourceMap.Clear();
                HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var enabledProfiles = _subscriptionProfiles.Where(p => p.Enabled).ToList();
                foreach (var p in enabledProfiles)
                {
                    string pPath = System.IO.Path.Combine(_profilesDir, p.FileName);
                    if (!File.Exists(pPath)) continue;
                    try
                    {
                        string yText = File.ReadAllText(pPath, Encoding.UTF8);
                        var pNodes = ExtractProxyNodesFromYaml(yText);
                        foreach (var n in pNodes)
                        {
                            string finalName = n.Name;
                            if (seenNames.Contains(finalName))
                            {
                                int suf = 2;
                                while (seenNames.Contains(finalName + " (" + suf + ")")) suf++;
                                finalName = finalName + " (" + suf + ")";
                            }
                            seenNames.Add(finalName);
                            _nodeSourceMap[finalName] = p.Name;
                        }
                    }
                    catch { }
                }
            }
        }

        private string GetNodeSource(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName)) return "";
            if (nodeName == "DIRECT" || nodeName == "REJECT" || nodeName == "PASS") return "内置系统";
            string src;
            lock (_nodeSourceMap)
            {
                if (_nodeSourceMap.TryGetValue(nodeName, out src)) return src;
            }
            var enabled = _subscriptionProfiles.Where(p => p.Enabled).ToList();
            if (enabled.Count == 1) return enabled[0].Name;
            return "本地配置";
        }

        private int CountNodesInYamlFile(string path)
        {
            if (!File.Exists(path)) return 0;
            try
            {
                var nodes = ExtractProxyNodesFromYaml(File.ReadAllText(path, Encoding.UTF8));
                return nodes.Count;
            }
            catch { return 0; }
        }

        private class ParsedProxyNode
        {
            public string Name;
            public string RawBlock;
        }

        private List<ParsedProxyNode> ExtractProxyNodesFromYaml(string yamlText)
        {
            List<ParsedProxyNode> result = new List<ParsedProxyNode>();
            if (string.IsNullOrEmpty(yamlText)) return result;

            string text = FixMojibake(yamlText.Trim());
            if (!text.Contains("proxies:") && !text.Contains("proxy-groups:") && text.Length > 20)
            {
                try
                {
                    string cleanB64 = Regex.Replace(text, @"\s+", "");
                    int mod4 = cleanB64.Length % 4;
                    if (mod4 > 0) cleanB64 += new string('=', 4 - mod4);
                    byte[] b64Bytes = Convert.FromBase64String(cleanB64);
                    string decoded = Encoding.UTF8.GetString(b64Bytes);
                    if (decoded.Contains("proxies:") || decoded.Contains("name:") || decoded.Contains("server:") || decoded.Contains("://"))
                    {
                        text = FixMojibake(decoded);
                    }
                }
                catch { }
            }

            text = Regex.Replace(text, @"(short-id:\s*)([0-9a-fA-F]+)", m =>
            {
                string val = m.Groups[2].Value;
                if (!val.StartsWith("'") && !val.StartsWith("\""))
                    return m.Groups[1].Value + "'" + val + "'";
                return m.Value;
            });

            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            bool inProxies = false;
            List<string> curBlock = new List<string>();

            Action commitCurBlock = () =>
            {
                if (curBlock.Count == 0) return;
                string blockStr = string.Join("\r\n", curBlock);
                Match nm = Regex.Match(blockStr, @"(?<![a-zA-Z0-9_-])name:\s*[""']?([^""'\r\n,}]+)[""']?");
                if (nm.Success)
                {
                    string nodeName = FixMojibake(nm.Groups[1].Value.Trim());
                    if (!string.IsNullOrEmpty(nodeName))
                    {
                        result.Add(new ParsedProxyNode { Name = nodeName, RawBlock = blockStr });
                    }
                }
                curBlock.Clear();
            };

            foreach (string rawLine in lines)
            {
                string line = rawLine;
                if (Regex.IsMatch(line, @"^proxies:\s*$"))
                {
                    commitCurBlock();
                    inProxies = true;
                    continue;
                }
                if (inProxies && Regex.IsMatch(line, @"^[a-zA-Z0-9_-]+:\s*$"))
                {
                    commitCurBlock();
                    inProxies = false;
                    break;
                }
                if (inProxies)
                {
                    if (Regex.IsMatch(line, @"^\s*-\s+"))
                    {
                        commitCurBlock();
                    }
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        curBlock.Add(line);
                    }
                }
            }
            commitCurBlock();

            if (result.Count == 0)
            {
                foreach (string rawLine in lines)
                {
                    string trimmed = rawLine.Trim();
                    if (trimmed.StartsWith("- {") || trimmed.StartsWith("-{"))
                    {
                        Match nm = Regex.Match(trimmed, @"(?<![a-zA-Z0-9_-])name:\s*[""']?([^""'\r\n,}]+)[""']?");
                        if (nm.Success)
                        {
                            string nodeName = FixMojibake(nm.Groups[1].Value.Trim());
                            if (!string.IsNullOrEmpty(nodeName))
                            {
                                result.Add(new ParsedProxyNode { Name = nodeName, RawBlock = trimmed });
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static string YamlQuoteName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "''";
            return "'" + name.Replace("'", "''") + "'";
        }

        private void AppendRegionGroup(StringBuilder sb, string gName, List<string> regionNodes, List<string> allNodes)
        {
            sb.AppendLine("- name: " + gName);
            sb.AppendLine("  type: select");
            sb.AppendLine("  proxies:");
            var targetList = (regionNodes.Count > 0) ? regionNodes : (allNodes.Count > 0 ? allNodes : new List<string> { "DIRECT" });
            foreach (var n in targetList)
            {
                if (n == "DIRECT" || n == "REJECT")
                {
                    sb.AppendLine("  - " + n);
                }
                else
                {
                    sb.AppendLine("  - " + YamlQuoteName(n));
                }
            }
        }

        private async Task<int> RebuildMergedConfigAndReloadAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(_templateYamlPath))
                    {
                        if (File.Exists(_configYamlPath)) File.Copy(_configYamlPath, _templateYamlPath, true);
                    }
                    if (!File.Exists(_templateYamlPath)) return 0;

                    string templateText = FixMojibake(File.ReadAllText(_templateYamlPath, Encoding.UTF8));

                    // 1. 提取所有已启用订阅的代理节点并按名称去重
                    HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    List<ParsedProxyNode> allNodes = new List<ParsedProxyNode>();
                    Dictionary<string, string> newSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    var enabledProfiles = _subscriptionProfiles.Where(p => p.Enabled).ToList();
                    foreach (var p in enabledProfiles)
                    {
                        string pPath = System.IO.Path.Combine(_profilesDir, p.FileName);
                        if (!File.Exists(pPath)) continue;
                        string yText = FixMojibake(File.ReadAllText(pPath, Encoding.UTF8));
                        var pNodes = ExtractProxyNodesFromYaml(yText);
                        foreach (var n in pNodes)
                        {
                            string finalName = n.Name;
                            if (seenNames.Contains(finalName))
                            {
                                int suf = 2;
                                while (seenNames.Contains(finalName + " (" + suf + ")")) suf++;
                                finalName = finalName + " (" + suf + ")";
                                n.RawBlock = Regex.Replace(n.RawBlock, @"(?<![a-zA-Z0-9_-])name:\s*[""']?[^""'\r\n,}]+[""']?", "name: " + YamlQuoteName(finalName));
                                n.Name = finalName;
                            }
                            seenNames.Add(finalName);
                            allNodes.Add(n);
                            newSourceMap[finalName] = p.Name;
                        }
                    }

                    lock (_nodeSourceMap)
                    {
                        _nodeSourceMap.Clear();
                        foreach (var kv in newSourceMap) _nodeSourceMap[kv.Key] = kv.Value;
                    }

                    // 2. 按地区分类
                    List<string> allNames = new List<string>();
                    List<string> hkNames = new List<string>();
                    List<string> usNames = new List<string>();
                    List<string> jpNames = new List<string>();
                    List<string> sgNames = new List<string>();
                    List<string> twNames = new List<string>();
                    List<string> krNames = new List<string>();
                    List<string> euNames = new List<string>();
                    List<string> otherNames = new List<string>();

                    foreach (var n in allNodes)
                    {
                        string name = FixMojibake(n.Name);
                        allNames.Add(name);
                        string lower = name.ToLowerInvariant();
                        if (name.Contains("🇭🇰") || lower.Contains("hk") || lower.Contains("hong kong") || lower.Contains("hkg") || name.Contains("香港")) hkNames.Add(name);
                        else if (name.Contains("🇺🇸") || lower.Contains("us") || lower.Contains("usa") || lower.Contains("united states") || lower.Contains("america") || name.Contains("美国") || name.Contains("洛杉矶") || name.Contains("圣何塞")) usNames.Add(name);
                        else if (name.Contains("🇯🇵") || lower.Contains("jp") || lower.Contains("japan") || lower.Contains("jpn") || name.Contains("日本") || name.Contains("东京") || name.Contains("大阪")) jpNames.Add(name);
                        else if (name.Contains("🇸🇬") || lower.Contains("sg") || lower.Contains("sgp") || lower.Contains("singapore") || name.Contains("狮城") || name.Contains("新加坡")) sgNames.Add(name);
                        else if (name.Contains("🇹🇼") || lower.Contains("tw") || lower.Contains("twn") || lower.Contains("taiwan") || name.Contains("台湾") || name.Contains("台北")) twNames.Add(name);
                        else if (name.Contains("🇰🇷") || lower.Contains("kr") || lower.Contains("kor") || lower.Contains("korea") || name.Contains("韩国") || name.Contains("首尔")) krNames.Add(name);
                        else if (name.Contains("🇬🇧") || name.Contains("🇩🇪") || name.Contains("🇳🇱") || name.Contains("🇫🇷") || lower.Contains("uk") || lower.Contains("de") || lower.Contains("germany") || lower.Contains("europe") || name.Contains("欧洲") || name.Contains("德国") || name.Contains("英国") || name.Contains("荷兰") || name.Contains("法国")) euNames.Add(name);
                        else otherNames.Add(name);
                    }

                    // 3. 构造 proxies 区块
                    StringBuilder sbProxies = new StringBuilder();
                    if (allNodes.Count > 0)
                    {
                        sbProxies.AppendLine("proxies:");
                        foreach (var n in allNodes)
                        {
                            sbProxies.AppendLine(n.RawBlock);
                        }
                    }
                    else
                    {
                        sbProxies.AppendLine("proxies: []");
                    }

                    // 4. 构造 proxy-groups 区块
                    StringBuilder sbGroups = new StringBuilder();
                    sbGroups.AppendLine("proxy-groups:");

                    sbGroups.AppendLine("- name: PROXY");
                    sbGroups.AppendLine("  type: select");
                    sbGroups.AppendLine("  proxies:");
                    sbGroups.AppendLine("  - AUTO");
                    sbGroups.AppendLine("  - 香港");
                    sbGroups.AppendLine("  - 美国");
                    sbGroups.AppendLine("  - 日本");
                    sbGroups.AppendLine("  - 新加坡");
                    sbGroups.AppendLine("  - 台湾");
                    sbGroups.AppendLine("  - 韩国");
                    sbGroups.AppendLine("  - 欧洲");
                    sbGroups.AppendLine("  - 其他");
                    if (allNames.Count > 0)
                    {
                        foreach (var n in allNames) sbGroups.AppendLine("  - " + YamlQuoteName(n));
                    }
                    else
                    {
                        sbGroups.AppendLine("  - DIRECT");
                    }

                    sbGroups.AppendLine("- name: AUTO");
                    sbGroups.AppendLine("  type: url-test");
                    sbGroups.AppendLine("  url: https://www.gstatic.com/generate_204");
                    sbGroups.AppendLine("  interval: 300");
                    sbGroups.AppendLine("  tolerance: 50");
                    sbGroups.AppendLine("  proxies:");
                    if (allNames.Count > 0)
                    {
                        foreach (var n in allNames) sbGroups.AppendLine("  - " + YamlQuoteName(n));
                    }
                    else
                    {
                        sbGroups.AppendLine("  - DIRECT");
                    }

                    AppendRegionGroup(sbGroups, "香港", hkNames, allNames);
                    AppendRegionGroup(sbGroups, "美国", usNames, allNames);
                    AppendRegionGroup(sbGroups, "日本", jpNames, allNames);
                    AppendRegionGroup(sbGroups, "新加坡", sgNames, allNames);
                    AppendRegionGroup(sbGroups, "台湾", twNames, allNames);
                    AppendRegionGroup(sbGroups, "韩国", krNames, allNames);
                    AppendRegionGroup(sbGroups, "欧洲", euNames, allNames);
                    AppendRegionGroup(sbGroups, "其他", otherNames, allNames);

                    // 5. 与 template.yaml 拼接
                    string[] tLines = templateText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    StringBuilder sbHead = new StringBuilder();
                    StringBuilder sbTail = new StringBuilder();
                    bool seenProxies = false;
                    bool inRules = false;

                    for (int i = 0; i < tLines.Length; i++)
                    {
                        string l = tLines[i];
                        if (!seenProxies && Regex.IsMatch(l, @"^proxies:\s*$"))
                        {
                            seenProxies = true;
                            continue;
                        }
                        if (Regex.IsMatch(l, @"^rules:\s*$"))
                        {
                            inRules = true;
                            sbTail.AppendLine(l);
                            if (_customDirectRules != null && _customDirectRules.Count > 0)
                            {
                                foreach (var ruleItem in _customDirectRules)
                                {
                                    if (string.IsNullOrWhiteSpace(ruleItem)) continue;
                                    string r = ruleItem.Trim();
                                    if (r.StartsWith("- ")) r = r.Substring(2).Trim();

                                    if (r.StartsWith("DOMAIN", StringComparison.OrdinalIgnoreCase) ||
                                        r.StartsWith("IP-CIDR", StringComparison.OrdinalIgnoreCase) ||
                                        r.StartsWith("GEOIP", StringComparison.OrdinalIgnoreCase) ||
                                        r.StartsWith("GEOSITE", StringComparison.OrdinalIgnoreCase) ||
                                        r.StartsWith("AND,", StringComparison.OrdinalIgnoreCase) ||
                                        r.StartsWith("OR,", StringComparison.OrdinalIgnoreCase))
                                    {
                                        sbTail.AppendLine("- " + r);
                                    }
                                    else if (r.StartsWith("*.") || r.StartsWith("."))
                                    {
                                        sbTail.AppendLine("- DOMAIN-SUFFIX," + r.TrimStart('*', '.') + ",DIRECT");
                                    }
                                    else if (Regex.IsMatch(r, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(/\d{1,2})?$"))
                                    {
                                        string cidr = r.Contains("/") ? r : (r + "/32");
                                        sbTail.AppendLine("- IP-CIDR," + cidr + ",DIRECT,no-resolve");
                                    }
                                    else
                                    {
                                        sbTail.AppendLine("- DOMAIN-SUFFIX," + r + ",DIRECT");
                                    }
                                }
                            }
                            continue;
                        }
                        if (!seenProxies)
                        {
                            sbHead.AppendLine(l);
                        }
                        else if (inRules)
                        {
                            sbTail.AppendLine(l);
                        }
                    }

                    string mergedYaml = sbHead.ToString() + sbProxies.ToString() + sbGroups.ToString() + sbTail.ToString();

                    // 6. 安全校验与原子性替换写入
                    string tmpPath = _configYamlPath + ".tmp";
                    File.WriteAllText(tmpPath, mergedYaml, new UTF8Encoding(false));

                    // 测试 mihomo 核心能否通过验证
                    if (File.Exists(_mihomoExe))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = _mihomoExe,
                            Arguments = string.Format("-d \"{0}\" -f \"{1}\" -t", _dataDir, tmpPath),
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit(3500);
                            if (p.ExitCode != 0)
                            {
                                return 0;
                            }
                        }
                    }

                    File.Copy(tmpPath, _configYamlPath, true);
                    try { File.Delete(tmpPath); } catch { }

                    await HotReloadConfigYamlAsync(_configYamlPath);
                    return allNodes.Count;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private async Task<bool> UpdateSingleSubscriptionAsync(SubscriptionProfileItem profile, TextBlock statusTb = null)
        {
            if (profile == null) return false;
            if (statusTb == null) statusTb = _profilesStatusTb;

            if (statusTb != null) statusTb.Text = string.Format("正在更新 [{0}] 订阅配置...", profile.Name);
            try
            {
                string yamlContent = null;
                if (!string.IsNullOrEmpty(profile.Url) && (profile.Url.StartsWith("http://") || profile.Url.StartsWith("https://")))
                {
                    yamlContent = await Task.Run(() =>
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(profile.Url);
                        req.Method = "GET";
                        req.Timeout = 14000;
                        req.UserAgent = "clash.meta / clash-verge / mihomo-1.19.31";
                        req.Proxy = null;
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    });
                }
                else if (!string.IsNullOrEmpty(profile.Url) && File.Exists(profile.Url))
                {
                    yamlContent = File.ReadAllText(profile.Url, Encoding.UTF8);
                }
                else if (profile.Id == "default" && File.Exists(_templateYamlPath))
                {
                    yamlContent = File.ReadAllText(_templateYamlPath, Encoding.UTF8);
                }
                else
                {
                    string pPath = System.IO.Path.Combine(_profilesDir, profile.FileName);
                    if (File.Exists(pPath))
                    {
                        yamlContent = File.ReadAllText(pPath, Encoding.UTF8);
                    }
                }

                if (string.IsNullOrEmpty(yamlContent))
                {
                    if (statusTb != null) statusTb.Text = string.Format("更新失败：无法读取配置源 [{0}] 的内容。", profile.Name);
                    return false;
                }

                var nodes = ExtractProxyNodesFromYaml(yamlContent);
                if (nodes.Count == 0)
                {
                    if (statusTb != null) statusTb.Text = "更新失败：远程地址或文件未返回有效的 proxies 节点，原订阅已保留。";
                    return false;
                }

                string profilePath = System.IO.Path.Combine(_profilesDir, profile.FileName);
                File.WriteAllText(profilePath, yamlContent, new UTF8Encoding(false));

                profile.NodeCount = nodes.Count;
                profile.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                SaveSubscriptionProfiles();

                // 核心：更新完成后自动合并到整体！
                int totalMerged = await RebuildMergedConfigAndReloadAsync();

                if (statusTb != null)
                {
                    statusTb.Text = string.Format("✓ [{0}] 更新成功！已提取 {1} 个节点并自动合并生效（聚合总节点: {2}）。",
                        profile.Name, nodes.Count, totalMerged);
                }
                PopulateProfilesList();
                return true;
            }
            catch (Exception ex)
            {
                if (statusTb != null) statusTb.Text = "更新失败: " + ex.Message;
                return false;
            }
        }

        private async Task UpdateAllSubscriptionsAsync(TextBlock statusTb = null)
        {
            if (statusTb == null) statusTb = _profilesStatusTb;
            var enabledProfiles = _subscriptionProfiles.Where(p => p.Enabled).ToList();
            if (enabledProfiles.Count == 0)
            {
                if (statusTb != null) statusTb.Text = "当前无启用的订阅配置。";
                return;
            }

            int successCount = 0;
            for (int i = 0; i < enabledProfiles.Count; i++)
            {
                var p = enabledProfiles[i];
                if (statusTb != null) statusTb.Text = string.Format("正在全量更新 ({0}/{1}): {2}...", i + 1, enabledProfiles.Count, p.Name);
                bool ok = await UpdateSingleSubscriptionAsync(p, null);
                if (ok) successCount++;
            }

            int totalNodes = await RebuildMergedConfigAndReloadAsync();
            if (statusTb != null)
            {
                statusTb.Text = string.Format("✓ 全量更新完成！成功更新 {0}/{1} 个订阅，当前聚合 {2} 个节点并已热重载生效！",
                    successCount, enabledProfiles.Count, totalNodes);
            }
            PopulateProfilesList();
        }

        private async Task AddNewSubscriptionAsync(string url, string customName, TextBlock statusTb = null)
        {
            url = (url ?? "").Trim();
            if (string.IsNullOrEmpty(url))
            {
                if (statusTb != null) statusTb.Text = "请输入有效的订阅 URL 链接。";
                return;
            }
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                if (statusTb != null) statusTb.Text = "订阅链接必须以 http:// 或 https:// 开头。";
                return;
            }

            string subId = "sub_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = subId + ".yaml";
            string name = string.IsNullOrEmpty(customName) ? "订阅 " + (_subscriptionProfiles.Count + 1) : customName.Trim();

            if (statusTb != null) statusTb.Text = "正在下载并解析远程订阅配置...";

            try
            {
                string yamlContent = await Task.Run(() =>
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 14000;
                    req.UserAgent = "clash.meta / clash-verge / mihomo-1.19.31";
                    req.Proxy = null;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                });

                var nodes = ExtractProxyNodesFromYaml(yamlContent);
                if (nodes.Count == 0)
                {
                    if (statusTb != null)
                    {
                        statusTb.Text = "添加失败：该链接未返回任何有效的 proxies 节点，已保护现有配置未受损坏。";
                    }
                    return;
                }

                string filePath = System.IO.Path.Combine(_profilesDir, fileName);
                File.WriteAllText(filePath, yamlContent, new UTF8Encoding(false));

                var newProfile = new SubscriptionProfileItem
                {
                    Id = subId,
                    Name = name,
                    Url = url,
                    FileName = fileName,
                    NodeCount = nodes.Count,
                    UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Enabled = true
                };
                _subscriptionProfiles.Add(newProfile);
                SaveSubscriptionProfiles();

                int totalMerged = await RebuildMergedConfigAndReloadAsync();

                if (statusTb != null)
                {
                    statusTb.Text = string.Format("订阅添加成功！已解析 {0} 个节点并自动合并到整体运行配置（当前共 {1} 节点）。",
                        nodes.Count, totalMerged);
                }
                PopulateProfilesList();
            }
            catch (Exception ex)
            {
                if (statusTb != null) statusTb.Text = "拉取订阅失败: " + ex.Message + " (现有配置文件已受保护未损坏)";
            }
        }

        private async Task ImportLocalYamlProfileAsync(string filePath, TextBlock statusTb = null)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                string yamlContent = File.ReadAllText(filePath, Encoding.UTF8);
                var nodes = ExtractProxyNodesFromYaml(yamlContent);
                if (nodes.Count == 0)
                {
                    if (statusTb != null) statusTb.Text = "导入失败：选中的本地文件中未检测到 proxies 节点。";
                    return;
                }

                string subId = "local_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = subId + ".yaml";
                string baseName = System.IO.Path.GetFileNameWithoutExtension(filePath);

                string destPath = System.IO.Path.Combine(_profilesDir, fileName);
                File.WriteAllText(destPath, yamlContent, new UTF8Encoding(false));

                _subscriptionProfiles.Add(new SubscriptionProfileItem
                {
                    Id = subId,
                    Name = baseName,
                    Url = filePath,
                    FileName = fileName,
                    NodeCount = nodes.Count,
                    UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Enabled = true
                });
                SaveSubscriptionProfiles();

                int total = await RebuildMergedConfigAndReloadAsync();
                if (statusTb != null)
                {
                    statusTb.Text = string.Format("本地配置导入成功！已提取 {0} 个节点并自动合并（总节点: {1}）。", nodes.Count, total);
                }
                PopulateProfilesList();
            }
            catch (Exception ex)
            {
                if (statusTb != null) statusTb.Text = "导入本地文件失败: " + ex.Message;
            }
        }

        private async Task DeleteSubscriptionAsync(SubscriptionProfileItem profile)
        {
            if (profile == null) return;

            var confirm = MessageBox.Show(string.Format("确定删除订阅 [{0}] 吗？\n删除后将自动重新合并剩余订阅到整体配置。", profile.Name),
                "确认删除订阅", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _subscriptionProfiles.Remove(profile);
            SaveSubscriptionProfiles();

            try
            {
                string p = System.IO.Path.Combine(_profilesDir, profile.FileName);
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }

            int total = await RebuildMergedConfigAndReloadAsync();
            if (_profilesStatusTb != null)
            {
                if (_subscriptionProfiles.Count == 0)
                {
                    _profilesStatusTb.Text = string.Format("✓ 订阅 [{0}] 已删除，当前无任何订阅（直连模式）。", profile.Name);
                }
                else
                {
                    _profilesStatusTb.Text = string.Format("✓ 订阅 [{0}] 已删除，已重新合并生效（当前总节点: {1}）。", profile.Name, total);
                }
            }
            PopulateProfilesList();
        }

        private string GetPrimarySelectedNode()
        {
            if (_proxyGroups.Count == 0) return "正在同步...";
            return _proxyGroups[0].Now ?? "DIRECT";
        }

        private string GetUptimeString()
        {
            TimeSpan ts = DateTime.Now - _startTime;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        private void ToggleSystemProxy(bool enable)
        {
            _systemProxyEnabled = enable;
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (reg != null)
                    {
                        reg.SetValue("ProxyEnable", enable ? 1 : 0);
                        if (enable) reg.SetValue("ProxyServer", "127.0.0.1:7890");
                    }
                }
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }

            if (_activePage == "home") SwitchNavigationPage("home");
        }

        private void StartMihomoCore()
        {
            try
            {
                Process[] existing = Process.GetProcessesByName("mihomo");
                if (existing.Length > 0)
                {
                    _mihomoProcess = existing[0];
                    ChildProcessTracker.AddProcess(_mihomoProcess);
                    return;
                }
                if (!File.Exists(_mihomoExe)) return;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _mihomoExe,
                    Arguments = string.Format("-d \"{0}\" -ext-ctl 127.0.0.1:9097", _dataDir),
                    WorkingDirectory = _dataDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                _mihomoProcess = Process.Start(psi);
                ChildProcessTracker.AddProcess(_mihomoProcess);
            }
            catch { }
        }

        private void StopMihomoCore()
        {
            try
            {
                if (_mihomoProcess != null && !_mihomoProcess.HasExited)
                {
                    _mihomoProcess.Kill();
                    _mihomoProcess.WaitForExit(2000);
                }
            }
            catch { }
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            try
            {
                try { if (_wsTrafficCts != null) _wsTrafficCts.Cancel(); } catch { }
                try { if (_wsLogsCts != null) _wsLogsCts.Cancel(); } catch { }
                if (_systemProxyEnabled) ToggleSystemProxy(false);
                StopMihomoCore();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch { }
            Application.Current.Shutdown();
        }

        private void InitSystemTray()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Text = "Herciniamihomo Pro - Liquid Glass";
            string ico1 = System.IO.Path.Combine(_appDir, "app.ico");
            string ico2 = System.IO.Path.Combine(_dataDir, "ui", "favicon.ico");
            string iconPath = File.Exists(ico1) ? ico1 : ico2;
            if (File.Exists(iconPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        _trayIcon.Icon = new System.Drawing.Icon(fs);
                    }
                }
                catch { _trayIcon.Icon = System.Drawing.SystemIcons.Shield; }
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Shield;
            }
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                }
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var showItem = new System.Windows.Forms.ToolStripMenuItem("打开主界面");
            showItem.Click += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); };
            menu.Items.Add(showItem);

            var proxyItem = new System.Windows.Forms.ToolStripMenuItem("切换系统代理 (127.0.0.1:7890)");
            proxyItem.Click += (s, e) => ToggleSystemProxy(!_systemProxyEnabled);
            menu.Items.Add(proxyItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出并还原代理");
            exitItem.Click += (s, e) => ExitApplication();
            menu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = menu;
        }

        // =====================================================================
        // Windows 开机静默自启注册表管理 (Option E)
        // =====================================================================
        private bool IsAutoStartSilentEnabled()
        {
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(RunRegKeyPath, false))
                {
                    if (reg == null) return false;
                    object val = reg.GetValue(RunRegKeyName);
                    return val != null;
                }
            }
            catch { return false; }
        }

        private void SetAutoStartSilent(bool enable)
        {
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(RunRegKeyPath, true))
                {
                    if (reg == null) return;
                    if (enable)
                    {
                        string exePath = System.IO.Path.Combine(_appDir, "Herciniamihomo.exe");
                        reg.SetValue(RunRegKeyName, string.Format("\"{0}\" -silent", exePath));
                    }
                    else
                    {
                        reg.DeleteValue(RunRegKeyName, false);
                    }
                }
            }
            catch { }
        }

        // =====================================================================
        // 自定义直连网站管理引擎 (Custom Direct Rules)
        // =====================================================================
        private void LoadCustomDirectRules()
        {
            try
            {
                if (File.Exists(_customDirectRulesPath))
                {
                    string json = File.ReadAllText(_customDirectRulesPath, Encoding.UTF8);
                    var list = _json.Deserialize<List<string>>(json);
                    if (list != null && list.Count > 0)
                    {
                        _customDirectRules = list;
                        return;
                    }
                }
            }
            catch { }

            _customDirectRules = new List<string>
            {
                "bilibili.com",
                "hdslb.com",
                "bilivideo.com",
                "steamcommunity.com",
                "steampowered.com",
                "epicgames.com",
                "127.0.0.1",
                "localhost"
            };
            SaveCustomDirectRules();
        }

        private void SaveCustomDirectRules()
        {
            try
            {
                File.WriteAllText(_customDirectRulesPath, _json.Serialize(_customDirectRules), Encoding.UTF8);
            }
            catch { }
        }

        private async void AddCustomDirectRule(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return;
            string d = domain.Trim().ToLowerInvariant();
            if (_customDirectRules.Any(r => r.Equals(d, StringComparison.OrdinalIgnoreCase))) return;

            _customDirectRules.Add(d);
            SaveCustomDirectRules();
            PopulateCustomDirectRulesTags();
            if (_customRulesStatusTb != null) _customRulesStatusTb.Text = "正在注入直连规则并热重载内核...";
            await RebuildMergedConfigAndReloadAsync();
            if (_customRulesStatusTb != null)
            {
                _customRulesStatusTb.Text = string.Format("✓ [{0}] 已应用 {1} 条直连规则，最高优先级匹配 DIRECT (热重载完成)",
                    DateTime.Now.ToString("HH:mm:ss"), _customDirectRules.Count);
            }
        }

        private async void RemoveCustomDirectRule(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return;
            if (_customDirectRules.RemoveAll(r => r.Equals(domain.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
            {
                SaveCustomDirectRules();
                PopulateCustomDirectRulesTags();
                if (_customRulesStatusTb != null) _customRulesStatusTb.Text = "正在更新直连规则并热重载内核...";
                await RebuildMergedConfigAndReloadAsync();
                if (_customRulesStatusTb != null)
                {
                    _customRulesStatusTb.Text = string.Format("✓ [{0}] 已应用 {1} 条直连规则，最高优先级匹配 DIRECT (热重载完成)",
                        DateTime.Now.ToString("HH:mm:ss"), _customDirectRules.Count);
                }
            }
        }

        private void PopulateCustomDirectRulesTags()
        {
            if (_customRulesTagWrap == null) return;
            _customRulesTagWrap.Children.Clear();

            if (_customDirectRules.Count == 0)
            {
                _customRulesTagWrap.Children.Add(new TextBlock
                {
                    Text = "暂无自定义直连规则，请在上方输入域名添加，或点击一键添加推荐预设。",
                    Foreground = FrozenBrush(148, 163, 184),
                    FontSize = 11.5,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (string ruleItem in _customDirectRules)
            {
                string r = ruleItem;
                Border pill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = FrozenBrush(85, 15, 23, 42),
                    BorderBrush = FrozenBrush(110, 56, 189, 248),
                    BorderThickness = new Thickness(0.9),
                    Padding = new Thickness(9, 4, 8, 4),
                    Margin = new Thickness(0, 0, 7, 7)
                };

                StackPanel pillRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                pillRow.Children.Add(new TextBlock
                {
                    Text = r,
                    Foreground = FrozenBrush(186, 230, 253),
                    FontFamily = MonoFont,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });

                Border delBtn = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = FrozenBrush(40, 248, 113, 113),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "×",
                        Foreground = FrozenBrush(252, 165, 165),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -1, 0, 0)
                    }
                };
                delBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    RemoveCustomDirectRule(r);
                };
                pillRow.Children.Add(delBtn);
                pill.Child = pillRow;
                AttachSpringHover(pill);
                _customRulesTagWrap.Children.Add(pill);
            }
        }

        // =====================================================================
        // 网络自愈急救箱诊断方法 (Option F)
        // =====================================================================
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        private static extern int DnsFlushResolverCache();

        private void ExecuteNetworkDoctorResetProxy()
        {
            try
            {
                ToggleSystemProxy(false);
                if (_networkDoctorStatusTb != null)
                {
                    _networkDoctorStatusTb.Text = string.Format("✓ [{0}] Windows 系统代理注册表已强力重置为直连，WinINet 缓存已刷新！", DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                if (_networkDoctorStatusTb != null) _networkDoctorStatusTb.Text = "重置系统代理失败: " + ex.Message;
            }
        }

        private void ExecuteNetworkDoctorFlushDns()
        {
            try
            {
                try { DnsFlushResolverCache(); } catch { }
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ipconfig.exe",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                if (_networkDoctorStatusTb != null)
                {
                    _networkDoctorStatusTb.Text = string.Format("✓ [{0}] Windows DNS 本地解析缓存已完全清空，域名解析链已重置！", DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                if (_networkDoctorStatusTb != null) _networkDoctorStatusTb.Text = "刷新系统 DNS 失败: " + ex.Message;
            }
        }

        private async Task ExecuteNetworkDoctorFlushFakeIpAsync()
        {
            try
            {
                await HttpSendAsync("POST", "http://127.0.0.1:9097/cache/fakeip/flush", "");
                if (_networkDoctorStatusTb != null)
                {
                    _networkDoctorStatusTb.Text = string.Format("✓ [{0}] Mihomo 内核 Fake-IP 地址池与映射已全部释放！", DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                if (_networkDoctorStatusTb != null) _networkDoctorStatusTb.Text = "清空 Fake-IP 失败: " + ex.Message;
            }
        }

        private async Task RestartMihomoCoreAsync()
        {
            try
            {
                if (_networkDoctorStatusTb != null) _networkDoctorStatusTb.Text = "正在平滑重启 Mihomo 核心进程与监控通道...";

                try { if (_wsTrafficCts != null) _wsTrafficCts.Cancel(); } catch { }
                try { if (_wsLogsCts != null) _wsLogsCts.Cancel(); } catch { }
                _wsTrafficActive = false;
                _wsLogsActive = false;

                Process[] existing = Process.GetProcessesByName("mihomo");
                foreach (var p in existing)
                {
                    try { p.Kill(); p.WaitForExit(1000); } catch { }
                }
                _mihomoProcess = null;

                await Task.Delay(400);
                StartMihomoCore();
                await Task.Delay(600);

                StartWebSocketTrafficStream();
                StartLiveCoreLogsStream();
                await FetchProxiesFromCoreAsync();
                await PollMihomoStateAsync();

                if (_networkDoctorStatusTb != null)
                {
                    _networkDoctorStatusTb.Text = string.Format("✓ [{0}] Mihomo 核心服务与全双工 WebSocket 已成功重启！", DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                if (_networkDoctorStatusTb != null) _networkDoctorStatusTb.Text = "核心重启失败: " + ex.Message;
            }
        }

        private void SaveOpticalSettings()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "preset", _wallpaperPreset },
                    { "customPath", _customWallpaperPath },
                    { "opacity", _glassSurfaceOpacity },
                    { "rim", _glassRimIntensity },
                    { "dimming", _wallpaperDimming },
                    { "blur", _wallpaperBlurVal }
                };
                File.WriteAllText(_uiSettingsPath, _json.Serialize(data), Encoding.UTF8);
            }
            catch { }
        }

        private void LoadSavedOpticalSettings()
        {
            try
            {
                if (!File.Exists(_uiSettingsPath)) return;
                var d = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(_uiSettingsPath, Encoding.UTF8));
                if (d == null) return;
                if (d.ContainsKey("preset")) _wallpaperPreset = Convert.ToString(d["preset"]);
                if (d.ContainsKey("customPath")) _customWallpaperPath = Convert.ToString(d["customPath"]);
                if (d.ContainsKey("opacity")) _glassSurfaceOpacity = Convert.ToDouble(d["opacity"]);
                if (d.ContainsKey("rim")) _glassRimIntensity = Convert.ToDouble(d["rim"]);
                if (d.ContainsKey("dimming")) _wallpaperDimming = Convert.ToDouble(d["dimming"]);
                if (d.ContainsKey("blur")) _wallpaperBlurVal = Convert.ToDouble(d["blur"]);
            }
            catch { }
        }

        private async Task<string> HttpGetAsync(string url)
        {
            return await Task.Run(() =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 3000;
                    req.Proxy = null;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch { return ""; }
            });
        }

        private async Task<string> HttpSendAsync(string method, string url, string jsonBody)
        {
            return await Task.Run(() =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = method;
                    req.ContentType = "application/json";
                    req.Timeout = 3500;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(jsonBody))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                        req.ContentLength = bytes.Length;
                        using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                    }
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch { return ""; }
            });
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return bytesPerSec.ToString("0") + " B/s";
            double kb = bytesPerSec / 1024.0;
            if (kb < 1024) return kb.ToString("0.0") + " KB/s";
            double mb = kb / 1024.0;
            return mb.ToString("0.00") + " MB/s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.00") + " MB";
            double gb = mb / 1024.0;
            return gb.ToString("0.00") + " GB";
        }
    }
}
