﻿using NHotkey;
using NHotkey.Wpf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfAnimatedGif;
using Clipboard = System.Windows.Clipboard;
using DpiChangedEventArgs = System.Windows.DpiChangedEventArgs;

namespace RitsukageGif
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public SelectedRegionResult Region { get; private set; }

        private int _delay;

        private int _fps;
        public int FPS
        {
            get => _fps;
            private set
            {
                _fps = value;
                _delay = 1000 / _fps;
            }
        }

        public int Scale { get; private set; }

        public bool RecordCursor { get; private set; }

        public bool RecordInMemory { get; private set; }

        public bool Recording { get; private set; }

        private bool _canBeginRecord;
        private bool _canChangeRegion;
        private byte[] _lastRecordGifClipboardBytes;
        private CancellationTokenSource _recordingCancellationTokenSource;
        private CancellationTokenSource _processingCancellationTokenSource;
        private Gif.RecordInfo _recordInfo;
        private string _currentGifPath;

        private readonly List<string> _tempFileList = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            Title += $" ver {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
        }

        private void SetDefaultConfig()
        {
            GifFrameInteger.Value = FPS = 20;
            GifScaleInteger.Value = Scale = 2;
            RecordCursorCheckBox.IsChecked = RecordCursor = Settings.Default.RecordCursor;
            MemoryRecordCheckBox.IsChecked = RecordInMemory = Settings.Default.MemoryRecord;
        }

        private void SaveConfig()
        {
            Settings.Default.RecordCursor = RecordCursor;
            Settings.Default.MemoryRecord = RecordInMemory;
            Settings.Default.Save();
        }

        private void RegisterHotKeys()
        {
            bool success1 = true, success2 = true;
            try
            {
                HotkeyManager.Current.AddOrReplace("PushRecordGif", Key.A, ModifierKeys.Control | ModifierKeys.Shift,
                    OnHotKey_PushRecordGif);
            }
            catch
            {
                success1 = false;
            }
            try
            {
                HotkeyManager.Current.AddOrReplace("SelectRegion", Key.S, ModifierKeys.Control | ModifierKeys.Shift,
                    OnHotKey_SelectRegion);
            }
            catch
            {
                success2 = false;
            }
            if (success1 && success2) return;
            var sb = new StringBuilder();
            sb.AppendLine("以下快捷键注册失败，请检查是否有其他程序占用了快捷键。");
            if (!success1)
                sb.AppendLine("Ctrl + Shift + A：开始/停止录制");
            if (!success2)
                sb.AppendLine("Ctrl + Shift + S：选择录制区域");
            Task.Run(() =>
            {
                MessageBox.Show(sb.ToString(),
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }).ConfigureAwait(false);
        }

        private void RemoveHotKeys()
        {
            HotkeyManager.Current.Remove("PushRecordGif");
            HotkeyManager.Current.Remove("SelectRegion");
        }

        private void OnHotKey_PushRecordGif(object sender, HotkeyEventArgs e)
        {
            if (!_canBeginRecord) return;
            RecordButton_Click(null, null);
        }

        private void OnHotKey_SelectRegion(object sender, HotkeyEventArgs e)
        {
            if (!_canChangeRegion) return;
            RegionSelectButton_Click(null, null);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetDefaultConfig();
            RegisterHotKeys();
            _canBeginRecord = false;
            _canChangeRegion = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveConfig();
            AboutWindow.CloseInstance();
            RemoveHotKeys();
            _recordingCancellationTokenSource?.Cancel();
            _processingCancellationTokenSource?.Cancel();
            _canBeginRecord = false;
            _canChangeRegion = false;
            foreach (var file in _tempFileList.Where(File.Exists).ToArray())
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_canBeginRecord) return;
            if (Recording)
            {
                _canChangeRegion = true;
                Recording = false;
                RecordButton.Content = "开始录制";
                RecordButton.Background = Brushes.White;
                RegionSelectButton.IsEnabled = true;
                GifScaleInteger.IsEnabled = true;
                GifFrameInteger.IsEnabled = true;
                RecordCursorCheckBox.IsEnabled = true;
                MemoryRecordCheckBox.IsEnabled = true;
                _recordingCancellationTokenSource?.Cancel();
            }
            else
            {
                _canChangeRegion = false;
                Recording = true;
                RecordButton.Content = "停止录制";
                RecordButton.Background = Brushes.Red;
                RegionSelectButton.IsEnabled = false;
                GifScaleInteger.IsEnabled = false;
                GifFrameInteger.IsEnabled = false;
                RecordCursorCheckBox.IsEnabled = false;
                MemoryRecordCheckBox.IsEnabled = false;
                _recordingCancellationTokenSource?.Cancel();
                _processingCancellationTokenSource?.Cancel();
                var tokenRecording = new CancellationTokenSource();
                var tokenProcessing = new CancellationTokenSource();
                _recordingCancellationTokenSource = tokenRecording;
                _processingCancellationTokenSource = tokenProcessing;
                var path = GenerateTempFileName(".gif");
                _tempFileList.Add(path);
                var info = RecordInMemory
                    ? Gif.BeginWithMemory(path, Region.Converted, _delay, (double)1 / Scale, RecordCursor, tokenRecording.Token, tokenProcessing.Token)
                    : Gif.BeginWithoutMemory(path, Region.Converted, _delay, (double)1 / Scale, RecordCursor, tokenRecording.Token, tokenProcessing.Token);
                _recordInfo = info;
                await Task.Run(() =>
                {
                    do
                    {
                        Thread.Sleep(30);
                        Dispatcher.Invoke(() =>
                        {
                            GifFramesLabel.Content = $"{info.ProcessedFrames} / {info.Frames}";
                        });
                    } while (!(info.Completed || tokenProcessing.IsCancellationRequested));
                    if (tokenProcessing.IsCancellationRequested) return;
                    var file = new FileInfo(path);
                    Dispatcher.Invoke(() =>
                    {
                        if (file.Length > 6 * 1024 * 1024)
                        {
                            GifSizeLabel.Foreground = Brushes.DarkRed;
                            GifSizeLabel.FontWeight = FontWeights.Bold;
                        }
                        else if (file.Length > 3 * 1024 * 1024)
                        {
                            GifSizeLabel.Foreground = Brushes.Red;
                            GifSizeLabel.FontWeight = FontWeights.Normal;
                        }
                        else
                        {
                            GifSizeLabel.Foreground = Brushes.Black;
                            GifSizeLabel.FontWeight = FontWeights.Normal;
                        }
                        if (file.Length > 1024 * 1024)
                            GifSizeLabel.Content = $"{(double)file.Length / 1024 / 1024:F2}MB";
                        else if (file.Length > 1024)
                            GifSizeLabel.Content = $"{(double)file.Length / 1024:F2}KB";
                        else
                            GifSizeLabel.Content = $"{(double)file.Length:F2}B";
                    });
                    var sb = new StringBuilder();
                    sb.Append("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">");
                    sb.AppendFormat(
                        "<html><body><!--StartFragment--><p><img src=\"file:///{0}\"></p><!--EndFragment--></body></html>",
                        path);
                    _lastRecordGifClipboardBytes = Encoding.Default.GetBytes(sb.ToString());
                    Dispatcher.Invoke(() =>
                    {
                        using (var data = new MemoryStream(_lastRecordGifClipboardBytes))
                        {
                            Clipboard.SetData("Html Format", data);
                        }
                    });
                    Dispatcher.Invoke(() =>
                    {
                        GifView.Visibility = Visibility.Visible;
                        var image = new BitmapImage();
                        image.BeginInit();
                        var ms = new MemoryStream(File.ReadAllBytes(path));
                        image.StreamSource = ms;
                        image.EndInit();
                        _currentGifPath = path;
                        ImageBehavior.SetAnimatedSource(GifView, image);
                    });
                }, tokenProcessing.Token).ConfigureAwait(false);
            }
        }

        private async void RegionSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_canChangeRegion) return;
            _canBeginRecord = false;
            _canChangeRegion = false;
            var regionSelect = RegionSelect.Begin();
            (var confirm, var region) = await regionSelect.WaitForResult().ConfigureAwait(false);
            _canChangeRegion = true;
            if (confirm)
            {
                if (region != default)
                {
                    Region = region;
                }
                else
                {
                    Region = null;
                }
            }
            Dispatcher.Invoke(() =>
            {
                if (Region != default)
                {
                    _canBeginRecord = true;
                    RecordButton.IsEnabled = true;
                }
                else
                {
                    RecordButton.IsEnabled = false;
                }
            });
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow.Begin();
        }

        private void GifScaleInteger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Scale = (int)e.NewValue;
        }

        private void GifFrameInteger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            FPS = (int)e.NewValue;
        }

        private void RecordCursorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecordCursor = true;
        }

        private void RecordCursorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecordCursor = false;
        }

        private void MemoryRecordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecordInMemory = true;
        }

        private void MemoryRecordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecordInMemory = false;
        }

        private void GifView_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var data = new MemoryStream(_lastRecordGifClipboardBytes);
            Clipboard.SetData("Html Format", data);
        }

        private void GifView_OnPreviewMouseLeftButtonDownPreviewMouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentGifPath)) return;
            if (!(sender is Image gif)) return;
            var dataObject = new DataObject(DataFormats.FileDrop, new[] { _currentGifPath });
            var dde = DragDrop.DoDragDrop(gif, dataObject, DragDropEffects.Copy);
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            ScreenInfo.ClearCache();
        }

        private static string GenerateTempFileName(string ext)
        {
            return Path.Combine(Path.GetTempPath(),
                    string.Join(string.Empty, Guid.NewGuid().ToByteArray().Select(x => x.ToString("X2"))) + ext)
                .Replace('\\', '/');
        }
    }
}
