using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PodPilot.App.ViewModels;

/// <summary>Kleine basisklasse voor INotifyPropertyChanged. Zo is er geen MVVM-pakket nodig.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Zet een veld en meldt de wijziging. Geeft false als de waarde gelijk bleef.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
