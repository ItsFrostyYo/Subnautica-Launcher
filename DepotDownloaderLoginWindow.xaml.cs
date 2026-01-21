using System.Windows;

namespace SubnauticaLauncher
{
    public partial class DepotDownloaderLoginWindow : Window
    {
        public string Username => UsernameBox.Text;
        public string Password => PasswordBox.Password;

        public DepotDownloaderLoginWindow()
        {
            InitializeComponent();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                StatusText.Text = "Username and password required.";
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}