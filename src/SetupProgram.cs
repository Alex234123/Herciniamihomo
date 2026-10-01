using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace HerciniamihomoInstaller
{
    public class SetupApp : Application
    {
        [STAThread]
        public static void Main()
        {
            SetupApp app = new SetupApp();
            app.Run(new SetupWindow());
        }
    }

    public class SetupWindow : Window
    {
        private TextBox _installPathTb;
        private CheckBox _desktopShortcutCb;
        private CheckBox _startMenuCb;
        private CheckBox _launchNowCb;
        private ProgressBar _progBar;
        private TextBlock _statusTb;
        private Border _installBtn;
        private TextBlock _installBtnText;
        private bool _isInstalling = false;

        public SetupWindow()
        {
            this.Title = "Herciniamihomo Pro v1.0.1 - 安装向导";
            this.Width = 540;
            this.Height = 380;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.NoResize;

            BuildUI();
        }

        private void BuildUI()
        {
            Border rootCard = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new LinearGradientBrush(
                    Color.FromArgb(245, 15, 23, 42),
                    Color.FromArgb(245, 30, 41, 59),
                    45.0
                ),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                ClipToBounds = true
            };
            rootCard.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) }); // Banner
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) }); // Footer

            // 1. Header (Title & Close)
            Grid header = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            TextBlock titleTb = new TextBlock
            {
                Text = "Herciniamihomo Pro - 1,000Hz Liquid Glass 安装向导",
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button closeBtn = new Button
            {
                Content = "✕",
                Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 28,
                Height = 28
            };
            closeBtn.Click += delegate { if (!_isInstalling) this.Close(); };
            header.Children.Add(titleTb);
            header.Children.Add(closeBtn);
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // 2. Banner with Logo
            Grid banner = new Grid { Margin = new Thickness(24, 0, 24, 0) };
            banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border logoBox = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(28),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)),
                BorderThickness = new Thickness(1.5),
                Clip = new EllipseGeometry(new Rect(0, 0, 56, 56)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            try
            {
                // Extract circular icon from payload or render vector
                Ellipse el = new Ellipse
                {
                    Fill = new RadialGradientBrush(Color.FromArgb(255, 56, 189, 248), Color.FromArgb(255, 14, 165, 233))
                };
                logoBox.Child = el;
            }
            catch { }
            Grid.SetColumn(logoBox, 0);
            banner.Children.Add(logoBox);

            StackPanel bannerTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bannerTexts.Children.Add(new TextBlock
            {
                Text = "Herciniamihomo Pro v1.0.1",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            bannerTexts.Children.Add(new TextBlock
            {
                Text = "1,000Hz (1kHz) 极速微积分物理引擎 · Liquid Glass 液态毛玻璃原生桌面客户端",
                Foreground = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)),
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(bannerTexts, 1);
            banner.Children.Add(bannerTexts);
            Grid.SetRow(banner, 1);
            mainGrid.Children.Add(banner);

            // 3. Middle Content (Install Path & Options)
            StackPanel contentStack = new StackPanel { Margin = new Thickness(24, 8, 24, 0) };

            TextBlock pathLabel = new TextBlock
            {
                Text = "安装目标路径：",
                Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            contentStack.Children.Add(pathLabel);

            Grid pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            string defaultDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Herciniamihomo"
            );

            _installPathTb = new TextBox
            {
                Text = defaultDir,
                Height = 32,
                Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 148, 163, 184)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_installPathTb, 0);
            pathRow.Children.Add(_installPathTb);

            Button browseBtn = new Button
            {
                Content = "浏览...",
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(120, 30, 41, 59)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 148, 163, 184)),
                Cursor = Cursors.Hand
            };
            browseBtn.Click += delegate
            {
                var fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.SelectedPath = _installPathTb.Text;
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _installPathTb.Text = fbd.SelectedPath;
                }
            };
            Grid.SetColumn(browseBtn, 1);
            pathRow.Children.Add(browseBtn);
            contentStack.Children.Add(pathRow);

            WrapPanel optsWrap = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            _desktopShortcutCb = new CheckBox
            {
                Content = "创建桌面快捷方式",
                IsChecked = true,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 16, 0)
            };
            _startMenuCb = new CheckBox
            {
                Content = "创建开始菜单项",
                IsChecked = true,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 16, 0)
            };
            _launchNowCb = new CheckBox
            {
                Content = "安装完成后立即运行",
                IsChecked = true,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
                FontSize = 12
            };
            optsWrap.Children.Add(_desktopShortcutCb);
            optsWrap.Children.Add(_startMenuCb);
            optsWrap.Children.Add(_launchNowCb);
            contentStack.Children.Add(optsWrap);

            _progBar = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 14, 0, 0),
                Visibility = Visibility.Collapsed,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                Background = new SolidColorBrush(Color.FromArgb(80, 15, 23, 42))
            };
            contentStack.Children.Add(_progBar);

            _statusTb = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromArgb(180, 56, 189, 248)),
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(_statusTb);

            Grid.SetRow(contentStack, 2);
            mainGrid.Children.Add(contentStack);

            // 4. Footer (Action Buttons)
            Grid footer = new Grid { Margin = new Thickness(24, 0, 24, 16) };
            _installBtn = new Border
            {
                Width = 140,
                Height = 38,
                CornerRadius = new CornerRadius(8),
                Background = new LinearGradientBrush(
                    Color.FromRgb(14, 165, 233),
                    Color.FromRgb(2, 132, 199),
                    90.0
                ),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            _installBtnText = new TextBlock
            {
                Text = "立即安装",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _installBtn.Child = _installBtnText;
            _installBtn.MouseLeftButtonUp += delegate { StartInstallAsync(); };
            footer.Children.Add(_installBtn);
            Grid.SetRow(footer, 3);
            mainGrid.Children.Add(footer);

            rootCard.Child = mainGrid;
            this.Content = rootCard;
        }

        private async void StartInstallAsync()
        {
            if (_isInstalling) return;
            _isInstalling = true;

            string targetDir = _installPathTb.Text.Trim();
            if (string.IsNullOrEmpty(targetDir))
            {
                MessageBox.Show("请指定有效的安装路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                _isInstalling = false;
                return;
            }

            _installPathTb.IsEnabled = false;
            _installBtn.IsEnabled = false;
            _installBtn.Opacity = 0.5;
            _progBar.Visibility = Visibility.Visible;
            _statusTb.Visibility = Visibility.Visible;
            _progBar.Value = 10;
            _statusTb.Text = "正在初始化安装目录...";

            bool desktopLnk = _desktopShortcutCb.IsChecked == true;
            bool startMenuLnk = _startMenuCb.IsChecked == true;
            bool launchNow = _launchNowCb.IsChecked == true;

            try
            {
                await Task.Run(delegate
                {
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // Extract embedded payload
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream stream = asm.GetManifestResourceStream("Payload"))
                    {
                        if (stream == null)
                        {
                            throw new InvalidOperationException("未找到内置的安装程序数据资源 (Payload)！");
                        }

                        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                        {
                            int total = archive.Entries.Count;
                            int count = 0;
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                count++;
                                string fullDest = System.IO.Path.Combine(targetDir, entry.FullName);
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    Directory.CreateDirectory(fullDest);
                                }
                                else
                                {
                                    string dir = System.IO.Path.GetDirectoryName(fullDest);
                                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                                    entry.ExtractToFile(fullDest, true);
                                }

                                int percent = 10 + (int)((count / (double)total) * 75);
                                Dispatcher.Invoke(delegate
                                {
                                    _progBar.Value = percent;
                                    _statusTb.Text = string.Format("正在提取文件 [{0}/{1}]: {2}", count, total, entry.Name);
                                });
                            }
                        }
                    }

                    string exePath = System.IO.Path.Combine(targetDir, "Herciniamihomo.exe");

                    // Create shortcuts
                    Dispatcher.Invoke(delegate
                    {
                        _progBar.Value = 90;
                        _statusTb.Text = "正在创建系统快捷方式与注册表信息...";
                    });

                    if (desktopLnk)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        CreateShortcut(System.IO.Path.Combine(desktopPath, "Herciniamihomo Pro.lnk"), exePath, targetDir);
                    }

                    if (startMenuLnk)
                    {
                        string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                        string appMenuDir = System.IO.Path.Combine(programsPath, "Herciniamihomo Pro");
                        if (!Directory.Exists(appMenuDir)) Directory.CreateDirectory(appMenuDir);
                        CreateShortcut(System.IO.Path.Combine(appMenuDir, "Herciniamihomo Pro.lnk"), exePath, targetDir);
                    }

                    // Registry entry for Add/Remove Programs
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Herciniamihomo"))
                        {
                            if (key != null)
                            {
                                key.SetValue("DisplayName", "Herciniamihomo Pro");
                                key.SetValue("DisplayVersion", "1.0.1");
                                key.SetValue("DisplayIcon", exePath);
                                key.SetValue("InstallLocation", targetDir);
                                key.SetValue("Publisher", "Hercinia");
                                key.SetValue("UninstallString", "\"" + exePath + "\" -uninstall");
                            }
                        }
                    }
                    catch { }
                });

                _progBar.Value = 100;
                _statusTb.Text = "安装成功！正在启动程序...";
                _statusTb.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

                if (launchNow)
                {
                    string exePath = System.IO.Path.Combine(targetDir, "Herciniamihomo.exe");
                    if (File.Exists(exePath))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = exePath,
                                WorkingDirectory = targetDir
                            });
                        }
                        catch { }
                    }
                }

                await Task.Delay(1200);
                this.Close();
            }
            catch (Exception ex)
            {
                _statusTb.Text = "安装过程中出现错误: " + ex.Message;
                _statusTb.Foreground = Brushes.Tomato;
                _installBtn.IsEnabled = true;
                _installBtn.Opacity = 1.0;
                _isInstalling = false;
                MessageBox.Show("安装失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workDir)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                var shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetExe;
                shortcut.WorkingDirectory = workDir;
                shortcut.IconLocation = targetExe + ",0";
                shortcut.Description = "Herciniamihomo Pro - 1,000Hz Liquid Glass Mihomo Client";
                shortcut.Save();
            }
            catch { }
        }
    }
}
