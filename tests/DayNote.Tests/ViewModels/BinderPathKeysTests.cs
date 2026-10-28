using System.IO;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class BinderPathKeysTests
{
    [Fact]
    public void A_lookup_started_before_a_published_name_cannot_replace_it()
    {
        var keys = new BinderPathKeys();
        var path = Path.Combine(Path.GetTempPath(), "binder.daynote");
        var alias = Path.Combine(Path.GetTempPath(), "BINDER.DAYNOTE");
        var oldName = Path.Combine(Path.GetTempPath(), "Binder.daynote");
        keys.Adopt(keys.Lookups, [path, alias], [oldName, oldName]);
        var older = keys.Lookups;
        keys.Published(path, path);
        keys.Adopt(older, [path, alias], [oldName, oldName]);
        Assert.True(keys.Equal(path, alias));
        keys.Adopt(keys.Lookups, [alias], [null]);
        Assert.True(keys.Equal(path, alias));
        Assert.False(keys.Equal(path, oldName));
        keys.Adopt(keys.Lookups, [oldName], [path]);
        Assert.True(keys.Equal(path, oldName));
    }
}
