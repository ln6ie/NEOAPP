using System.Windows;
using System.Windows.Controls;
using NEO.Views;

namespace NEO.Tests;

public sealed class ComposerLayoutTests
{
    [Fact]
    public void ComposerIsSlimExpandsForLinesAndCentersSendButton()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new ConversationWindow();
                var root = (FrameworkElement)window.Content;
                var input = (TextBox)window.FindName("MessageInput");
                var composer = (Border)window.FindName("Composer");
                var send = (Button)window.FindName("SendButton");
                void Layout() { root.Measure(new Size(660, 560)); root.Arrange(new Rect(0, 0, 660, 560)); root.UpdateLayout(); }
                input.Text = "مرحبا";
                Layout();
                var logo = (FrameworkElement)window.FindName("HeaderLogo");
                var actions = (FrameworkElement)window.FindName("HeaderActions");
                var buttons = (StackPanel)((Border)actions).Child;
                Assert.Equal("\uE711", ((Button)buttons.Children[0]).Content);
                Assert.Equal("\uE921", ((Button)buttons.Children[1]).Content);
                var scroll = (ScrollViewer)window.FindName("ConversationScroll");
                Assert.True(logo.TranslatePoint(new Point(16, 16), root).X > actions.TranslatePoint(new Point(actions.ActualWidth / 2, 0), root).X);
                Assert.Equal(FlowDirection.LeftToRight, scroll.FlowDirection);
                Assert.Equal(FlowDirection.RightToLeft, ((FrameworkElement)scroll.Content).FlowDirection);
                Assert.True(scroll.TranslatePoint(new Point(), root).Y < actions.TranslatePoint(new Point(), root).Y);
                var singleLine = composer.ActualHeight;
                Assert.InRange(singleLine, 42, 50);
                input.Text = "السطر الأول\nالسطر الثاني\nالسطر الثالث";
                Layout();
                Assert.True(composer.ActualHeight > singleLine);
                var textCenter = input.TranslatePoint(new Point(0, input.ActualHeight / 2), composer).Y;
                var sendCenter = send.TranslatePoint(new Point(0, send.ActualHeight / 2), composer).Y;
                Assert.InRange(Math.Abs(textCenter - sendCenter), 0, 1);
                var content = (StackPanel)scroll.Content;
                content.Children.Add(new Border { Height = 2000 });
                Layout();
                var rail = (FrameworkElement)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
                var viewport = (FrameworkElement)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
                Assert.Equal(Visibility.Visible, rail.Visibility);
                var railLeft = rail.TranslatePoint(new Point(), root).X;
                Assert.True(viewport.TranslatePoint(new Point(viewport.ActualWidth, 0), root).X <= railLeft + 1);
                Assert.True(root.ActualWidth - rail.TranslatePoint(new Point(rail.ActualWidth, 0), root).X <= 2);
                scroll.ScrollToEnd(); Layout();
                Assert.True(scroll.VerticalOffset > 0);
                window.CloseForExit();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
