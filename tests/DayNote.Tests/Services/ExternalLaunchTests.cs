using System.IO;
using System.Threading.Tasks;
using DayNote.Services;
using Xunit;

namespace DayNote.Tests.Services;

public sealed class ExternalLaunchTests
{
    [Fact]
    public async Task A_declined_platform_launch_fails_instead_of_acknowledging_success()
    {
        await Assert.ThrowsAsync<IOException>(() => ExternalLaunch.RequireSuccessAsync(Task.FromResult(false)));
        await ExternalLaunch.RequireSuccessAsync(Task.FromResult(true));
    }
}
