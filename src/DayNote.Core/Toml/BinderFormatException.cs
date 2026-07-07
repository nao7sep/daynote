namespace DayNote.Core.Toml;

/// <summary>Thrown when a <c>.daynote</c> file cannot be parsed as a valid binder.</summary>
public sealed class BinderFormatException : Exception
{
    public BinderFormatException(string message) : base(message)
    {
    }

    public BinderFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
