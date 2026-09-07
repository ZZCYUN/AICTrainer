using System;
using System.Windows;
using AICTrainer.Services;
using AICTrainer.ViewModels;

namespace AICTrainer.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = (MainViewModel)DataContext;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _vm.NotifyAllProperties();
            var settings = TrainerSettings.Load();
            if (!settings.DoNotShowDisclaimer)
            {
                var dlg = new DisclaimerWindow { Owner = this };
                dlg.ShowDialog();
            }
        }

        private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnOpenGitHubRepo(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/ZZCYUN/AICTrainer",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnOpenGitHubRepoMouse(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OnOpenGitHubRepo(sender, e);
        }

        private async void OnInjectClick(object sender, RoutedEventArgs e)
        {
            await _vm.InjectAndConnectAsync();
        }

        private void OnQuickFullHeal(object sender, RoutedEventArgs e)
        {
            _vm.QuickHealCommand.Execute(null);
        }

        private void OnQuickCureCracks(object sender, RoutedEventArgs e)
        {
            _vm.QuickCureCracksCommand.Execute(null);
        }

        private void OnQuickCureSer(object sender, RoutedEventArgs e)
        {
            _vm.QuickCureSerCommand.Execute(null);
        }

        private void OnQuickClearSatiety(object sender, RoutedEventArgs e)
        {
            _vm.QuickClearSatietyCommand.Execute(null);
        }

        private void OnQuickClearEp(object sender, RoutedEventArgs e)
        {
            _vm.QuickClearEpCommand.Execute(null);
        }

        private void OnCategorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            MainCardsScrollViewer?.ScrollToTop();
        }

        private void OnHitboxConfigClick(object sender, RoutedEventArgs e)
        {
            OpenHitboxConfigDialog();
        }

        private void OnHitboxCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var parent = dep;
                while (parent != null && parent != sender)
                {
                    if (parent is System.Windows.Controls.CheckBox || parent is System.Windows.Controls.Button)
                    {
                        return;
                    }
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                }
            }
            OpenHitboxConfigDialog();
        }

        private void OpenHitboxConfigDialog()
        {
            var dlg = new HitboxConfigDialog
            {
                Owner = this,
                DataContext = _vm
            };
            dlg.ShowDialog();
        }

        private void OnReelConfigClick(object sender, RoutedEventArgs e)
        {
            OpenReelConfigDialog();
        }

        private void OnReelCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var parent = dep;
                while (parent != null && parent != sender)
                {
                    if (parent is System.Windows.Controls.CheckBox || parent is System.Windows.Controls.Button)
                    {
                        return;
                    }
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                }
            }
            OpenReelConfigDialog();
        }

        private void OpenReelConfigDialog()
        {
            var dlg = new ReelConfigDialog
            {
                Owner = this,
                DataContext = _vm
            };
            dlg.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_vm.IsSaveConfigEnabled)
            {
                _vm.SaveConfigSettings();
            }
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}
