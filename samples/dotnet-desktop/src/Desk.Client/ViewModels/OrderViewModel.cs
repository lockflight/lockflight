using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Desk.Client.ViewModels;

/// <summary>An order line as the window shows it. Plain C#: its tests run on any thread, without WPF.</summary>
public sealed class OrderViewModel : INotifyPropertyChanged
{
    private int quantity;
    private decimal unitPrice;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Quantity
    {
        get => quantity;
        set => Set(ref quantity, value, nameof(Total));
    }

    public decimal UnitPrice
    {
        get => unitPrice;
        set => Set(ref unitPrice, value, nameof(Total));
    }

    public decimal Total => Quantity * UnitPrice;

    public void Clear()
    {
        Quantity = 0;
        UnitPrice = 0m;
    }

    // Raises the change for the property and for the one computed from it, so the window's Total binding updates too.
    private void Set<T>(ref T field, T value, string dependent, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(dependent));
    }
}
