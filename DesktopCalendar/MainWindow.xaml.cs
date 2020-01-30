using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DesktopCalendar
{
    public class CanvasAutoSize : Canvas
    {
        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            base.MeasureOverride(constraint);
            double width = base
                .InternalChildren
                .OfType<UIElement>()
                .Max(i => i.DesiredSize.Width + (double)i.GetValue(Canvas.LeftProperty));

            double height = base
                .InternalChildren
                .OfType<UIElement>()
                .Max(i => i.DesiredSize.Height + (double)i.GetValue(Canvas.TopProperty));

            return new Size((double.IsNaN(width)) ? 0 : width, (double.IsNaN(height)) ? 0 : height);
        }
    }

    public partial class MainWindow : Window
    {
        List<Button> DateButtons = new List<Button>();
        DateTime Date;
        int DaysInMonth;

        DateButton CurrentDateButton = null;
        Line DateLine;

        bool OpenForInput;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;


            Date = DateTime.Now;
            InitMonth();
        }

        #region colors

        public static Color? GetChromeColor()
        {
            bool isEnabled;
            var hr1 = DwmIsCompositionEnabled(out isEnabled);
            if ((hr1 != 0) || !isEnabled) // 0 means S_OK.
                return null;

            DWMCOLORIZATIONPARAMS parameters;
            try
            {
                // This API is undocumented and so may become unusable in future versions of OSes.
                var hr2 = DwmGetColorizationParameters(out parameters);
                if (hr2 != 0) // 0 means S_OK.
                    return null;
            }
            catch
            {
                return null;
            }

            // Convert colorization color parameter to Color ignoring alpha channel.
            var targetColor = Color.FromRgb(
                (byte)(parameters.colorizationColor >> 16),
                (byte)(parameters.colorizationColor >> 8),
                (byte)parameters.colorizationColor);

            // Prepare base gray color.
            var baseColor = Color.FromRgb(217, 217, 217);

            // Blend the two colors using colorization color balance parameter.
            return BlendColor(targetColor, baseColor, (double)(100 - parameters.colorizationColorBalance));
        }

        private static Color BlendColor(Color color1, Color color2, double color2Perc)
        {
            if ((color2Perc < 0) || (100 < color2Perc))
                throw new ArgumentOutOfRangeException("color2Perc");

            return Color.FromRgb(
                BlendColorChannel(color1.R, color2.R, color2Perc),
                BlendColorChannel(color1.G, color2.G, color2Perc),
                BlendColorChannel(color1.B, color2.B, color2Perc));
        }

        private static byte BlendColorChannel(double channel1, double channel2, double channel2Perc)
        {
            var buff = channel1 + (channel2 - channel1) * channel2Perc / 100D;
            return Math.Min((byte)Math.Round(buff), (byte)255);
        }

        [DllImport("Dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);

        [DllImport("Dwmapi.dll", EntryPoint = "#127")] // Undocumented API
        private static extern int DwmGetColorizationParameters(out DWMCOLORIZATIONPARAMS parameters);

        [StructLayout(LayoutKind.Sequential)]
        private struct DWMCOLORIZATIONPARAMS
        {
            public uint colorizationColor;
            public uint colorizationAfterglow;
            public uint colorizationColorBalance; // Ranging from 0 to 100
            public uint colorizationAfterglowBalance;
            public uint colorizationBlurBalance;
            public uint colorizationGlassReflectionIntensity;
            public uint colorizationOpaqueBlend;
        }

        #endregion

        private void InitMonth()
        {
            tbMonthName.Text = Date.ToString("MMMM yyyy");
            panelDays.Children.Clear();

            DaysInMonth = DateTime.DaysInMonth(Date.Year, Date.Month);

            if (!File.Exists(Date.Month.ToString()))
            {
                for (int i = 0; i < DaysInMonth; i++)
                {
                    DateButton b = new DateButton();
                    b.Content = (i + 1).ToString();
                    b.Focusable = false;
                    b.Style = Resources["DayButton"] as Style;
                    RenderOptions.SetClearTypeHint(b, ClearTypeHint.Enabled);
                    b.Loaded += b_Loaded;
                    b.MouseEnter += b_MouseEnter;
                    b.MouseLeave += b_MouseLeave;
                    b.PreviewMouseUp += b_PreviewMouseUp;
                    b.ApplyTemplate();

                    DropShadowEffect d = new DropShadowEffect();
                    d.Color = (Color)Resources["DropShadowColor"];
                    d.BlurRadius = 4;
                    d.Opacity = 1;
                    d.ShadowDepth = 0;
                    b.Effect = d;

                    if (i == Date.Day - 1 && Date.Month == DateTime.Now.Month)
                        b.Foreground = (SolidColorBrush)Resources["HighlightColor"];

                    panelDays.Children.Add(b);
                }
            }
            else Load(Date.Month);
        }

        private Size MeasureString(string candidate)
        {
            var formattedText = new FormattedText(
                candidate,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(MemoText.FontFamily, MemoText.FontStyle, MemoText.FontWeight, MemoText.FontStretch), MemoText.FontSize, Brushes.Black);

            return new Size(formattedText.Width, formattedText.Height);
        }

        private void ShowMemo(DateButton b)
        {
            double top = Canvas.GetTop(panelDays);
            double left = b.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            if (b.Memo == null)
                MemoText.Text = "New memo...";
            else
                MemoText.Text = b.Memo;

            MemoText.UpdateLayout();
            MemoText.Visibility = Visibility.Visible;
            memoBorder.Visibility = Visibility.Visible;
            
            Canvas.SetTop(memoBorder, top - memoBorder.ActualHeight - 12);
            Canvas.SetLeft(memoBorder, left - (MemoText.ActualWidth / 2) + (b.ActualWidth / 2) - 6);
            FocusManager.SetFocusedElement(canvas, memoBorder);
        }

        private void HideMemo()
        {
            MemoText.Visibility = Visibility.Hidden;
            memoBorder.Visibility = Visibility.Hidden;
            OpenForInput = false;
        }

        private void ShowLine(DateButton b)
        {
            Point p = b.TransformToAncestor(this).Transform(new Point(0, 0));
            p.X += b.ActualWidth / 2;

            if (DateLine == null)
            {
                DateLine = new Line();
                DateLine.Stroke = Brushes.Gray;
                DateLine.StrokeThickness = 1;
                DateLine.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
                DateLine.SnapsToDevicePixels = true;
            }

            DateLine.X1 = p.X;
            DateLine.Y1 = p.Y - 100;
            DateLine.X2 = p.X;
            DateLine.Y2 = p.Y - 4;

            if (!canvas.Children.Contains(DateLine))
                canvas.Children.Add(DateLine);
        }

        private void Save()
        {
            StreamWriter sw = new StreamWriter(Date.Month.ToString());
            foreach (DateButton b in panelDays.Children)
                sw.WriteLine(b.Content + "|" + b.Memo);

            sw.Flush();
            sw.Close();
        }

        private void Load(int month)
        {
            StreamReader sr = new StreamReader(month.ToString());
            while (sr.Peek() != -1)
            {
                string[] s = sr.ReadLine().Split('|');

                DateButton b = new DateButton();
                b.Content = s[0];
                b.Focusable = false;
                b.Style = Resources["DayButton"] as Style;

                DropShadowEffect d = new DropShadowEffect();
                d.Color = (Color)Resources["DropShadowColor"];
                d.BlurRadius = 4;
                d.Opacity = 1;
                d.ShadowDepth = 0;
                b.Effect = d;

                RenderOptions.SetClearTypeHint(b, ClearTypeHint.Enabled);
                b.Loaded += b_Loaded;
                b.MouseEnter += b_MouseEnter;
                b.MouseLeave += b_MouseLeave;
                b.PreviewMouseUp += b_PreviewMouseUp;
                b.Memo = (s[1] == "") ? null : s[1];
                b.ApplyTemplate();

                if (s[0] == Date.Day.ToString() && Date.Month == DateTime.Now.Month)
                    b.Foreground = (SolidColorBrush)Resources["HighlightColor"];

                if (b.Memo != null)
                {
                    Border border = (Border)b.Template.FindName("DayButtonBorder", b);
                    if (border != null) border.BorderThickness = new Thickness(0, 0, 0, 1);
                }

                panelDays.Children.Add(b);
            }
            sr.Close();
        }

        private Color InvertColor(Color c)
        {
            int max = 255;
            return Color.FromArgb(c.A, (byte)(max - c.R), (byte)(max - c.G), (byte)(max - c.B));
        }

        private Color SetSaturation(Color c, double sat)
        {
            return Color.FromArgb(c.A, (byte)(c.R - 20), (byte)(c.G - 20), (byte)(c.B - 20));
        }

        private void SetTheme()
        {
            Color? c = GetChromeColor();
            double b = ColorManipulation.Brightness(c.Value);

            if (b < 180)
            {
                Resources["ForeColor"] = new SolidColorBrush(Colors.Black);
                Resources["BackColor"] = new SolidColorBrush(Color.FromArgb(48, 0, 0, 0));
                Resources["DropShadowColor"] = Color.FromArgb(255, 0, 0, 0);
            }
            else
            {
                Resources["ForeColor"] = new SolidColorBrush(Colors.White);
                Resources["BackColor"] = new SolidColorBrush(Color.FromArgb(64, 32, 32, 32));
                Resources["DropShadowColor"] = c.Value;
            }

            foreach (Button e in panelDays.Children)
            {
                DropShadowEffect d = new DropShadowEffect();
                d.Color = (Color)Resources["DropShadowColor"];
                d.BlurRadius = 4;
                d.Opacity = 1;
                d.ShadowDepth = 0;
                e.Effect = d;

                Border border = (Border)e.Template.FindName("DayButtonBorder", e);
                border.BorderBrush = (SolidColorBrush)Resources["ForeColor"];
            }

            foreach (UIElement e in panelInfo.Children)
            {
                DropShadowEffect d = new DropShadowEffect();
                d.Color = (Color)Resources["DropShadowColor"];
                d.BlurRadius = 4;
                d.Opacity = 1;
                d.ShadowDepth = 0;
                e.Effect = d;
            }

            DropShadowEffect de = new DropShadowEffect();
            de.Color = (Color)Resources["DropShadowColor"];
            de.BlurRadius = 4;
            de.Opacity = 1;
            de.ShadowDepth = 0;
            MemoText.Effect = de;
        }

        void b_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DateButton b = sender as DateButton;
                ShowMemo(b);
                MemoText.SelectAll();
                MemoText.Focusable = true;
                MemoText.Focus();       

                CurrentDateButton = b;
                OpenForInput = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                DateButton b = sender as DateButton;            
                Border border = (Border)b.Template.FindName("DayButtonBorder", b);

                if (border != null) 
                    border.BorderThickness = new Thickness(0);

                b.Memo = null;

                HideMemo();
                Save();  
            }
        }

        void b_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!OpenForInput)
                HideMemo();
        }

        void b_MouseEnter(object sender, MouseEventArgs e)
        {
            DateButton b = sender as DateButton;
            if (b.Memo != null && !OpenForInput)
            {
                ShowMemo(b);
            }
        }

        void b_Loaded(object sender, EventArgs e)
        {

        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Top = (SystemParameters.PrimaryScreenHeight / 2) - (Height / 2);
            Left = (SystemParameters.PrimaryScreenWidth / 2) - (Width / 2);

            Canvas.SetTop(borderDays, (Height / 2) - (borderDays.ActualHeight / 2));
            //Canvas.SetLeft(borderDays, (Width / 2) - (borderDays.ActualWidth / 2));
            Canvas.SetTop(borderInfo, Canvas.GetTop(borderDays) + borderDays.ActualHeight + 8);
            Canvas.SetLeft(borderInfo, (Width / 2) - (borderInfo.ActualWidth / 2));

            borderDays.Width = Width;
            ((DateButton)((StackPanel)borderDays.Child).Children[0]).Margin = new Thickness(0);

            SetTheme();
            //RenderOptions.SetClearTypeHint(tbMonthName, ClearTypeHint.Enabled);
        }

        #region StickToDesktop

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        IntPtr hWnd;

        readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        readonly IntPtr HWND_TOP = new IntPtr(0);
        readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x320;
        const int WM_SETFOCUS = 0x0007;
        const uint SWP_NOMOVE = 0x2;
        const uint SWP_NOSIZE = 0x1;
        const uint SWP_SHOWWINDOW = 0x40;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            hWnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETFOCUS)
            {
                SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
                handled = true;
            }
            else if (msg == WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                SetTheme();
            }

            return IntPtr.Zero;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }

        #endregion

        private void panelDays_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Canvas.SetTop(panelDays, (Height / 2) - (panelDays.ActualHeight / 2));
            Canvas.SetLeft(panelDays, (Width / 2) - (panelDays.ActualWidth / 2));
        }

        private void MemoText_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double x = e.NewSize.Width - e.PreviousSize.Width;
            double y = e.NewSize.Height - e.PreviousSize.Height;

            Vector offset = VisualTreeHelper.GetOffset(memoBorder);
            double a = offset.X;
            double b = offset.Y;

            Canvas.SetLeft(memoBorder, a - (x / 2));
            Canvas.SetTop(memoBorder, b - y);
        }

        private void MemoText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CurrentDateButton.Memo = MemoText.Text;
                Save();
                InitMonth();
                HideMemo();
            }
        }

        private void btnPreviousMonth_Click(object sender, RoutedEventArgs e)
        {
            Date = Date.AddMonths(-1);
            InitMonth();
        }

        private void btnNextMonth_Click(object sender, RoutedEventArgs e)
        {
            Date = Date.AddMonths(1);
            InitMonth();
        }

        private void tbMonthName_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            Date = DateTime.Now;
            InitMonth();
        }

        private void panelInfo_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double x = e.NewSize.Width - e.PreviousSize.Width;
            double y = e.NewSize.Height - e.PreviousSize.Height;
            Canvas.SetLeft(panelInfo, Canvas.GetLeft(panelInfo) - (x / 2));
            Canvas.SetTop(panelInfo, Canvas.GetTop(panelInfo) - y);
        }

        private void MemoText_LayoutUpdated(object sender, EventArgs e)
        {

        }
    }
}
