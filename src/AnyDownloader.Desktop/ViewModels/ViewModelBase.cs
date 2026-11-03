namespace AnyDownloader.Desktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Base class for all ViewModels in the application
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isLoading;
    private string? _errorMessage;

    /// <summary>
    /// Gets or sets the loading state
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Gets or sets the error message
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Clears the error message
    /// </summary>
    protected void ClearError()
    {
        ErrorMessage = null;
    }

    /// <summary>
    /// Sets an error message
    /// </summary>
    protected void SetError(string message)
    {
        ErrorMessage = message;
        IsLoading = false;
    }
}
