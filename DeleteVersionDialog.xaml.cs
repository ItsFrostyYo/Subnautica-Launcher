using System.Windows;

namespace SubnauticaLauncher
{
    public enum DeleteChoice
    {
        Cancel,
        RemoveFromLauncher,
        DeleteGame
    }

    public partial class DeleteVersionDialog : Window
    {
        public DeleteChoice Choice { get; private set; } = DeleteChoice.Cancel;

        public DeleteVersionDialog()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Choice = DeleteChoice.Cancel;
            Close();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            Choice = DeleteChoice.RemoveFromLauncher;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            Choice = DeleteChoice.DeleteGame;
            Close();
        }
    }
}