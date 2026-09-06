using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AICTrainer.Services;

namespace AICTrainer.Views
{
    public partial class DisclaimerWindow : Window
    {
        public DisclaimerWindow()
        {
            InitializeComponent();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnDoNotShowAgainClick(object sender, RoutedEventArgs e)
        {
            var settings = TrainerSettings.Load();
            settings.DoNotShowDisclaimer = true;
            settings.Save();
            Close();
        }

        private void OnPixivLinkClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.pixiv.net/artworks/149248561",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
