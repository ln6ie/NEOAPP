using System;
using System.Windows;
using System.Windows.Media;

namespace NEO.Views;

// Vector-rendered voice surface: no video, bitmap dependency, or idle render loop.
public sealed class VoiceOrb : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(VoiceOrb), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSpeakingProperty = DependencyProperty.Register(nameof(IsSpeaking), typeof(bool), typeof(VoiceOrb), new PropertyMetadata(false, MotionChanged));
    public static readonly DependencyProperty IsProcessingProperty = DependencyProperty.Register(nameof(IsProcessing), typeof(bool), typeof(VoiceOrb), new PropertyMetadata(false, MotionChanged));
    private readonly LinearGradientBrush blue = new(Color.FromRgb(0, 48, 202), Color.FromRgb(28, 91, 239), 115);
    private readonly RadialGradientBrush orange = new()
    {
        Center = new Point(0.85, 0.15), GradientOrigin = new Point(0.85, 0.15), RadiusX = 0.8, RadiusY = 0.75,
        GradientStops = new GradientStopCollection { new(Color.FromRgb(255, 139, 24), 0), new(Color.FromArgb(225, 255, 165, 66), 0.35), new(Colors.Transparent, 1) }
    };
    private readonly RadialGradientBrush cloud = new()
    {
        Center = new Point(0.4, 0.65), GradientOrigin = new Point(0.4, 0.65), RadiusX = 0.85, RadiusY = 0.65,
        GradientStops = new GradientStopCollection
        {
            new(Colors.White, 0), new(Color.FromArgb(235, 245, 247, 255), 0.3),
            new(Color.FromArgb(100, 200, 216, 255), 0.65), new(Colors.Transparent, 1)
        }
    };
    private bool rendering;
    private long lastFrame;
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsSpeaking { get => (bool)GetValue(IsSpeakingProperty); set => SetValue(IsSpeakingProperty, value); }
    public bool IsProcessing { get => (bool)GetValue(IsProcessingProperty); set => SetValue(IsProcessingProperty, value); }

    public VoiceOrb()
    {
        blue.Freeze();
        Loaded += (_, _) => UpdateMotion();
        IsVisibleChanged += (_, _) => UpdateMotion();
        Unloaded += (_, _) => StopMotion();
    }
    private static void MotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((VoiceOrb)d).UpdateMotion();
    private void UpdateMotion()
    {
        if (IsLoaded && IsVisible && (IsSpeaking || IsProcessing) && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            if (!rendering) { CompositionTarget.Rendering += Frame; rendering = true; }
        }
        else StopMotion();
        InvalidateVisual();
    }
    private void StopMotion()
    {
        if (rendering) CompositionTarget.Rendering -= Frame;
        rendering = false;
    }
    private void Frame(object? sender, EventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || !IsVisible) { StopMotion(); return; }
        if (Environment.TickCount64 - lastFrame < 33) return;
        lastFrame = Environment.TickCount64;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 3;
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (SystemParameters.HighContrast)
        {
            context.DrawEllipse(SystemColors.HighlightBrush, null, center, radius, radius);
            return;
        }
        var moving = rendering && SystemParameters.ClientAreaAnimation;
        var phase = moving ? Environment.TickCount64 / 850d : 0;
        var level = Math.Clamp(Level, 0, 1);
        if (moving) radius *= 0.96 + 0.025 * Math.Sin(phase) + level * 0.015;
        cloud.Center = new Point(0.4 + (moving ? Math.Sin(phase * 0.7) * 0.14 : 0), 0.65 - level * 0.18);
        cloud.GradientOrigin = cloud.Center;
        cloud.RadiusY = 0.58 + level * 0.26;
        context.DrawEllipse(blue, null, center, radius, radius);
        context.DrawEllipse(cloud, null, center, radius, radius);
        orange.Center = new Point(0.85 - (moving ? Math.Sin(phase * 0.5) * 0.12 : 0), 0.15 + level * 0.15);
        orange.GradientOrigin = orange.Center;
        context.DrawEllipse(orange, null, center, radius, radius);
    }
}
