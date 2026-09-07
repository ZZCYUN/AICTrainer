using System.Windows;
using System.Windows.Input;

namespace AICTrainer.Views
{
    public partial class HitboxConfigDialog : Window
    {
        public HitboxConfigDialog()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
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
