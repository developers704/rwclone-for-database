using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VallianiDataSync.App.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        LoginCard.BeginAnimation(OpacityProperty, fade);
        if (LoginCard.RenderTransform is System.Windows.Media.TranslateTransform tt)
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
    }
}
