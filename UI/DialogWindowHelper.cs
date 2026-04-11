using System.Windows;
using System;

namespace SubnauticaLauncher.UI
{
    internal static class DialogWindowHelper
    {
        public static bool? ShowDialog(Window owner, Window dialog)
        {
            Window modalOwner = owner is MainWindow ? owner : owner.Owner ?? owner;
            dialog.Owner = modalOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            PositionDialogOver(owner, dialog);

            if (owner is MainWindow)
                return dialog.ShowDialog();

            bool ownerWasVisible = owner.Visibility == Visibility.Visible;
            void HideOwnerOnRender(object? sender, EventArgs args)
            {
                dialog.ContentRendered -= HideOwnerOnRender;
                if (ownerWasVisible && owner.Visibility == Visibility.Visible)
                    owner.Visibility = Visibility.Hidden;
            }

            dialog.ContentRendered += HideOwnerOnRender;
            try
            {
                return dialog.ShowDialog();
            }
            finally
            {
                dialog.ContentRendered -= HideOwnerOnRender;
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

        private static void PositionDialogOver(Window anchor, Window dialog)
        {
            double dialogWidth = GetWindowDimension(dialog.Width, dialog.MinWidth, dialog.ActualWidth, fallback: 520);
            double dialogHeight = GetWindowDimension(dialog.Height, dialog.MinHeight, dialog.ActualHeight, fallback: 360);

            double anchorWidth = GetWindowDimension(anchor.Width, anchor.MinWidth, anchor.ActualWidth, fallback: 800);
            double anchorHeight = GetWindowDimension(anchor.Height, anchor.MinHeight, anchor.ActualHeight, fallback: 600);

            double left = anchor.Left + Math.Max(0, (anchorWidth - dialogWidth) / 2);
            double top = anchor.Top + Math.Max(0, (anchorHeight - dialogHeight) / 2);

            dialog.Left = left;
            dialog.Top = top;
        }

        private static double GetWindowDimension(double requested, double min, double actual, double fallback)
        {
            if (!double.IsNaN(actual) && actual > 1)
                return actual;

            if (!double.IsNaN(requested) && requested > 1)
                return requested;

            if (!double.IsNaN(min) && min > 1)
                return min;

            return fallback;
        }
    }
}
