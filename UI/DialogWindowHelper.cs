using System.Windows;
using System;

namespace SubnauticaLauncher.UI
{
    internal static class DialogWindowHelper
    {
        public static bool? ShowDialog(Window owner, Window dialog)
        {
            if (owner is MainWindow)
            {
                dialog.Owner = owner;
                return dialog.ShowDialog();
            }

            dialog.Owner = owner.Owner ?? owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            bool ownerWasVisible = owner.Visibility == Visibility.Visible;

            try
            {
                if (ownerWasVisible)
                    owner.Visibility = Visibility.Hidden;

                return dialog.ShowDialog();
            }
            finally
            {
                if (ownerWasVisible)
                {
                    owner.Visibility = Visibility.Visible;
                    owner.Activate();
                }
            }
        }

        public static void Finish(Window window, bool? dialogResult = null)
        {
            if (dialogResult.HasValue)
            {
                try
                {
                    window.DialogResult = dialogResult.Value;
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            window.Close();
        }
    }
}
