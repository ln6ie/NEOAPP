using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NEO.Core;
using NEO.Infrastructure;
using NEO.ViewModels;

namespace NEO.Views;

public partial class ConversationWindow : Window
{
    private AssistantViewModel? observed;
    private bool exiting;
    public event Action? SettingsRequested;
    public event Action? VoiceRequested;
    public ConversationWindow()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(MessageInput, Paste);
        DataContextChanged += (_, _) =>
        {
            if (observed != null) { observed.Conversation.CollectionChanged -= ConversationChanged; observed.PropertyChanged -= Changed; }
            observed = DataContext as AssistantViewModel;
            if (observed != null) { observed.Conversation.CollectionChanged += ConversationChanged; observed.PropertyChanged += Changed; }
        };
    }
    public void Reveal()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); MessageInput.Focus();
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            Surface.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180)));
        }
    }
    private void ConversationChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshConversation();
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(AssistantViewModel.LiveResponse)) RefreshConversation(); }
    private void RefreshConversation()
    {
        EmptyHint.Visibility = observed?.Conversation.Count > 0 || !string.IsNullOrEmpty(observed?.LiveResponse) ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => ConversationScroll.ScrollToEnd()));
    }
    private void Dismiss(object sender, RoutedEventArgs e) => Hide();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    public void CloseForExit() { exiting = true; Close(); }
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        { DragMove(); e.Handled = true; }
    }
    private void OpenSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    public void ShowSettings(SettingsView view)
    {
        if (SettingsContent.Content != null) return;
        SettingsContent.Content = view;
        view.Completed += ReturnToChat;
        ChatArea.Visibility = ComposerArea.Visibility = Visibility.Collapsed;
        SettingsArea.Visibility = Visibility.Visible;
    }
    private void BackToChat(object sender, RoutedEventArgs e) => ReturnToChat();
    private void ReturnToChat()
    {
        if (SettingsContent.Content is SettingsView view) view.Completed -= ReturnToChat;
        SettingsContent.Content = null;
        SettingsArea.Visibility = Visibility.Collapsed;
        ChatArea.Visibility = ComposerArea.Visibility = Visibility.Visible;
        MessageInput.Focus();
    }
    private void ToggleVoice(object sender, RoutedEventArgs e) => VoiceRequested?.Invoke();
    private async void Send(object sender, RoutedEventArgs e) { if (observed != null) await observed.SendAsync(); }
    private void HandleKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { if (SettingsArea.IsVisible) ReturnToChat(); else Hide(); e.Handled = true; } }
    private async void HandleInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        { e.Handled = true; if (observed != null) await observed.SendAsync(); }
    }
    private async void AttachFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Multiselect = true, Title = "إرفاق ملفات أو صور", Filter = "الملفات المدعومة|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.pdf;*.docx;*.txt;*.md;*.csv;*.json;*.xml;*.html;*.css;*.js;*.ts;*.cs;*.py;*.yaml;*.yml;*.log;*.sql|كل الملفات|*.*" };
        if (picker.ShowDialog(this) == true && observed != null) await observed.AddFilesAsync(picker.FileNames);
    }
    private void RemoveAttachment(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatAttachment attachment }) observed?.RemoveAttachment(attachment); }
    private async void Paste(object sender, DataObjectPastingEventArgs e)
    {
        if (observed == null) return;
        try
        {
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.DataObject.GetData(DataFormats.FileDrop) as string[];
                e.CancelCommand();
                if (paths != null) await observed.AddFilesAsync(paths);
            }
            else if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                var bitmap = e.DataObject.GetData(DataFormats.Bitmap) as BitmapSource ?? Clipboard.GetImage();
                e.CancelCommand();
                if (bitmap == null) return;
                bitmap = bitmap.Clone(); bitmap.Freeze();
                var bytes = await Task.Run(() =>
                {
                    var scale = Math.Min(1, 1536d / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                    BitmapSource source = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
                    using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
                });
                if (bytes.Length > AttachmentReader.MaxBytes) throw new InvalidOperationException("الصورة كبيرة جداً.");
                observed.AddAttachment(new ChatAttachment("صورة ملصقة.png", "image/png", bytes));
            }
        }
        catch (Exception ex) { NEOLog.Error("clipboard.failed", ex); MessageBox.Show(this, "تعذّر إرفاق المحتوى. جرّب اختيار الملف مباشرة.", "المرفقات", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void DragFiles(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy; e.Handled = true;
    }
    private async void DropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || observed == null) return;
        e.Handled = true; await observed.AddFilesAsync(paths);
    }
}
