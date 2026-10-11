using System.IO;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class BinderPathKeysTests
{
    [Fact]
    public void Older_registry_and_admission_snapshots_cannot_replace_a_published_name()
    {
        var keys = new BinderPathKeys();
        var path = Path.Combine(Path.GetTempPath(), "binder.daynote");
        var alias = Path.Combine(Path.GetTempPath(), "BINDER.DAYNOTE");
        var oldName = Path.Combine(Path.GetTempPath(), "Binder.daynote");
        Assert.True(keys.TryAdopt(keys.Revision, [path, alias], [oldName, oldName]));
        var oldRevision = keys.Revision;
        keys.Published(path, path);
        Assert.False(keys.TryAdopt(oldRevision, [path, alias], [oldName, oldName]));
        Assert.True(keys.Equal(path, alias));
        Assert.True(keys.TryAdopt(keys.Revision, [alias], [null]));
        Assert.True(keys.Equal(path, alias));
        Assert.False(keys.Equal(path, oldName));
        Assert.True(keys.TryAdopt(keys.Revision, [oldName], [path]));
        Assert.True(keys.Equal(path, oldName));
    }
}
