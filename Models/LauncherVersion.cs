using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SubnauticaLauncher.Models;

public sealed class LauncherVersion : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status = "Idle";
    public string DisplayName { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string GameFolder { get; set; } = string.Empty;
    public string OriginalVersionId { get; set; } = string.Empty;
    public string OriginalVersionDisplayName { get; set; } = string.Empty;
    public string LaunchOptions { get; set; } = string.Empty;
    public string IconName { get; set; } = "SNLIcon.png";
    public string IconPath => $"pack://application:,,,/Assets/Icons/{(string.IsNullOrWhiteSpace(IconName) ? "SNLIcon.png" : IconName)}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
