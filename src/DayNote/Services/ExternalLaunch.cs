namespace DayNote.Services;

/// <summary>Translate the platform launcher's declined outcome at the external-effect boundary.</summary>
internal static class ExternalLaunch
{
    public static async Task RequireSuccessAsync(Task<bool> launching)
    {
        if (!await launching)
            throw new IOException("The operating system declined the external launch.");
    }
}
