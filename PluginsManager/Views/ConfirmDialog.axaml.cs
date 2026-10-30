using Avalonia.Controls;

namespace PluginsManager.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("Confirm", "", "OK", false)
    {
    }

    public ConfirmDialog(string title, string message, string confirmText, bool danger)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (danger)
        {
            ConfirmButton.Classes.Remove("accent");
            ConfirmButton.Classes.Add("danger");
            ConfirmButton.Background = (Avalonia.Media.IBrush?)this.FindResource("DangerBrush");
            ConfirmButton.Foreground = Avalonia.Media.Brushes.White;
            ConfirmButton.IsDefault = false; // destructive actions should not trigger on Enter
        }

        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
