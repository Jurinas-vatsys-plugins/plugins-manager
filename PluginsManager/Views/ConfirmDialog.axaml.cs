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
        ConfirmButton.Width = double.NaN; // fit longer labels such as "I understand, install"
        ConfirmButton.MinWidth = 90;
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

    /// <summary>Turns the dialog into a message box with a single OK button.</summary>
    public ConfirmDialog AsAlert()
    {
        CancelButton.IsVisible = false;
        return this;
    }
}
