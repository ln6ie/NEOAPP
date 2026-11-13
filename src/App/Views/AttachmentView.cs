using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NEO.Core;

namespace NEO.Views;

/// <summary>Bounded, cached thumbnails; decoded image bytes never remain stream-backed.</summary>
public sealed class AttachmentView : Border
{
    private static readonly ConditionalWeakTable<ChatAttachment, ImageSource> Thumbnails = new();
    public static readonly DependencyProperty AttachmentProperty = DependencyProperty.Register(
        nameof(Attachment), typeof(ChatAttachment), typeof(AttachmentView), new PropertyMetadata(null, Changed));
    public ChatAttachment? Attachment { get => (ChatAttachment?)GetValue(AttachmentProperty); set => SetValue(AttachmentProperty, value); }
    public AttachmentView() { Padding = new Thickness(4); CornerRadius = new CornerRadius(10); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((AttachmentView)sender).Render();
    private void Render()
    {
        Child = null;
        if (Attachment is not { } item) return;
        ToolTip = item.Name;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(0, 0, 6, 0) };
        if (item.ImageBytes is { Length: > 0 })
        {
            try
            {
                preview.Child = new Image { Source = Thumbnails.GetValue(item, Decode), Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
            { preview.Child = FileGlyph(); }
        }
        else preview.Child = FileGlyph();
        row.Children.Add(preview);
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 96 };
        label.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(item.Name), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, MaxWidth = 96 });
        label.Children.Add(new TextBlock { Text = Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant(), FontSize = 10, Opacity = 0.65, FlowDirection = FlowDirection.LeftToRight });
        row.Children.Add(label);
        Child = row;
    }
    private static TextBlock FileGlyph() => new() { Text = "\uE8A5", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private static ImageSource Decode(ChatAttachment item)
    {
        using var stream = new MemoryStream(item.ImageBytes!, writable: false);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 80; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
}
