namespace DayNote.Core.Toml;

/// <summary>Thrown when a <c>.daynote</c> file cannot be parsed as a valid notebook.</summary>
public sealed class NotebookFormatException : Exception
{
    public NotebookFormatException(string message) : base(message)
    {
    }

    public NotebookFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
