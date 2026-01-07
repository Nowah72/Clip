using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using ClipboardManagerCS.Models;
using Newtonsoft.Json;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Interop;
using System.ComponentModel;
using System.Windows.Data;
using SysDrawing = System.Drawing;
using SysDrawingImaging = System.Drawing.Imaging;
namespace ClipboardManagerCS
{
    public partial class MainWindow : Window
    {
        private ClipboardData clipboardData = new();
        private readonly string dataFilePath;
        private string searchQuery = string.Empty;
        private bool deleteMode = false;
        private bool isDarkMode = true;
        private int selectedMonitor = 0; // 0 = all monitors, 1+ = specific monitor
        private string currentShortcut = "Alt + V";
        private HashSet<string> selectedItems = new();
        private string? lastHash = null;
        private DispatcherTimer clipboardMonitor;
        private bool ignoreNextClipboardChange = false;
        private System.Windows.Forms.NotifyIcon? notifyIcon;

        // ---- Global hotkey ----
        private const int HOTKEY_ID = 9000;
        private HwndSource? hwndSource;
        private bool hotkeyRegistered = false;

        // ---- P/Invoke declarations for global hotkey ----
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        // ---- Shortcut capture (settings) ----
        private bool isCapturingShortcut = false;
        private Border? activeShortcutBorder;
        private TextBlock? activeShortcutText;
        private TextBlock? shortcutHintText;

        // ---- Monitor dropdown (settings) ----
        private Border? monitorDropdownBorder;
        private TextBlock? monitorDropdownText;

        // ---- Drag & Drop for Groups ----
        // We treat MouseLeftButtonDown as "maybe click" and only start a real drag once the pointer
        // moves past the system drag threshold. While dragging we auto-switch to the Groups tab so
        // you can see the drop targets.
        private ClipboardItem? draggedItem = null;
        private Point dragStartPoint;
        private bool isDragging = false;
        private bool isMouseDownOnItem = false;

        public MainWindow()
        {
            InitializeComponent();

            // Set data file path
            var appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".clipboard_manager"
            );
            Directory.CreateDirectory(appDataPath);
            dataFilePath = System.IO.Path.Combine(appDataPath, "data.json");

            // Load existing data
            LoadData();

            // Setup system tray
            SetupSystemTray();

            // Setup clipboard monitoring
            clipboardMonitor = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            clipboardMonitor.Tick += ClipboardMonitor_Tick;
            clipboardMonitor.Start();

            // Initial populate
            PopulateLists();
        }


protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);

    hwndSource = (HwndSource)PresentationSource.FromVisual(this);
    hwndSource?.AddHook(WndProc);

    // Try to register the current shortcut at startup
    TryRegisterHotkey(currentShortcut);
}

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // Show the window when hotkey is pressed
                Show();
                WindowState = WindowState.Normal;
                Activate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private bool TryRegisterHotkey(string shortcut)
        {
            // First unregister any existing hotkey
            if (hotkeyRegistered)
            {
                UnregisterHotkey();
            }

            if (hwndSource == null)
                return false;

            // Parse the shortcut string
            var parts = shortcut.Split('+').Select(p => p.Trim()).ToArray();
            uint modifiers = 0;
            uint key = 0;

            // Modifier constants
            const uint MOD_ALT = 0x0001;
            const uint MOD_CONTROL = 0x0002;
            const uint MOD_SHIFT = 0x0004;
            const uint MOD_WIN = 0x0008;

            foreach (var part in parts)
            {
                switch (part.ToUpper())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "ALT":
                        modifiers |= MOD_ALT;
                        break;
                    case "SHIFT":
                        modifiers |= MOD_SHIFT;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= MOD_WIN;
                        break;
                    default:
                        // This should be the key
                        if (part.Length == 1)
                        {
                            key = (uint)char.ToUpper(part[0]);
                        }
                        break;
                }
            }

            if (key == 0)
                return false;

            bool success = RegisterHotKey(hwndSource.Handle, HOTKEY_ID, modifiers, key);
            hotkeyRegistered = success;
            return success;
        }

        private void UnregisterHotkey()
        {
            if (hwndSource != null && hotkeyRegistered)
            {
                UnregisterHotKey(hwndSource.Handle, HOTKEY_ID);
                hotkeyRegistered = false;
            }
        }

        private void SetupSystemTray()
        {
            notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = SysDrawing.SystemIcons.Application,
                Visible = true,
                Text = "Clip - Clipboard Manager"
            };

            // Single click to show
            notifyIcon.Click += (s, e) =>
            {
                if (e is System.Windows.Forms.MouseEventArgs mouseEvent && mouseEvent.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    Show();
                    Activate();
                }
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Show Clipboard", null, (s, e) =>
            {
                Show();
                Activate();
            });
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) =>
            {
                UnregisterHotkey();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                Application.Current.Shutdown();
            });

            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position window at bottom-right
            var screenBounds = SystemParameters.WorkArea;
            Left = screenBounds.Right - Width - 20;
            Top = screenBounds.Bottom - Height - 20;

            ApplyTheme();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Don't actually close, just hide
            e.Cancel = true;
            Hide();
        }

        #region Clipboard Monitoring

        private void ClipboardMonitor_Tick(object? sender, EventArgs e)
        {
            if (ignoreNextClipboardChange)
            {
                ignoreNextClipboardChange = false;
                return;
            }

            try
            {
                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        var imageBytes = BitmapSourceToBytes(image);
                        var hash = ComputeHash(imageBytes);

                        if (hash != lastHash)
                        {
                            lastHash = hash;
                            SaveImage(image, hash);
                        }
                    }
                }
                else if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var hash = ComputeHash(Encoding.UTF8.GetBytes(text));

                        if (hash != lastHash)
                        {
                            lastHash = hash;
                            SaveText(text, hash);
                        }
                    }
                }
            }
            catch
            {
                // Clipboard access can fail, ignore
            }
        }

        #endregion

        #region Data Persistence

        private void LoadData()
        {
            if (File.Exists(dataFilePath))
            {
                try
                {
                    var json = File.ReadAllText(dataFilePath);
                    var data = JsonConvert.DeserializeObject<ClipboardData>(json);
                    if (data != null)
                    {
                        clipboardData = data;
                    }
                }
                catch
                {
                    // If load fails, start with empty data
                }
            }
        }

        private void SaveData()
        {
            try
            {
                var json = JsonConvert.SerializeObject(clipboardData, Formatting.Indented);
                File.WriteAllText(dataFilePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }

        #endregion

        #region Save Items

        private void SaveText(string text, string hash)
        {
            var preview = text.Length > 100 ? text.Substring(0, 100) + "..." : text;

            var entry = new ClipboardItem
            {
                Type = "text",
                Hash = hash,
                Preview = preview,
                Content = text,
                Timestamp = DateTime.Now,
                IsStarred = false,
                Metadata = new Dictionary<string, object>
                {
                    ["char_count"] = text.Length,
                    ["word_count"] = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
                    ["line_count"] = text.Split('\n').Length,
                    ["size_bytes"] = Encoding.UTF8.GetByteCount(text),
                    ["created"] = DateTime.Now.ToString("o")
                }
            };

            // Remove duplicates
            clipboardData.Text.RemoveAll(x => x.Hash == hash);

            // Add to front
            clipboardData.Text.Insert(0, entry);

            SaveData();
            Dispatcher.Invoke(() => PopulateLists());
        }

        private void SaveImage(BitmapSource image, string hash)
        {
            // Create thumbnail
            var thumbnail = CreateThumbnail(image, 80, 80);
            var thumbnailBase64 = BitmapToBase64(thumbnail);

            // Compress full image
            var compressed = image;
            if (image.PixelWidth > 800 || image.PixelHeight > 800)
            {
                var scale = Math.Min(800.0 / image.PixelWidth, 800.0 / image.PixelHeight);
                compressed = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            }
            var compressedBase64 = BitmapToBase64(compressed, 75);

            var entry = new ClipboardItem
            {
                Type = "image",
                Hash = hash,
                Thumbnail = thumbnailBase64,
                Content = compressedBase64,
                Timestamp = DateTime.Now,
                IsStarred = false,
                Metadata = new Dictionary<string, object>
                {
                    ["original_width"] = image.PixelWidth,
                    ["original_height"] = image.PixelHeight,
                    ["stored_width"] = compressed.PixelWidth,
                    ["stored_height"] = compressed.PixelHeight,
                    ["format"] = "JPEG",
                    ["created"] = DateTime.Now.ToString("o")
                }
            };

            // Check if already exists - don't add duplicates
            if (clipboardData.Images.Any(x => x.Hash == hash))
            {
                return; // Already exists, don't add
            }

            // Add to front
            clipboardData.Images.Insert(0, entry);

            SaveData();
            Dispatcher.Invoke(() => PopulateLists());
        }

        #endregion

        #region Populate Lists

        private void PopulateLists()
        {
            TextItemsControl.Items.Clear();
            ImagesItemsControl.Items.Clear();
            StarredItemsControl.Items.Clear();

            // Filter text items by search
            var textItems = clipboardData.Text.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                textItems = textItems.Where(x => x.Content.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
            }

            // Populate text tab
            if (!textItems.Any())
            {
                TextItemsControl.Items.Add(CreateEmptyState(
                    "\uE70F",
                    string.IsNullOrWhiteSpace(searchQuery) ? "No text items yet" : "No items match your search",
                    string.IsNullOrWhiteSpace(searchQuery) ? "Copy some text to get started" : ""
                ));
            }
            else
            {
                foreach (var item in textItems)
                {
                    TextItemsControl.Items.Add(CreateTextItemCard(item));
                }
            }

            // Populate images tab
            if (!clipboardData.Images.Any())
            {
                ImagesItemsControl.Items.Add(CreateEmptyState("\uE91B", "No image items yet", "Copy an image to get started"));
            }
            else
            {
                foreach (var item in clipboardData.Images)
                {
                    ImagesItemsControl.Items.Add(CreateImageItemCard(item));
                }
            }

            // Populate starred tab
            var starred = clipboardData.Text.Concat(clipboardData.Images)
                .Where(x => x.IsStarred)
                .OrderByDescending(x => x.Timestamp);

            if (!starred.Any())
            {
                StarredItemsControl.Items.Add(CreateEmptyState("\uE735", "No starred items yet", "Star items to save them"));
            }
            else
            {
                foreach (var item in starred)
                {
                    if (item.Type == "text")
                        StarredItemsControl.Items.Add(CreateTextItemCard(item));
                    else
                        StarredItemsControl.Items.Add(CreateImageItemCard(item));
                }
            }

            PopulateGroups();
            UpdateFooter();
        }

        private string TruncateText(string text, int maxLength)
        {
            if (text.Length <= maxLength)
                return text;

            var truncated = text.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 0)
                truncated = truncated.Substring(0, lastSpace);

            return truncated + "...";
        }

        private Border CreateEmptyState(string icon, string message, string helpText)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 60)
            };

            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Opacity = 0.3
            });

            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 15,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            if (!string.IsNullOrEmpty(helpText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = helpText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            return new Border { Child = stack };
        }

        private Grid CreateTextItemCard(ClipboardItem item)
        {
            var grid = new Grid();
            var isExpanded = false;
            var lineCount = item.Content.Split('\n').Length;
            var isLongText = lineCount > 4 || item.Content.Length > 200;

            // Main card border
            var border = new Border
            {
                Background = (Brush)Resources["CardBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(16),
                Cursor = Cursors.Hand,
                Tag = item
            };

            if (deleteMode && selectedItems.Contains(item.Hash))
            {
                border.Background = new SolidColorBrush(Color.FromArgb(100, 239, 68, 68));
                border.BorderBrush = (Brush)Resources["DangerRed"];
                border.BorderThickness = new Thickness(2);
            }

	            border.MouseLeftButtonDown += (s, e) =>
	            {
	                if (deleteMode) { ItemCard_Click(item); return; }

	                // Mark as potential click; we'll only execute the click on MouseUp if we didn't drag.
	                isMouseDownOnItem = true;
	                dragStartPoint = e.GetPosition(null);
	                draggedItem = item;
	                border.CaptureMouse();
	                e.Handled = true;
	            };

	            border.MouseLeftButtonUp += (s, e) =>
	            {
	                if (deleteMode) return;

	                border.ReleaseMouseCapture();
	                if (!isDragging && isMouseDownOnItem)
	                {
	                    // This was a normal click (no drag) -> copy/open as before.
	                    ItemCard_Click(item);
	                }
	                isMouseDownOnItem = false;
	                draggedItem = null;
	            };

	            border.MouseMove += (s, e) =>
	            {
	                if (deleteMode) return;
	                if (!isMouseDownOnItem) return;
	                if (e.LeftButton != MouseButtonState.Pressed) return;
	                if (draggedItem == null) return;

	                var currentPos = e.GetPosition(null);
	                var diff = dragStartPoint - currentPos;

	                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
	                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
	                {
	                    isDragging = true;

	                    // Auto-switch to Groups so drop targets are visible.
	                    if (MainTabControl != null && GroupsTab != null)
	                        MainTabControl.SelectedItem = GroupsTab;

	                    border.Opacity = 0.5;
	                    try
	                    {
	                        DragDrop.DoDragDrop(border, item, DragDropEffects.Move);
	                    }
	                    finally
	                    {
	                        border.Opacity = 1.0;
	                        isDragging = false;
	                        isMouseDownOnItem = false;
	                        draggedItem = null;
	                        border.ReleaseMouseCapture();
	                    }
	                }
	            };

            border.MouseEnter += (s, e) =>
            {
                if (!deleteMode || !selectedItems.Contains(item.Hash))
                    border.Background = (Brush)Resources["CardHover"];
            };
            border.MouseLeave += (s, e) =>
            {
                if (!deleteMode || !selectedItems.Contains(item.Hash))
                    border.Background = (Brush)Resources["CardBackground"];
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(100, 239, 68, 68));
                }
            };

            var mainStack = new StackPanel();

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Content - initially collapsed for long text
            var textBlock = new TextBlock
            {
                Text = isLongText && !isExpanded ? TruncateText(item.Content, 200) : item.Content,
                FontSize = 13,
                Foreground = (Brush)Resources["TextPrimary"],
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(textBlock, 0);
            topGrid.Children.Add(textBlock);

            // Buttons - aligned to top
            if (!deleteMode)
            {
                var buttonStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };

                var metadataBtn = CreateColorfulIconButton("\uE946", () => ShowMetadata(item), "#06B6D4");
                buttonStack.Children.Add(metadataBtn);

                var starBtn = CreateColorfulIconButton(item.IsStarred ? "\uE735" : "\uE734", () => ToggleStar(item), item.IsStarred ? "#FCD34D" : "#555555");
                buttonStack.Children.Add(starBtn);

                Grid.SetColumn(buttonStack, 1);
                topGrid.Children.Add(buttonStack);
            }

            mainStack.Children.Add(topGrid);

            // Expand/Collapse button for long text
            if (isLongText)
            {
                var expandButton = new Button
                {
                    Width = 24,
                    Height = 24,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                var arrowIcon = new TextBlock
                {
                    Text = "\uE70D", // ChevronDown
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                expandButton.Content = arrowIcon;

                var expandTemplate = new ControlTemplate(typeof(Button));
                var expandBorder = new FrameworkElementFactory(typeof(Border));
                expandBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                expandBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                var expandContentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                expandContentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                expandContentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                expandBorder.AppendChild(expandContentPresenter);
                expandTemplate.VisualTree = expandBorder;
                expandButton.Template = expandTemplate;

                expandButton.Click += (s, e) =>
                {
                    e.Handled = true;
                    isExpanded = !isExpanded;
                    textBlock.Text = isExpanded ? item.Content : TruncateText(item.Content, 200);
                    arrowIcon.Text = isExpanded ? "\uE70E" : "\uE70D"; // ChevronUp : ChevronDown
                };

                mainStack.Children.Add(expandButton);
            }

            border.Child = mainStack;
            grid.Children.Add(border);

            // Yellow ribbon for starred items - FIXED ORIENTATION
            if (item.IsStarred && !deleteMode)
            {
                var ribbon = new Polygon
                {
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                    Points = new PointCollection { new Point(0, 0), new Point(28, 0), new Point(28, 28) },
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                grid.Children.Add(ribbon);
            }

            return grid;
        }

        private Grid CreateImageItemCard(ClipboardItem item)
        {
            var grid = new Grid();

            var border = new Border
            {
                Background = (Brush)Resources["CardBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(16),
                Cursor = Cursors.Hand,
                Tag = item
            };

            if (deleteMode && selectedItems.Contains(item.Hash))
            {
                border.Background = new SolidColorBrush(Color.FromArgb(100, 239, 68, 68));
                border.BorderBrush = (Brush)Resources["DangerRed"];
                border.BorderThickness = new Thickness(2);
            }

	            border.MouseLeftButtonDown += (s, e) =>
	            {
	                if (deleteMode) { ItemCard_Click(item); return; }

	                isMouseDownOnItem = true;
	                dragStartPoint = e.GetPosition(null);
	                draggedItem = item;
	                border.CaptureMouse();
	                e.Handled = true;
	            };

	            border.MouseLeftButtonUp += (s, e) =>
	            {
	                if (deleteMode) return;

	                border.ReleaseMouseCapture();
	                if (!isDragging && isMouseDownOnItem)
	                {
	                    ItemCard_Click(item);
	                }
	                isMouseDownOnItem = false;
	                draggedItem = null;
	            };

	            border.MouseMove += (s, e) =>
	            {
	                if (deleteMode) return;
	                if (!isMouseDownOnItem) return;
	                if (e.LeftButton != MouseButtonState.Pressed) return;
	                if (draggedItem == null) return;

	                var currentPos = e.GetPosition(null);
	                var diff = dragStartPoint - currentPos;

	                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
	                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
	                {
	                    isDragging = true;

	                    // Auto-switch to Groups so drop targets are visible.
	                    if (MainTabControl != null && GroupsTab != null)
	                        MainTabControl.SelectedItem = GroupsTab;

	                    border.Opacity = 0.5;
	                    try
	                    {
	                        DragDrop.DoDragDrop(border, item, DragDropEffects.Move);
	                    }
	                    finally
	                    {
	                        border.Opacity = 1.0;
	                        isDragging = false;
	                        isMouseDownOnItem = false;
	                        draggedItem = null;
	                        border.ReleaseMouseCapture();
	                    }
	                }
	            };

            border.MouseEnter += (s, e) =>
            {
                if (!deleteMode || !selectedItems.Contains(item.Hash))
                    border.Background = (Brush)Resources["CardHover"];
            };
            border.MouseLeave += (s, e) =>
            {
                if (!deleteMode || !selectedItems.Contains(item.Hash))
                    border.Background = (Brush)Resources["CardBackground"];
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(100, 239, 68, 68));
                }
            };

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Thumbnail
            try
            {
                var imageSource = Base64ToBitmap(item.Thumbnail ?? item.Content);
                var image = new Image
                {
                    Source = imageSource,
                    Width = 60,
                    Height = 60,
                    Margin = new Thickness(0, 0, 12, 0),
                    Stretch = Stretch.UniformToFill
                };

                var imageBorder = new Border
                {
                    Child = image,
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true
                };

                Grid.SetColumn(imageBorder, 0);
                innerGrid.Children.Add(imageBorder);
            }
            catch { }

            Grid.SetColumn(new Border(), 1);

            // Buttons
            if (!deleteMode)
            {
                var buttonStack = new StackPanel { Orientation = Orientation.Horizontal };

                var viewBtn = CreateColorfulIconButton("\uE890", () => ViewImage(item), "#A855F7");
                buttonStack.Children.Add(viewBtn);

                var metadataBtn = CreateColorfulIconButton("\uE946", () => ShowMetadata(item), "#06B6D4");
                buttonStack.Children.Add(metadataBtn);

                var starBtn = CreateColorfulIconButton(item.IsStarred ? "\uE735" : "\uE734", () => ToggleStar(item), item.IsStarred ? "#FCD34D" : "#555555");
                buttonStack.Children.Add(starBtn);

                Grid.SetColumn(buttonStack, 2);
                innerGrid.Children.Add(buttonStack);
            }

            border.Child = innerGrid;
            grid.Children.Add(border);

            // Yellow ribbon for starred items - FIXED ORIENTATION
            if (item.IsStarred && !deleteMode)
            {
                var ribbon = new Polygon
                {
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                    Points = new PointCollection { new Point(0, 0), new Point(28, 0), new Point(28, 28) },
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                grid.Children.Add(ribbon);
            }

            return grid;
        }

        private Button CreateColorfulIconButton(string icon, Action onClick, string color)
        {
            var iconText = new TextBlock
            {
                Text = icon,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = (Brush)Resources["TextPrimary"]
            };

            var button = new Button
            {
                Content = iconText,
                Width = 36,
                Height = 36,
                Margin = new Thickness(4, 0, 0, 0),
                Background = (Brush)Resources["SecondaryBackground"],
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand
            };

            button.Click += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var viewboxFactory = new FrameworkElementFactory(typeof(Viewbox));
            viewboxFactory.SetValue(Viewbox.WidthProperty, 18.0);
            viewboxFactory.SetValue(Viewbox.HeightProperty, 18.0);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            viewboxFactory.AppendChild(contentFactory);
            borderFactory.AppendChild(viewboxFactory);

            template.VisualTree = borderFactory;
            button.Template = template;

            return button;
        }

        #endregion

        #region Item Actions

        private void ItemCard_Click(ClipboardItem item)
        {
            if (deleteMode)
            {
                if (selectedItems.Contains(item.Hash))
                    selectedItems.Remove(item.Hash);
                else
                    selectedItems.Add(item.Hash);

                UpdateDeleteCounter();
                PopulateLists();
            }
            else
            {
                CopyToClipboard(item);
            }
        }

        private void CopyToClipboard(ClipboardItem item)
{
    try
    {
        ignoreNextClipboardChange = true;

        if (item.Type == "text")
        {
            Clipboard.SetText(item.Content);
            lastHash = item.Hash; // ok
        }
        else
        {
            var bitmap = Base64ToBitmap(item.Content);
            Clipboard.SetImage(bitmap);

            // IMPORTANT: calculează hash exact ca monitorul
            var bytes = BitmapSourceToBytes(bitmap);
            lastHash = ComputeHash(bytes);
        }

        ShowCopyNotification();
    }
    catch { }
}

        private void ToggleStar(ClipboardItem item)
        {
            item.IsStarred = !item.IsStarred;
            SaveData();
            PopulateLists();
        }


private void ShowMetadata(ClipboardItem item)
        {
            ShowModal(CreateMetadataModal(item));
        }

        private Border CreateMetadataModal(ClipboardItem item)
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                MaxWidth = 360,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            titleStack.Children.Add(new TextBlock
            {
                Text = item.Type == "text" ? "\uE70F" : "\uE91B",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = item.Type == "text" ? "Text Metadata" : "Image Metadata",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            stack.Children.Add(titleStack);

            var metadataGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            metadataGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            metadataGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var metadataItems = new List<(string label, string value)>();

            if (item.Type == "text")
            {
                metadataItems.Add(("Created:", item.Metadata.GetValueOrDefault("created", "N/A")?.ToString() ?? "N/A"));
                metadataItems.Add(("Characters:", $"{item.Metadata.GetValueOrDefault("char_count", 0):N0}"));
                metadataItems.Add(("Words:", $"{item.Metadata.GetValueOrDefault("word_count", 0):N0}"));
                metadataItems.Add(("Lines:", $"{item.Metadata.GetValueOrDefault("line_count", 0):N0}"));
                metadataItems.Add(("Size:", $"{item.Metadata.GetValueOrDefault("size_bytes", 0):N0} bytes"));
                metadataItems.Add(("Hash:", item.Hash.Length > 16 ? item.Hash.Substring(0, 16) + "..." : item.Hash));
            }
            else
            {
                metadataItems.Add(("Created:", item.Metadata.GetValueOrDefault("created", "N/A")?.ToString() ?? "N/A"));
                metadataItems.Add(("Original:", $"{item.Metadata.GetValueOrDefault("original_width", 0)} × {item.Metadata.GetValueOrDefault("original_height", 0)}"));
                metadataItems.Add(("Stored:", $"{item.Metadata.GetValueOrDefault("stored_width", 0)} × {item.Metadata.GetValueOrDefault("stored_height", 0)}"));
                metadataItems.Add(("Format:", item.Metadata.GetValueOrDefault("format", "N/A")?.ToString() ?? "N/A"));
                metadataItems.Add(("Hash:", item.Hash.Length > 16 ? item.Hash.Substring(0, 16) + "..." : item.Hash));
            }

            for (int i = 0; i < metadataItems.Count; i++)
            {
                metadataGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var labelBlock = new TextBlock
                {
                    Text = metadataItems[i].label,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetRow(labelBlock, i);
                Grid.SetColumn(labelBlock, 0);
                metadataGrid.Children.Add(labelBlock);

                var valueBlock = new TextBlock
                {
                    Text = metadataItems[i].value,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(valueBlock, i);
                Grid.SetColumn(valueBlock, 1);
                metadataGrid.Children.Add(valueBlock);
            }

            stack.Children.Add(metadataGrid);

            var okButton = new Button
            {
                Content = "OK",
                Height = 36,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                }
            };

            okButton.Click += (s, e) => HideModal();

            var buttonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorder = new FrameworkElementFactory(typeof(Border));
            buttonBorder.SetValue(Border.BackgroundProperty, gradient);
            buttonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
            buttonContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            buttonContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonBorder.AppendChild(buttonContent);
            buttonTemplate.VisualTree = buttonBorder;
            okButton.Template = buttonTemplate;

            stack.Children.Add(okButton);

            mainBorder.Child = stack;
            return mainBorder;
        }

        private void ViewImage(ClipboardItem item)
        {
            ShowModal(CreateImageViewerModal(item));
        }

        private Border CreateImageViewerModal(ClipboardItem item)
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                MaxWidth = 600,
                MaxHeight = 500,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var grid = new Grid();

            var stack = new StackPanel();

            var image = new Image
            {
                Source = Base64ToBitmap(item.Content),
                Stretch = Stretch.Uniform,
                MaxHeight = 400,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(image);

            // Button panel in top-right corner
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -10, -10, 0)
            };

            // Expand button
            var expandBtn = new Button
            {
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(128, 26, 26, 26)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7")),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var expandIcon = new TextBlock
            {
                Text = "\uE740",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            expandBtn.Content = expandIcon;

            var expandTemplate = new ControlTemplate(typeof(Button));
            var expandBorder = new FrameworkElementFactory(typeof(Border));
            expandBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            expandBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            expandBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            expandBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var expandContentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            expandContentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            expandContentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            expandBorder.AppendChild(expandContentPresenter);
            expandTemplate.VisualTree = expandBorder;
            expandBtn.Template = expandTemplate;

            expandBtn.Click += (s, e) =>
            {
                OpenFullSizeImageWindow(item);
            };

            buttonPanel.Children.Add(expandBtn);

            // Open folder button
            var folderBtn = new Button
            {
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(128, 26, 26, 26)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4")),
                Cursor = Cursors.Hand
            };

            var folderIcon = new TextBlock
            {
                Text = "\uE8DA",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            folderBtn.Content = folderIcon;

            var folderTemplate = new ControlTemplate(typeof(Button));
            var folderBorder = new FrameworkElementFactory(typeof(Border));
            folderBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            folderBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            folderBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            folderBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var folderContentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            folderContentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            folderContentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            folderBorder.AppendChild(folderContentPresenter);
            folderTemplate.VisualTree = folderBorder;
            folderBtn.Template = folderTemplate;

            folderBtn.Click += (s, e) =>
            {
                SaveImageToTempAndOpenFolder(item);
            };

            buttonPanel.Children.Add(folderBtn);

            grid.Children.Add(stack);
            grid.Children.Add(buttonPanel);

            var closeButton = new Button
            {
                Content = "Close",
                Height = 36,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                }
            };

            closeButton.Click += (s, e) => HideModal();

            var buttonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorder = new FrameworkElementFactory(typeof(Border));
            buttonBorder.SetValue(Border.BackgroundProperty, gradient);
            buttonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
            buttonContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            buttonContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonBorder.AppendChild(buttonContent);
            buttonTemplate.VisualTree = buttonBorder;
            closeButton.Template = buttonTemplate;

            stack.Children.Add(closeButton);

            mainBorder.Child = grid;
            return mainBorder;
        }

        private void OpenFullSizeImageWindow(ClipboardItem item)
        {
            var window = new Window
            {
                Title = "Image Viewer",
                WindowState = WindowState.Maximized,
                Background = Brushes.Black,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                Topmost = true
            };

            var grid = new Grid();

            var image = new Image
            {
                Source = Base64ToBitmap(item.Content),
                Stretch = Stretch.Uniform
            };

            var closeBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = "\uE711",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 24,
                    Foreground = Brushes.White
                },
                Width = 50,
                Height = 50,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 20, 20, 0),
                Cursor = Cursors.Hand
            };

            var closeTemplate = new ControlTemplate(typeof(Button));
            var closeBorder = new FrameworkElementFactory(typeof(Border));
            closeBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            closeBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(25));
            var closeContent = new FrameworkElementFactory(typeof(ContentPresenter));
            closeContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeBorder.AppendChild(closeContent);
            closeTemplate.VisualTree = closeBorder;
            closeBtn.Template = closeTemplate;

            closeBtn.Click += (s, e) => window.Close();

            grid.Children.Add(image);
            grid.Children.Add(closeBtn);

            window.Content = grid;
            window.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                    window.Close();
            };

            window.Show();
        }

        private void SaveImageToTempAndOpenFolder(ClipboardItem item)
        {
            try
            {
                var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipboardManager");
                Directory.CreateDirectory(tempFolder);

                var fileName = $"clipboard_image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var filePath = System.IO.Path.Combine(tempFolder, fileName);

                var bitmap = Base64ToBitmap(item.Content);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(fileStream);
                }

                // Open folder and select the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Modal System

        private void ShowModal(UIElement content)
        {
            OverlayBackground.Visibility = Visibility.Visible;
            ModalContainer.Children.Clear();
            ModalContainer.Children.Add(content);
            ModalContainer.Visibility = Visibility.Visible;
            this.Focus();
        }

        private void HideModal()
        {
            ModalContainer.Visibility = Visibility.Collapsed;
            OverlayBackground.Visibility = Visibility.Collapsed;
            ModalContainer.Children.Clear();

            // If closing snip preview without taking an action, delete temp file
            if (!snipActionTaken && !string.IsNullOrEmpty(currentSnipTempPath))
            {
                DeleteTempSnip();
            }
        }

        private void CloseOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            HideModal();
        }

        private void ModalContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Could add animation here if needed
        }

        #endregion

        #region UI Actions

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchQuery = SearchBox.Text;
            PopulateLists();
        }


private void SnipButton_Click(object sender, RoutedEventArgs e)
        {
            Hide(); // Hide main window while snipping
            StartSnipping();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowModal(CreateSettingsModal());
        }

        private void DeleteModeButton_Click(object sender, RoutedEventArgs e)
        {
            deleteMode = !deleteMode;
            selectedItems.Clear();

            if (deleteMode)
            {
                DeleteModeIcon.Text = "\uE711";
                DeleteModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                DeleteBanner.Visibility = Visibility.Visible;
            }
            else
            {
                DeleteModeIcon.Text = "\uE74D";
                DeleteModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A"));
                DeleteBanner.Visibility = Visibility.Collapsed;
            }

            UpdateDeleteCounter();
            PopulateLists();
        }


private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            clipboardData.Text.RemoveAll(x => selectedItems.Contains(x.Hash));
            clipboardData.Images.RemoveAll(x => selectedItems.Contains(x.Hash));

            SaveData();
            selectedItems.Clear();
            DeleteModeButton_Click(sender, e);
        }

        private Border CreateSettingsModal()
        {
            var mainBorder = new Border
            {
                Background = (Brush)Resources["PrimaryBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Width = 450,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "\uE713",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["TextPrimary"],
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Settings",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titleStack);

            // Visual Section
            var visualHeader = new TextBlock
            {
                Text = "Visual",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(visualHeader);

            // Dark Mode Toggle
            var darkModeRow = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            darkModeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            darkModeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var darkModeLabelStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var darkModeLabel = new TextBlock
            {
                Text = "Dark Mode",
                FontSize = 13,
                Foreground = (Brush)Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center
            };
            darkModeLabelStack.Children.Add(darkModeLabel);

            var devTag = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            devTag.Child = new TextBlock
            {
                Text = "In development",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1F36"))
            };
            darkModeLabelStack.Children.Add(devTag);

            Grid.SetColumn(darkModeLabelStack, 0);
            darkModeRow.Children.Add(darkModeLabelStack);

            // iPhone-style toggle
            var toggleButton = CreateiPhoneToggle(isDarkMode);
            Grid.SetColumn(toggleButton, 1);
            darkModeRow.Children.Add(toggleButton);
            stack.Children.Add(darkModeRow);

            // Misc Section
            var miscHeader = new TextBlock
            {
                Text = "Misc",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(miscHeader);

            // Multi-Monitor Dropdown
            var monitorRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            monitorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            monitorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            var monitorLabel = new TextBlock
            {
                Text = "Multi-Monitor",
                FontSize = 13,
                Foreground = (Brush)Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(monitorLabel, 0);
            monitorRow.Children.Add(monitorLabel);

            var monitorDropdown = CreateMonitorDropdown();
            Grid.SetColumn(monitorDropdown, 1);
            monitorRow.Children.Add(monitorDropdown);
            stack.Children.Add(monitorRow);

            // Shortcut Configuration
            var shortcutRow = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            shortcutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shortcutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            var shortcutLabel = new TextBlock
            {
                Text = "Shortcut to open",
                FontSize = 13,
                Foreground = (Brush)Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(shortcutLabel, 0);
            shortcutRow.Children.Add(shortcutLabel);

            var shortcutButton = CreateShortcutButton(currentShortcut);
            Grid.SetColumn(shortcutButton, 1);
            shortcutRow.Children.Add(shortcutButton);
            stack.Children.Add(shortcutRow);

            shortcutHintText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, -10, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(shortcutHintText);

            // Close Button
            var closeButton = new Button
            {
                Content = "Close",
                Height = 40,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                }
            };

            closeButton.Click += (s, e) => HideModal();

            var buttonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorder = new FrameworkElementFactory(typeof(Border));
            buttonBorder.SetValue(Border.BackgroundProperty, gradient);
            buttonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
            buttonContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            buttonContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonBorder.AppendChild(buttonContent);
            buttonTemplate.VisualTree = buttonBorder;
            closeButton.Template = buttonTemplate;

            stack.Children.Add(closeButton);

            mainBorder.Child = stack;
            return mainBorder;
        }

        private Border CreateiPhoneToggle(bool isOn)
        {
            var toggleBorder = new Border
            {
                Width = 52,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = isOn
                    ? new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                            new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                        }
                    }
                    : (Brush)Resources["BorderBrush"],
                Cursor = Cursors.Hand
            };

            var canvas = new Canvas();
            var knob = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Brushes.White,
                Margin = new Thickness(isOn ? 23 : 3, 3, 0, 0)
            };

            canvas.Children.Add(knob);
            toggleBorder.Child = canvas;

            toggleBorder.MouseLeftButtonDown += (s, e) =>
            {
                isOn = !isOn;
                isDarkMode = isOn;
                toggleBorder.Background = isOn
                    ? new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                            new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                        }
                    }
                    : (Brush)Resources["BorderBrush"];
                knob.Margin = new Thickness(isOn ? 23 : 3, 3, 0, 0);
                ApplyTheme();
                HideModal();
                ShowModal(CreateSettingsModal());
            };

            return toggleBorder;
        }

        
private void ApplyTheme()
{
    // Update the SolidColorBrush resources in-place so all DynamicResource bindings update immediately.
    void SetBrush(string key, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        if (Resources[key] is SolidColorBrush scb && !scb.IsFrozen)
            scb.Color = c;
        else
            Resources[key] = new SolidColorBrush(c);
    }

    if (isDarkMode)
    {
        SetBrush("PrimaryBackground", "#0A0A0A");
        SetBrush("SecondaryBackground", "#111111");
        SetBrush("CardBackground", "#1A1A1A");
        SetBrush("CardHover", "#222222");
        SetBrush("BorderBrush", "#2A2A2A");
        SetBrush("TextPrimary", "#E5E5E5");
        SetBrush("TextSecondary", "#888888");
        SetBrush("TextTertiary", "#555555");
        SetBrush("SuccessGreen", "#10B981");
        SetBrush("DangerRed", "#EF4444");
        SetBrush("WarningYellow", "#FCD34D");
    }
    else
    {
        // Light mode palette - warm and comfortable
        SetBrush("PrimaryBackground", "#EEF0F2");
        SetBrush("SecondaryBackground", "#F5F6F7");
        SetBrush("CardBackground", "#FAFBFB");
        SetBrush("CardHover", "#FFFFFF");
        SetBrush("BorderBrush", "#D4D7DC");
        SetBrush("TextPrimary", "#1A1F36");
        SetBrush("TextSecondary", "#697386");
        SetBrush("TextTertiary", "#8B95A5");
        SetBrush("SuccessGreen", "#10B981");
        SetBrush("DangerRed", "#EF4444");
        SetBrush("WarningYellow", "#F59E0B");
    }

    if (shortcutHintText != null)
        shortcutHintText.Foreground = (Brush)Resources["TextSecondary"];
}


        
private Border CreateMonitorDropdown()
{
    monitorDropdownBorder = new Border
    {
        Background = (Brush)Resources["CardBackground"],
        BorderBrush = (Brush)Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 8),
        Cursor = Cursors.Hand
    };

    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

    monitorDropdownText = new TextBlock
    {
        Text = GetMonitorDisplayText(selectedMonitor),
        FontSize = 13,
        Foreground = (Brush)Resources["TextPrimary"],
        VerticalAlignment = VerticalAlignment.Center
    };
    Grid.SetColumn(monitorDropdownText, 0);
    grid.Children.Add(monitorDropdownText);

    var arrow = new TextBlock
    {
        Text = "\uE70D",
        FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
        FontSize = 12,
        Foreground = (Brush)Resources["TextSecondary"],
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0)
    };
    Grid.SetColumn(arrow, 1);
    grid.Children.Add(arrow);

    monitorDropdownBorder.Child = grid;

    var menu = new ContextMenu
    {
        Background = (Brush)Resources["SecondaryBackground"],
        BorderBrush = (Brush)Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        Padding = new Thickness(4),
        HasDropShadow = true,
        UseLayoutRounding = true,
        SnapsToDevicePixels = true
    };

    // Style for MenuItem (removes left gutter reserved for icon/checkmark)
    var menuItemStyle = new Style(typeof(MenuItem));

    // Control template for MenuItem (flat modern look, no icon column)
    var template = new ControlTemplate(typeof(MenuItem));

    var borderFactory = new FrameworkElementFactory(typeof(Border));
    borderFactory.Name = "ItemBorder";
    borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
    borderFactory.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
    borderFactory.SetValue(Border.MarginProperty, new Thickness(2));

    var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
    contentFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
    contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
    contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
    borderFactory.AppendChild(contentFactory);

    template.VisualTree = borderFactory;

    // Hover trigger
    var hoverTrigger = new Trigger { Property = MenuItem.IsMouseOverProperty, Value = true };
    hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Resources["CardHover"], "ItemBorder"));
    template.Triggers.Add(hoverTrigger);

    menuItemStyle.Setters.Add(new Setter(MenuItem.TemplateProperty, template));
    menuItemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, Resources["TextPrimary"]));
    menuItemStyle.Setters.Add(new Setter(MenuItem.FontSizeProperty, 13.0));

    // IMPORTANT: remove icon/check gutter
    menuItemStyle.Setters.Add(new Setter(MenuItem.IconProperty, null));
    menuItemStyle.Setters.Add(new Setter(MenuItem.IsCheckableProperty, false));

    // IMPORTANT: apply style as ItemContainerStyle on ContextMenu
    menu.ItemContainerStyle = menuItemStyle;

    void AddItem(string header, int value)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = value
            // Style is applied via menu.ItemContainerStyle
        };

        item.Click += (s, e) =>
        {
            selectedMonitor = (int)((MenuItem)s!).Tag;
            if (monitorDropdownText != null)
                monitorDropdownText.Text = GetMonitorDisplayText(selectedMonitor);
        };

        menu.Items.Add(item);
    }

    AddItem("All Monitors", 0);

    try
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            AddItem($"Monitor {i + 1} ({s.Bounds.Width}x{s.Bounds.Height})", i + 1);
        }
    }
    catch
    {
        // ignore
    }

    monitorDropdownBorder.MouseLeftButtonDown += (s, e) =>
    {
        e.Handled = true;

        monitorDropdownBorder.ContextMenu = menu;
        menu.PlacementTarget = monitorDropdownBorder;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

        menu.IsOpen = true;
    };

    return monitorDropdownBorder;
}

private static string GetMonitorDisplayText(int selectedMonitor)
{
    return selectedMonitor <= 0 ? "All Monitors" : $"Monitor {selectedMonitor}";
}

        
private Border CreateShortcutButton(string shortcut)
{
    var buttonBorder = new Border
    {
        Background = (Brush)Resources["CardBackground"],
        BorderBrush = (Brush)Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 8),
        Cursor = Cursors.Hand
    };

    var text = new TextBlock
    {
        Text = shortcut,
        FontSize = 13,
        Foreground = (Brush)Resources["TextPrimary"],
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    buttonBorder.Child = text;

    buttonBorder.MouseLeftButtonDown += (s, e) =>
    {
        e.Handled = true;

        isCapturingShortcut = true;
        activeShortcutBorder = buttonBorder;
        activeShortcutText = text;

        text.Text = "Press shortcut...";
        buttonBorder.BorderBrush = (Brush)Resources["AccentCyan"];

        if (shortcutHintText != null)
            shortcutHintText.Text = "Press your key combination now (e.g. Ctrl+Alt+C, Win+V, etc). Press Esc to cancel.";

        PreviewKeyDown -= MainWindow_PreviewKeyDownForShortcut;
        PreviewKeyDown += MainWindow_PreviewKeyDownForShortcut;
        Focus();
    };

    return buttonBorder;
}

private void MainWindow_PreviewKeyDownForShortcut(object sender, KeyEventArgs e)
{
    if (!isCapturingShortcut) return;

    e.Handled = true;

    if (e.Key == Key.Escape)
    {
        ExitShortcutCapture(cancelled: true);
        return;
    }

    var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

    if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        return;

    var mods = Keyboard.Modifiers;
    var shortcut = BuildShortcutString(mods, key);

    currentShortcut = shortcut;

    if (activeShortcutText != null) activeShortcutText.Text = shortcut;

    var ok = TryRegisterHotkey(shortcut);

    if (shortcutHintText != null)
    {
        shortcutHintText.Text = ok
            ? $"Hotkey set to: {shortcut}"
            : $"Could not register: {shortcut}. This shortcut may be reserved by Windows or another app. Pick a different one.";
    }

    ExitShortcutCapture(cancelled: false);
}

private void ExitShortcutCapture(bool cancelled)
{
    isCapturingShortcut = false;
    PreviewKeyDown -= MainWindow_PreviewKeyDownForShortcut;

    if (activeShortcutBorder != null)
        activeShortcutBorder.BorderBrush = (Brush)Resources["BorderBrush"];

    if (cancelled)
    {
        if (activeShortcutText != null) activeShortcutText.Text = currentShortcut;
        if (shortcutHintText != null) shortcutHintText.Text = "Shortcut selection cancelled.";
    }

    activeShortcutBorder = null;
    activeShortcutText = null;
}

private static string BuildShortcutString(ModifierKeys mods, Key key)
{
    var parts = new List<string>();

    if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
    if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
    if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
    if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

    var keyName = key.ToString();
    if (key >= Key.A && key <= Key.Z)
        keyName = keyName.ToUpperInvariant();

    parts.Add(keyName);

    return string.Join(" + ", parts);
}


        private void DeleteAllButton_Click(object sender, MouseButtonEventArgs e)
        {
            var totalItems = clipboardData.Text.Count + clipboardData.Images.Count;
            if (totalItems == 0) return;

            ShowModal(CreateDeleteAllConfirmModal());
        }

        private Border CreateDeleteAllConfirmModal()
        {
            var mainBorder = new Border
            {
                Background = (Brush)Resources["PrimaryBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                MaxWidth = 360,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "\uE7BA",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["DangerRed"],
                Margin = new Thickness(0, 0, 8, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Delete All Data",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["DangerRed"]
            });
            stack.Children.Add(titleStack);

            var message = new TextBlock
            {
                Text = "Are you sure you want to permanently delete all stored data? This action cannot be undone.",
                FontSize = 13,
                Foreground = (Brush)Resources["TextPrimary"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(message);

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)Resources["CardBackground"],
                Foreground = (Brush)Resources["TextPrimary"],
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => HideModal();

            var deleteBtn = new Button
            {
                Content = "Delete All",
                Padding = new Thickness(16, 8, 16, 8),
                Background = (Brush)Resources["DangerRed"],
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            deleteBtn.Click += (s, e) =>
            {
                clipboardData.Text.Clear();
                clipboardData.Images.Clear();
                SaveData();
                PopulateLists();
                HideModal();
            };

            buttonStack.Children.Add(cancelBtn);
            buttonStack.Children.Add(deleteBtn);
            stack.Children.Add(buttonStack);

            mainBorder.Child = stack;
            return mainBorder;
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            ShowModal(CreateAddItemModal());
        }

        private Border CreateAddItemModal()
        {
            var mainBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                MaxWidth = 360,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "What would you like to add?",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            var textBtnStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            textBtnStack.Children.Add(new TextBlock
            {
                Text = "\uE70F",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            textBtnStack.Children.Add(new TextBlock
            {
                Text = "Text",
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var textBtn = new Button
            {
                Content = textBtnStack,
                Width = 120,
                Height = 80,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7")),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand
            };
            textBtn.Click += (s, ev) =>
            {
                HideModal();
                ShowModal(CreateAddTextModal());
            };

            var imageBtnStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            imageBtnStack.Children.Add(new TextBlock
            {
                Text = "\uE91B",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            imageBtnStack.Children.Add(new TextBlock
            {
                Text = "Image",
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var imageBtn = new Button
            {
                Content = imageBtnStack,
                Width = 120,
                Height = 80,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC4899")),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand
            };
            imageBtn.Click += (s, ev) =>
            {
                HideModal();
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                    Title = "Select Image"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                        var imageBytes = File.ReadAllBytes(openFileDialog.FileName);
                        var hash = ComputeHash(imageBytes);
                        SaveImage(bitmap, hash);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            buttonStack.Children.Add(textBtn);
            buttonStack.Children.Add(imageBtn);
            stack.Children.Add(buttonStack);

            mainBorder.Child = stack;
            return mainBorder;
        }

        private Border CreateAddTextModal()
{
    var mainBorder = new Border
    {
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(20),
        Width = 400,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    var grid = new Grid();
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    titleStack.Children.Add(new TextBlock
    {
        Text = "\uE70F",
        FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 8, 0)
    });
    titleStack.Children.Add(new TextBlock
    {
        Text = "Add Text",
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White
    });
    Grid.SetRow(titleStack, 0);
    grid.Children.Add(titleStack);

    var textBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111")),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
        Padding = new Thickness(10),
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 12)
    };
    Grid.SetRow(textBox, 1);
    grid.Children.Add(textBox);

    var buttonStack = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    // ---------- Templates (rounded) ----------
 ControlTemplate MakeRoundedButtonTemplate(Brush background, double radius = 8)
{
    var template = new ControlTemplate(typeof(Button));

    var border = new FrameworkElementFactory(typeof(Border));
    border.SetValue(Border.BackgroundProperty, background);
    border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));

    // IMPORTANT: default padding (will be overridden by Button.Padding automatically)
    border.SetValue(Border.PaddingProperty, new Thickness(32, 10, 32, 10));

    var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
    presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
    presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

    border.AppendChild(presenter);
    template.VisualTree = border;

    return template;
}
    var gradient = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 0),
        GradientStops = new GradientStopCollection
        {
            new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
            new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
        }
    };

    var cancelBtn = new Button
    {
        Content = "Cancel",
        Padding = new Thickness(32, 8, 32, 8), // match Add Text width feel
        Margin = new Thickness(0, 0, 10, 0),
        Background = Brushes.Transparent,      // template handles background
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
        Template = MakeRoundedButtonTemplate(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
            8
        )
    };
    cancelBtn.Click += (s, e) => HideModal();

    var addBtn = new Button
    {
        Content = "Add Text",
        Padding = new Thickness(32, 8, 32, 8),
        Background = Brushes.Transparent,      // template handles background
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
        Template = MakeRoundedButtonTemplate(gradient, 8)
    };

    addBtn.Click += (s, e) =>
    {
        var text = textBox.Text.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var hash = ComputeHash(Encoding.UTF8.GetBytes(text));
            SaveText(text, hash);
            HideModal();
        }
    };

    buttonStack.Children.Add(cancelBtn);
    buttonStack.Children.Add(addBtn);
    Grid.SetRow(buttonStack, 2);
    grid.Children.Add(buttonStack);

    mainBorder.Child = grid;
    return mainBorder;
}

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Tab changed
        }

        private void ShowCopyNotification()
        {
            var text = new TextBlock
            {
                Text = "Copied!",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["SuccessGreen"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            // Position at bottom center of window
            Canvas.SetLeft(text, (ActualWidth / 2) - 40);
            Canvas.SetBottom(text, 60);

            AnimationCanvas.Children.Add(text);

            // Create fly-up animation
            var moveAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 60,
                To = 120,
                Duration = TimeSpan.FromSeconds(1.0),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };

            // Create fade-out animation
            var fadeAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(1.0),
                BeginTime = TimeSpan.FromSeconds(0)
            };

            // Remove element when animation completes
            fadeAnimation.Completed += (s, e) =>
            {
                AnimationCanvas.Children.Remove(text);
            };

            text.BeginAnimation(Canvas.BottomProperty, moveAnimation);
            text.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private void UpdateDeleteCounter()
        {
            DeleteCounterLabel.Text = $"{selectedItems.Count} item{(selectedItems.Count != 1 ? "s" : "")} selected";
            DeleteSelectedButton.IsEnabled = selectedItems.Count > 0;
        }

        private void UpdateFooter()
        {
            var total = clipboardData.Text.Count + clipboardData.Images.Count;
            FooterLabel.Text = $"{total} item{(total != 1 ? "s" : "")} stored";
        }

        #endregion

        #region Helper Methods

        private string ComputeHash(byte[] data)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private byte[] BitmapSourceToBytes(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private BitmapSource CreateThumbnail(BitmapSource source, int width, int height)
        {
            var scaleX = (double)width / source.PixelWidth;
            var scaleY = (double)height / source.PixelHeight;
            var scale = Math.Min(scaleX, scaleY);
            return new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        private string BitmapToBase64(BitmapSource bitmap, int quality = 100)
        {
            var encoder = quality < 100 ? (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = quality } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }

        private BitmapSource Base64ToBitmap(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        #endregion

        #region Snipping Tool

        private string? currentSnipTempPath = null;
        private SnipData? currentSnipData = null;
        private bool snipActionTaken = false;

        private void StartSnipping()
        {
            var snipWindow = new SnippingWindow();
            snipWindow.SnipCompleted += (snipData) =>
            {
                if (snipData != null)
                {
                    currentSnipData = snipData;
                    // Save full quality image to temp (using the full captured image, not compressed)
                    SaveSnipToTemp(snipData.FullImage);
                    Show();
                    Activate();
                    ShowSnipPreviewModal();
                }
                else
                {
                    Show();
                    Activate();
                }
            };
            snipWindow.Show();
        }

        private void SaveSnipToTemp(BitmapSource? bitmap)
        {
            if (bitmap == null) return;

            var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipboardManager", "Snips");
            Directory.CreateDirectory(tempFolder);

            var fileName = $"snip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            currentSnipTempPath = System.IO.Path.Combine(tempFolder, fileName);

            // Save at full quality as PNG
            using (var fileStream = new FileStream(currentSnipTempPath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }
        }

        private void ShowSnipPreviewModal()
        {
            if (string.IsNullOrEmpty(currentSnipTempPath) || !File.Exists(currentSnipTempPath))
                return;

            snipActionTaken = false; // Reset action flag

            var mainBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                MaxWidth = 500,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            // Title
            var title = new TextBlock
            {
                Text = "Snip Captured",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(title);

            // Thumbnail with drag-and-drop
            var thumbnailBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var thumbnail = new Image
            {
                Source = new BitmapImage(new Uri(currentSnipTempPath)),
                MaxHeight = 250,
                MaxWidth = 450,
                Stretch = Stretch.Uniform,
                Cursor = Cursors.Hand
            };

            // Enable drag-and-drop
            thumbnail.MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var dataObject = new DataObject(DataFormats.FileDrop, new string[] { currentSnipTempPath });
                    DragDrop.DoDragDrop(thumbnail, dataObject, DragDropEffects.Copy);
                }
            };

            thumbnailBorder.Child = thumbnail;
            stack.Children.Add(thumbnailBorder);

            // Hint text
            var hint = new TextBlock
            {
                Text = "Drag thumbnail to save anywhere",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(hint);

            // Buttons - arranged in a grid for better layout
            var buttonGrid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            buttonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Save button
            var saveBtn = CreateSnipActionButton("\uE74E", "Save", "#1A1A1A", () =>
            {
                snipActionTaken = true;
                SaveSnipToFile();
                HideModal();
            });
            saveBtn.Margin = new Thickness(0, 0, 5, 5);
            Grid.SetRow(saveBtn, 0);
            Grid.SetColumn(saveBtn, 0);
            buttonGrid.Children.Add(saveBtn);

            // Save to Clip button
            var saveToClipBtn = CreateSnipActionButton("\uE8C8", "Save to Clip", "#1A1A1A", () =>
            {
                snipActionTaken = true;
                SaveSnipToClipboard();
                HideModal();
            });
            saveToClipBtn.Margin = new Thickness(5, 0, 0, 5);
            Grid.SetRow(saveToClipBtn, 0);
            Grid.SetColumn(saveToClipBtn, 1);
            buttonGrid.Children.Add(saveToClipBtn);

            // Edit button
            var editBtn = CreateSnipActionButton("\uE70F", "Edit", "#1A1A1A", () =>
            {
                snipActionTaken = true;
                HideModal();
                ShowImageEditor();
            });
            editBtn.Margin = new Thickness(0, 5, 5, 0);
            Grid.SetRow(editBtn, 1);
            Grid.SetColumn(editBtn, 0);
            buttonGrid.Children.Add(editBtn);

            // Delete button
            var deleteBtn = CreateSnipActionButton("\uE74D", "Delete", "#1A1A1A", () =>
            {
                snipActionTaken = true;
                DeleteSnip();
                HideModal();
            });
            deleteBtn.Margin = new Thickness(5, 5, 0, 0);
            Grid.SetRow(deleteBtn, 1);
            Grid.SetColumn(deleteBtn, 1);
            buttonGrid.Children.Add(deleteBtn);

            stack.Children.Add(buttonGrid);
            mainBorder.Child = stack;
            ShowModal(mainBorder);
        }

        private Button CreateSnipActionButton(string icon, string text, string color, Action onClick)
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel.Children.Add(iconBlock);

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E5E5")),
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel.Children.Add(textBlock);

            var button = new Button
            {
                Content = stackPanel,
                Height = 42,
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            button.Click += (s, e) => onClick();

            button.MouseEnter += (s, e) =>
            {
                button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222222"));
            };

            button.MouseLeave += (s, e) =>
            {
                button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            };

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            button.Template = template;

            return button;
        }

        private void SaveSnipToFile()
        {
            if (string.IsNullOrEmpty(currentSnipTempPath) || !File.Exists(currentSnipTempPath))
                return;

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|All Files|*.*",
                DefaultExt = "png",
                FileName = $"snip_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (saveDialog.ShowDialog() == true)
            {
                // Copy at full quality (temp is already full quality PNG)
                File.Copy(currentSnipTempPath, saveDialog.FileName, true);
                DeleteTempSnip();
            }
        }

        private void SaveSnipToClipboard()
        {
            if (currentSnipData?.CroppedImage == null) return;

            // Use the cropped image (not full image)
            var bitmap = currentSnipData.CroppedImage;

            // Create thumbnail
            var thumbnail = CreateThumbnail(bitmap, 80, 80);
            var thumbnailBase64 = BitmapToBase64(thumbnail, 60);

            // Compress for storage
            BitmapSource compressed = bitmap;
            if (bitmap.PixelWidth > 800 || bitmap.PixelHeight > 800)
            {
                var scale = Math.Min(800.0 / bitmap.PixelWidth, 800.0 / bitmap.PixelHeight);
                compressed = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            }
            var base64 = BitmapToBase64(compressed, 75);
            var hash = ComputeHash(Convert.FromBase64String(base64));

            // Check for duplicates
            if (clipboardData.Images.Any(x => x.Hash == hash))
            {
                DeleteTempSnip();
                return;
            }

            var item = new ClipboardItem
            {
                Type = "image",
                Hash = hash,
                Content = base64,
                Thumbnail = thumbnailBase64,
                Timestamp = DateTime.Now,
                Metadata = new Dictionary<string, object>
                {
                    { "width", bitmap.PixelWidth },
                    { "height", bitmap.PixelHeight },
                    { "format", "Snip" }
                }
            };

            clipboardData.Images.Insert(0, item);
            SaveData();
            PopulateLists();
            DeleteTempSnip();
        }

        private void DeleteSnip()
        {
            DeleteTempSnip();
        }

        private void DeleteTempSnip()
        {
            if (!string.IsNullOrEmpty(currentSnipTempPath) && File.Exists(currentSnipTempPath))
            {
                try
                {
                    File.Delete(currentSnipTempPath);
                }
                catch { }
            }
            currentSnipTempPath = null;
        }

        private void ShowImageEditor()
        {
            if (currentSnipData == null) return;

            var editorWindow = new ImageEditorWindow(currentSnipData);
            editorWindow.ImageSaved += (editedBitmap) =>
            {
                if (editedBitmap != null)
                {
                    // Update the cropped image with the edited version
                    currentSnipData.CroppedImage = editedBitmap;
                    // Save edited image to temp at full quality
                    SaveSnipToTemp(editedBitmap);
                    ShowSnipPreviewModal();
                }
            };
            editorWindow.ShowDialog();
        }

        #endregion

        #region Snipping Window

        private class SnipData
        {
            public BitmapSource? FullImage { get; set; }
            public BitmapSource? CroppedImage { get; set; }
            public Int32Rect CropRect { get; set; }  // Crop area within the full image
        }

        private class SnippingWindow : Window
        {
            public event Action<SnipData?>? SnipCompleted;
            private SysDrawing.Point startPoint;
            private SysDrawing.Rectangle? selectionRect;
            private bool isSelecting;

            public SnippingWindow()
            {
                WindowState = WindowState.Maximized;
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
                Topmost = true;
                Cursor = Cursors.Cross;

                MouseDown += OnMouseDown;
                MouseMove += OnMouseMove;
                MouseUp += OnMouseUp;
                KeyDown += OnKeyDown;
            }

            private void OnMouseDown(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var pos = e.GetPosition(this);
                    startPoint = new SysDrawing.Point((int)pos.X, (int)pos.Y);
                    isSelecting = true;
                }
            }

            private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
            {
                if (isSelecting)
                {
                    var pos = e.GetPosition(this);
                    var currentPoint = new SysDrawing.Point((int)pos.X, (int)pos.Y);

                    int x = Math.Min(startPoint.X, currentPoint.X);
                    int y = Math.Min(startPoint.Y, currentPoint.Y);
                    int width = Math.Abs(startPoint.X - currentPoint.X);
                    int height = Math.Abs(startPoint.Y - currentPoint.Y);

                    selectionRect = new SysDrawing.Rectangle(x, y, width, height);
                    InvalidateVisual();
                }
            }

            private async void OnMouseUp(object sender, MouseButtonEventArgs e)
            {
                if (isSelecting && selectionRect.HasValue && selectionRect.Value.Width > 5 && selectionRect.Value.Height > 5)
                {
                    var rect = selectionRect.Value;
                    // Hide window first to avoid capturing the overlay
                    Hide();
                    // Wait for window to disappear
                    await System.Threading.Tasks.Task.Delay(100);
                    // Capture screen
                    CaptureScreen(rect);
                }
                else
                {
                    SnipCompleted?.Invoke(null);
                }
                Close();
            }

            private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    SnipCompleted?.Invoke(null);
                    Close();
                }
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);

                if (selectionRect.HasValue)
                {
                    var rect = new Rect(selectionRect.Value.X, selectionRect.Value.Y, selectionRect.Value.Width, selectionRect.Value.Height);
                    drawingContext.DrawRectangle(null, new System.Windows.Media.Pen(Brushes.Red, 2), rect);
                }
            }

            private void CaptureScreen(SysDrawing.Rectangle rect)
            {
                const int margin = 10;

                // Get screen bounds
                var screenBounds = System.Windows.Forms.Screen.FromPoint(new SysDrawing.Point(rect.X, rect.Y)).Bounds;

                // Calculate expanded rectangle with 10px margin, clamped to screen bounds
                var expandedRect = new SysDrawing.Rectangle(
                    Math.Max(rect.X - margin, screenBounds.X),
                    Math.Max(rect.Y - margin, screenBounds.Y),
                    Math.Min(rect.Width + margin * 2, screenBounds.Right - Math.Max(rect.X - margin, screenBounds.X)),
                    Math.Min(rect.Height + margin * 2, screenBounds.Bottom - Math.Max(rect.Y - margin, screenBounds.Y))
                );

                // Capture the expanded area
                using (var fullBitmap = new SysDrawing.Bitmap(expandedRect.Width, expandedRect.Height))
                {
                    using (var g = SysDrawing.Graphics.FromImage(fullBitmap))
                    {
                        g.CopyFromScreen(expandedRect.X, expandedRect.Y, 0, 0, expandedRect.Size);
                    }

                    // Convert full bitmap to BitmapSource
                    var hBitmapFull = fullBitmap.GetHbitmap();
                    BitmapSource fullBitmapSource;
                    try
                    {
                        fullBitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmapFull,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        fullBitmapSource.Freeze();
                    }
                    finally
                    {
                        DeleteObject(hBitmapFull);
                    }

                    // Calculate crop rectangle within the full image
                    var cropRect = new Int32Rect(
                        rect.X - expandedRect.X,
                        rect.Y - expandedRect.Y,
                        rect.Width,
                        rect.Height
                    );

                    // Create cropped bitmap
                    var croppedBitmap = new CroppedBitmap(fullBitmapSource, cropRect);
                    croppedBitmap.Freeze();

                    // Create snip data
                    var snipData = new SnipData
                    {
                        FullImage = fullBitmapSource,
                        CroppedImage = croppedBitmap,
                        CropRect = cropRect
                    };

                    SnipCompleted?.Invoke(snipData);
                }
            }

            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);
        }

        #endregion

        #region Image Editor Window

        private class ImageEditorWindow : Window
        {
            public event Action<BitmapSource?>? ImageSaved;

            private BitmapSource currentImage;
            private Int32Rect cropRect;
            private Image imageControl = null!;
            private Canvas overlayCanvas = null!;
            private Canvas cropCanvas = null!;
            private bool isDragging = false;
            private string dragHandle = "";
            private Point dragStart;
            private double currentScale = 1.0;
            private int dragStartX, dragStartY, dragStartWidth, dragStartHeight;

            public ImageEditorWindow(SnipData snipData)
            {
                Title = "Edit Image";
                Width = 800;
                Height = 600;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A"));
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;

                currentImage = snipData.FullImage!;
                cropRect = snipData.CropRect;

                InitializeUI();
            }

            private void InitializeUI()
            {
                var mainBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0A0A")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Margin = new Thickness(10)
                };

                var mainGrid = new Grid
                {
                    Margin = new Thickness(20)
                };
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Title
                var titleBar = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 16)
                };
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = "Edit Image",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(title, 0);
                titleBar.Children.Add(title);

                var closeBtn = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "\uE711",
                        FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                        FontSize = 16
                    },
                    Width = 32,
                    Height = 32,
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                closeBtn.Click += (s, e) => Close();
                Grid.SetColumn(closeBtn, 1);
                titleBar.Children.Add(closeBtn);

                Grid.SetRow(titleBar, 0);
                mainGrid.Children.Add(titleBar);

                // Image viewer with overlay
                var imageContainer = new Grid
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111")),
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var viewBox = new Viewbox
                {
                    Stretch = Stretch.Uniform
                };

                var imageGrid = new Grid();

                imageControl = new Image
                {
                    Source = currentImage,
                    Stretch = Stretch.None
                };
                imageGrid.Children.Add(imageControl);

                overlayCanvas = new Canvas
                {
                    Background = Brushes.Transparent
                };
                imageGrid.Children.Add(overlayCanvas);

                cropCanvas = new Canvas
                {
                    Background = Brushes.Transparent
                };
                cropCanvas.MouseLeftButtonDown += CropCanvas_MouseDown;
                cropCanvas.MouseMove += CropCanvas_MouseMove;
                cropCanvas.MouseLeftButtonUp += CropCanvas_MouseUp;
                imageGrid.Children.Add(cropCanvas);

                viewBox.Child = imageGrid;
                imageContainer.Children.Add(viewBox);

                imageContainer.SizeChanged += (s, e) => UpdateCropOverlay();

                Grid.SetRow(imageContainer, 1);
                mainGrid.Children.Add(imageContainer);

                // Buttons
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0)
                };

                var rotateBtn = CreateEditorButton("\uE7AD", "Rotate 90°", RotateImage);
                buttonPanel.Children.Add(rotateBtn);

                var flipHBtn = CreateEditorButton("\uE7C7", "Flip H", FlipHorizontal);
                flipHBtn.Margin = new Thickness(8, 0, 0, 0);
                buttonPanel.Children.Add(flipHBtn);

                var flipVBtn = CreateEditorButton("\uE7C8", "Flip V", FlipVertical);
                flipVBtn.Margin = new Thickness(8, 0, 0, 0);
                buttonPanel.Children.Add(flipVBtn);

                var doneBtn = CreateEditorButton("\uE73E", "Done", SaveImage);
                doneBtn.Margin = new Thickness(24, 0, 0, 0);
                doneBtn.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                    }
                };
                buttonPanel.Children.Add(doneBtn);

                Grid.SetRow(buttonPanel, 2);
                mainGrid.Children.Add(buttonPanel);

                mainBorder.Child = mainGrid;
                Content = mainBorder;

                Loaded += (s, e) => UpdateCropOverlay();
            }

            private Button CreateEditorButton(string icon, string text, Action onClick)
            {
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                var iconBlock = new TextBlock
                {
                    Text = icon,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                stackPanel.Children.Add(iconBlock);

                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                stackPanel.Children.Add(textBlock);

                var button = new Button
                {
                    Content = stackPanel,
                    Height = 36,
                    Padding = new Thickness(16, 8, 16, 8),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };

                button.Click += (s, e) => onClick();

                var template = new ControlTemplate(typeof(Button));
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
                border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                border.AppendChild(content);
                template.VisualTree = border;
                button.Template = template;

                return button;
            }

            private void UpdateCropOverlay()
            {
                if (imageControl.ActualWidth == 0 || imageControl.ActualHeight == 0) return;

                // Calculate scale
                double scaleX = imageControl.ActualWidth / currentImage.PixelWidth;
                double scaleY = imageControl.ActualHeight / currentImage.PixelHeight;
                currentScale = Math.Min(scaleX, scaleY);

                // Update overlay canvas
                overlayCanvas.Width = currentImage.PixelWidth * currentScale;
                overlayCanvas.Height = currentImage.PixelHeight * currentScale;
                overlayCanvas.Children.Clear();

                var clip = new CombinedGeometry
                {
                    GeometryCombineMode = GeometryCombineMode.Exclude,
                    Geometry1 = new RectangleGeometry(new Rect(0, 0, overlayCanvas.Width, overlayCanvas.Height)),
                    Geometry2 = new RectangleGeometry(new Rect(
                        cropRect.X * currentScale,
                        cropRect.Y * currentScale,
                        cropRect.Width * currentScale,
                        cropRect.Height * currentScale
                    ))
                };

                var overlay = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Data = clip
                };
                overlayCanvas.Children.Add(overlay);

                // Update crop canvas
                cropCanvas.Width = overlayCanvas.Width;
                cropCanvas.Height = overlayCanvas.Height;
                cropCanvas.Children.Clear();

                // Draw crop rectangle
                var cropBorder = new System.Windows.Shapes.Rectangle
                {
                    Width = cropRect.Width * currentScale,
                    Height = cropRect.Height * currentScale,
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(cropBorder, cropRect.X * currentScale);
                Canvas.SetTop(cropBorder, cropRect.Y * currentScale);
                cropCanvas.Children.Add(cropBorder);

                // Draw corner handles
                double handleSize = 12;
                var corners = new[]
                {
                    new { Name = "TopLeft", X = cropRect.X * currentScale - handleSize/2, Y = cropRect.Y * currentScale - handleSize/2 },
                    new { Name = "TopRight", X = (cropRect.X + cropRect.Width) * currentScale - handleSize/2, Y = cropRect.Y * currentScale - handleSize/2 },
                    new { Name = "BottomLeft", X = cropRect.X * currentScale - handleSize/2, Y = (cropRect.Y + cropRect.Height) * currentScale - handleSize/2 },
                    new { Name = "BottomRight", X = (cropRect.X + cropRect.Width) * currentScale - handleSize/2, Y = (cropRect.Y + cropRect.Height) * currentScale - handleSize/2 }
                };

                foreach (var corner in corners)
                {
                    var handle = new System.Windows.Shapes.Ellipse
                    {
                        Width = handleSize,
                        Height = handleSize,
                        Fill = Brushes.White,
                        Cursor = Cursors.SizeNWSE,
                        Tag = corner.Name
                    };
                    Canvas.SetLeft(handle, corner.X);
                    Canvas.SetTop(handle, corner.Y);
                    cropCanvas.Children.Add(handle);
                }

                // Draw edge handles
                var edges = new[]
                {
                    new { Name = "Top", X = (cropRect.X + cropRect.Width/2) * currentScale - handleSize/2, Y = cropRect.Y * currentScale - handleSize/2, Cursor = Cursors.SizeNS },
                    new { Name = "Bottom", X = (cropRect.X + cropRect.Width/2) * currentScale - handleSize/2, Y = (cropRect.Y + cropRect.Height) * currentScale - handleSize/2, Cursor = Cursors.SizeNS },
                    new { Name = "Left", X = cropRect.X * currentScale - handleSize/2, Y = (cropRect.Y + cropRect.Height/2) * currentScale - handleSize/2, Cursor = Cursors.SizeWE },
                    new { Name = "Right", X = (cropRect.X + cropRect.Width) * currentScale - handleSize/2, Y = (cropRect.Y + cropRect.Height/2) * currentScale - handleSize/2, Cursor = Cursors.SizeWE }
                };

                foreach (var edge in edges)
                {
                    var handle = new System.Windows.Shapes.Rectangle
                    {
                        Width = handleSize,
                        Height = handleSize,
                        Fill = Brushes.White,
                        Cursor = edge.Cursor,
                        Tag = edge.Name
                    };
                    Canvas.SetLeft(handle, edge.X);
                    Canvas.SetTop(handle, edge.Y);
                    cropCanvas.Children.Add(handle);
                }
            }

            private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
            {
                var pos = e.GetPosition(cropCanvas);

                // Check if clicking on a handle
                foreach (var child in cropCanvas.Children)
                {
                    if (child is FrameworkElement element && element.Tag is string tag)
                    {
                        double left = Canvas.GetLeft(element);
                        double top = Canvas.GetTop(element);
                        double right = left + element.Width;
                        double bottom = top + element.Height;

                        if (pos.X >= left && pos.X <= right && pos.Y >= top && pos.Y <= bottom)
                        {
                            isDragging = true;
                            dragHandle = tag;
                            dragStart = pos;
                            dragStartX = cropRect.X;
                            dragStartY = cropRect.Y;
                            dragStartWidth = cropRect.Width;
                            dragStartHeight = cropRect.Height;
                            cropCanvas.CaptureMouse();
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }

            private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
            {
                if (!isDragging) return;

                var pos = e.GetPosition(cropCanvas);
                double deltaX = (pos.X - dragStart.X) / currentScale;
                double deltaY = (pos.Y - dragStart.Y) / currentScale;

                int newX = dragStartX;
                int newY = dragStartY;
                int newWidth = dragStartWidth;
                int newHeight = dragStartHeight;

                switch (dragHandle)
                {
                    case "TopLeft":
                        newX = dragStartX + (int)deltaX;
                        newY = dragStartY + (int)deltaY;
                        newWidth = dragStartWidth - (int)deltaX;
                        newHeight = dragStartHeight - (int)deltaY;
                        break;
                    case "TopRight":
                        newY = dragStartY + (int)deltaY;
                        newWidth = dragStartWidth + (int)deltaX;
                        newHeight = dragStartHeight - (int)deltaY;
                        break;
                    case "BottomLeft":
                        newX = dragStartX + (int)deltaX;
                        newWidth = dragStartWidth - (int)deltaX;
                        newHeight = dragStartHeight + (int)deltaY;
                        break;
                    case "BottomRight":
                        newWidth = dragStartWidth + (int)deltaX;
                        newHeight = dragStartHeight + (int)deltaY;
                        break;
                    case "Top":
                        newY = dragStartY + (int)deltaY;
                        newHeight = dragStartHeight - (int)deltaY;
                        break;
                    case "Bottom":
                        newHeight = dragStartHeight + (int)deltaY;
                        break;
                    case "Left":
                        newX = dragStartX + (int)deltaX;
                        newWidth = dragStartWidth - (int)deltaX;
                        break;
                    case "Right":
                        newWidth = dragStartWidth + (int)deltaX;
                        break;
                }

                // Constrain to image bounds and minimum size
                if (newWidth >= 10 && newHeight >= 10 &&
                    newX >= 0 && newY >= 0 &&
                    newX + newWidth <= currentImage.PixelWidth &&
                    newY + newHeight <= currentImage.PixelHeight)
                {
                    cropRect = new Int32Rect(newX, newY, newWidth, newHeight);
                    UpdateCropOverlay();
                }
            }

            private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
            {
                if (isDragging)
                {
                    isDragging = false;
                    cropCanvas.ReleaseMouseCapture();
                }
            }

            private void RotateImage()
            {
                var rotated = new TransformedBitmap(currentImage, new RotateTransform(90));
                rotated.Freeze();

                // Keep crop dimensions but swap width/height and center it
                int newWidth = cropRect.Height;
                int newHeight = cropRect.Width;
                int newX = Math.Max(0, (rotated.PixelWidth - newWidth) / 2);
                int newY = Math.Max(0, (rotated.PixelHeight - newHeight) / 2);

                // Ensure it fits within bounds
                if (newX + newWidth > rotated.PixelWidth)
                    newWidth = rotated.PixelWidth - newX;
                if (newY + newHeight > rotated.PixelHeight)
                    newHeight = rotated.PixelHeight - newY;

                cropRect = new Int32Rect(newX, newY, newWidth, newHeight);

                currentImage = rotated;
                imageControl.Source = currentImage;
                UpdateCropOverlay();
            }

            private void FlipHorizontal()
            {
                var flipped = new TransformedBitmap(currentImage, new ScaleTransform(-1, 1));
                flipped.Freeze();

                // Mirror the crop horizontally
                int newX = currentImage.PixelWidth - cropRect.X - cropRect.Width;
                cropRect = new Int32Rect(newX, cropRect.Y, cropRect.Width, cropRect.Height);

                currentImage = flipped;
                imageControl.Source = currentImage;
                UpdateCropOverlay();
            }

            private void FlipVertical()
            {
                var flipped = new TransformedBitmap(currentImage, new ScaleTransform(1, -1));
                flipped.Freeze();

                // Mirror the crop vertically
                int newY = currentImage.PixelHeight - cropRect.Y - cropRect.Height;
                cropRect = new Int32Rect(cropRect.X, newY, cropRect.Width, cropRect.Height);

                currentImage = flipped;
                imageControl.Source = currentImage;
                UpdateCropOverlay();
            }

            private void SaveImage()
            {
                var croppedImage = new CroppedBitmap(currentImage, cropRect);
                croppedImage.Freeze();
                ImageSaved?.Invoke(croppedImage);
                Close();
            }
        }

        #endregion

        #region Groups Feature

        private void PopulateGroups()
        {
            GroupsItemsControl.Items.Clear();

            if (!clipboardData.Groups.Any())
            {
                // Show click-anywhere overlay with big + button
                if (GroupsCreateOverlay != null)
                    GroupsCreateOverlay.Visibility = Visibility.Visible;
                return;
            }

            if (GroupsCreateOverlay != null)
                GroupsCreateOverlay.Visibility = Visibility.Collapsed;

            foreach (var group in clipboardData.Groups)
            {
                GroupsItemsControl.Items.Add(CreateGroupCard(group));
            }
        }

        private Border CreateGroupCard(ClipboardGroup group)
        {
            var mainBorder = new Border
            {
                Background = (Brush)Resources["CardBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(0),
                AllowDrop = true,
                Tag = group
            };

            var stack = new StackPanel();

            // Header with expand/collapse
            var headerBorder = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(16, 12, 16, 12),
                Cursor = Cursors.Hand
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Expand/Collapse icon
            var expandIcon = new TextBlock
            {
                Text = group.IsExpanded ? "\uE70E" : "\uE70D",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = (Brush)Resources["TextSecondary"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(expandIcon, 0);
            headerGrid.Children.Add(expandIcon);

            // Group info stack
            var infoStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var iconBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Color)),
                Margin = new Thickness(0, 0, 12, 0)
            };
            var iconText = new TextBlock
            {
                Text = group.Icon,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = iconText;
            infoStack.Children.Add(iconBorder);

            var nameStack = new StackPanel();
            var nameText = new TextBlock
            {
                Text = group.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextPrimary"]
            };
            var countText = new TextBlock
            {
                Text = $"{group.ItemHashes.Count} items",
                FontSize = 11,
                Foreground = (Brush)Resources["TextSecondary"]
            };
            nameStack.Children.Add(nameText);
            nameStack.Children.Add(countText);
            infoStack.Children.Add(nameStack);

            Grid.SetColumn(infoStack, 1);
            headerGrid.Children.Add(infoStack);

            // Delete button
            var deleteBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = "\uE74D",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14
                },
                Width = 32,
                Height = 32,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (Brush)Resources["DangerRed"],
                Cursor = Cursors.Hand,
                Margin = new Thickness(8, 0, 0, 0)
            };
            deleteBtn.Click += (s, e) =>
            {
                e.Handled = true;
                clipboardData.Groups.Remove(group);
                // Remove group reference from items
                foreach (var item in clipboardData.Text.Concat(clipboardData.Images))
                {
                    if (item.GroupId == group.Id)
                        item.GroupId = null;
                }
                SaveData();
                PopulateGroups();
            };
            Grid.SetColumn(deleteBtn, 2);
            headerGrid.Children.Add(deleteBtn);

            headerBorder.Child = headerGrid;
            headerBorder.MouseLeftButtonDown += (s, e) =>
            {
                group.IsExpanded = !group.IsExpanded;
                expandIcon.Text = group.IsExpanded ? "\uE70E" : "\uE70D";
                PopulateGroups();
            };
            stack.Children.Add(headerBorder);

            // Drop zone indicator
            var dropZone = new Border
            {
                Height = 60,
                Background = new SolidColorBrush(Color.FromArgb(30, 168, 85, 247)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Color)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(16, 0, 16, 12),
                Visibility = Visibility.Collapsed
            };
            var dropText = new TextBlock
            {
                Text = "Drop here to add to group",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Color)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dropZone.Child = dropText;
            stack.Children.Add(dropZone);

            // Items container
            if (group.IsExpanded)
            {
                var itemsStack = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };

                var allItems = clipboardData.Text.Concat(clipboardData.Images).ToList();
                var groupItems = allItems.Where(x => group.ItemHashes.Contains(x.Hash)).ToList();

                foreach (var item in groupItems)
                {
                    var itemCard = item.Type == "text" ? CreateTextItemCard(item) : CreateImageItemCard(item);
                    itemsStack.Children.Add(itemCard);
                }

                if (!groupItems.Any())
                {
                    var emptyText = new TextBlock
                    {
                        Text = "Drag items here to add them",
                        FontSize = 12,
                        Foreground = (Brush)Resources["TextTertiary"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 12, 0, 12)
                    };
                    itemsStack.Children.Add(emptyText);
                }

                stack.Children.Add(itemsStack);
            }

            mainBorder.Child = stack;

            // Drag & Drop handlers
            mainBorder.DragEnter += (s, e) =>
            {
                if (draggedItem != null && !group.ItemHashes.Contains(draggedItem.Hash))
                {
                    dropZone.Visibility = Visibility.Visible;
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };

            mainBorder.DragLeave += (s, e) =>
            {
                dropZone.Visibility = Visibility.Collapsed;
                e.Handled = true;
            };

            mainBorder.DragOver += (s, e) =>
            {
                if (draggedItem != null && !group.ItemHashes.Contains(draggedItem.Hash))
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };

            mainBorder.Drop += (s, e) =>
            {
                dropZone.Visibility = Visibility.Collapsed;
                if (draggedItem != null && !group.ItemHashes.Contains(draggedItem.Hash))
                {
                    group.ItemHashes.Add(draggedItem.Hash);
                    draggedItem.GroupId = group.Id;
                    SaveData();
                    PopulateGroups();
                    ShowGroupNotification($"Added to {group.Name}");
                }
                e.Handled = true;
            };

            return mainBorder;
        }

        private void CreateGroupButton_Click(object sender, RoutedEventArgs e)
        {
            ShowModal(CreateGroupModal());
        }

        private void GroupsCreateOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Make the whole Groups area clickable (only visible when there are no groups)
            e.Handled = true;
            ShowModal(CreateGroupModal());
        }

        private Border CreateGroupModal()
        {
            var mainBorder = new Border
            {
                Background = (Brush)Resources["PrimaryBackground"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Width = 450,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();

            // Title
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "\uE710",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["TextPrimary"],
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Create New Group",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titleStack);

            // Name input
            var nameLabel = new TextBlock
            {
                Text = "Group Name",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(nameLabel);

            var nameInput = new TextBox
            {
                Text = "New Group",
                FontSize = 14,
                Padding = new Thickness(12, 10, 12, 10),
                Background = (Brush)Resources["CardBackground"],
                Foreground = (Brush)Resources["TextPrimary"],
                BorderBrush = (Brush)Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 20)
            };
            stack.Children.Add(nameInput);

            // Color picker
            var colorLabel = new TextBlock
            {
                Text = "Color",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(colorLabel);

            var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 20) };
            var colors = new[] { "#A855F7", "#EC4899", "#EF4444", "#F59E0B", "#10B981", "#06B6D4", "#3B82F6", "#6366F1" };
            Border? selectedColorBorder = null;
            string selectedColor = colors[0];

            foreach (var color in colors)
            {
                var colorBorder = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(3),
                    BorderBrush = color == selectedColor ? Brushes.White : Brushes.Transparent
                };

                if (color == selectedColor)
                    selectedColorBorder = colorBorder;

                colorBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (selectedColorBorder != null)
                        selectedColorBorder.BorderBrush = Brushes.Transparent;
                    selectedColorBorder = colorBorder;
                    colorBorder.BorderBrush = Brushes.White;
                    selectedColor = color;
                };

                colorPanel.Children.Add(colorBorder);
            }
            stack.Children.Add(colorPanel);

            // Icon picker
            var iconLabel = new TextBlock
            {
                Text = "Icon",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Resources["TextSecondary"],
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(iconLabel);

            var iconPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 20) };
            var icons = new[] { "\uE8B7", "\uE8F1", "\uE734", "\uE8B8", "\uE774", "\uE8A5", "\uE7EE", "\uE82D" };
            Border? selectedIconBorder = null;
            string selectedIcon = icons[0];

            foreach (var icon in icons)
            {
                var iconBorder = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(8),
                    Background = (Brush)Resources["CardBackground"],
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(2),
                    BorderBrush = icon == selectedIcon ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedColor)) : (Brush)Resources["BorderBrush"]
                };

                var iconText = new TextBlock
                {
                    Text = icon,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    Foreground = (Brush)Resources["TextPrimary"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = iconText;

                if (icon == selectedIcon)
                    selectedIconBorder = iconBorder;

                iconBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (selectedIconBorder != null)
                        selectedIconBorder.BorderBrush = (Brush)Resources["BorderBrush"];
                    selectedIconBorder = iconBorder;
                    iconBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedColor));
                    selectedIcon = icon;
                };

                iconPanel.Children.Add(iconBorder);
            }
            stack.Children.Add(iconPanel);

            // Buttons
            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(24, 10, 24, 10),
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)Resources["CardBackground"],
                Foreground = (Brush)Resources["TextPrimary"],
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => HideModal();

            var createBtn = new Button
            {
                Content = "Create Group",
                Padding = new Thickness(24, 10, 24, 10),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#A855F7"), 0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#EC4899"), 1)
                }
            };

            var createTemplate = new ControlTemplate(typeof(Button));
            var createBorder = new FrameworkElementFactory(typeof(Border));
            createBorder.SetValue(Border.BackgroundProperty, gradient);
            createBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var createContent = new FrameworkElementFactory(typeof(ContentPresenter));
            createContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            createContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            createBorder.AppendChild(createContent);
            createTemplate.VisualTree = createBorder;
            createBtn.Template = createTemplate;

            createBtn.Click += (s, e) =>
            {
                var newGroup = new ClipboardGroup
                {
                    Name = string.IsNullOrWhiteSpace(nameInput.Text) ? "New Group" : nameInput.Text,
                    Color = selectedColor,
                    Icon = selectedIcon
                };
                clipboardData.Groups.Add(newGroup);
                SaveData();
                PopulateGroups();
                HideModal();
                ShowGroupNotification($"Created group: {newGroup.Name}");
            };

            buttonStack.Children.Add(cancelBtn);
            buttonStack.Children.Add(createBtn);
            stack.Children.Add(buttonStack);

            mainBorder.Child = stack;
            return mainBorder;
        }

        private void ShowGroupNotification(string message)
        {
            var text = new TextBlock
            {
                Text = message,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            Canvas.SetLeft(text, (ActualWidth / 2) - 100);
            Canvas.SetBottom(text, 60);

            AnimationCanvas.Children.Add(text);

            var moveAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 60,
                To = 120,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };

            var fadeAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.8),
                BeginTime = TimeSpan.FromSeconds(0)
            };

            fadeAnimation.Completed += (s, e) =>
            {
                AnimationCanvas.Children.Remove(text);
            };

            text.BeginAnimation(Canvas.BottomProperty, moveAnimation);
            text.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        #endregion
    }
}
