using System.Windows.Input;

namespace BatteryInfo.App.ViewModels;

/// <summary>An <see cref="ICommand"/> over a delegate. Call <see cref="RaiseCanExecuteChanged"/> when
/// <c>canExecute</c> may have changed.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
