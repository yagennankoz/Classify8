using System.Windows;

namespace Classify8.Views
{
    public enum DeleteAction { Convert, Force, Cancel }

    public partial class PresetDeleteConfirmWindow : Window
    {
        public DeleteAction ResultAction { get; private set; } = DeleteAction.Cancel;

        public PresetDeleteConfirmWindow()
        {
            InitializeComponent();
        }

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = DeleteAction.Convert;
            DialogResult = true;
        }

        private void BtnForce_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = DeleteAction.Force;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ResultAction = DeleteAction.Cancel;
            DialogResult = false;
        }
    }
}