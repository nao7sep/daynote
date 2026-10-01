using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using DayNote.Core.Backup;
using DayNote.Core.Storage;
using DayNote.Tests.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.I18n;

[Collection(AppPathsEnvironment.CollectionName)]
public sealed class PopulatedMainWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teardown_closes_the_store_before_deletion_and_the_next_fixture_owns_its_store(bool show)
    {
        var originalHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        string? previousFixtureHome = null;
        for (var run = 0; run < 2; run++)
        {
            var fixture = PopulatedMainWindow.Create();
            try
            {
                if (show)
                {
                    fixture.Window.Show();
                }
                await fixture.FillAsync();
                BackupStore.Close();
                using (var connection = new SqliteConnection($"Data Source={new AppPaths().BackupStoreFile};Mode=ReadOnly;Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM backups WHERE path = $path";
                    command.Parameters.AddWithValue("$path", Path.Combine(fixture.Home, "config.json"));
                    Assert.True(Convert.ToInt64(command.ExecuteScalar()) > 0);
                }
                // Reopen through the managed write path so Dispose has a live singleton to close.
                fixture.ViewModel.CycleTextStyleCommand.Execute(null);
                if (previousFixtureHome is not null)
                {
                    Assert.False(Directory.Exists(previousFixtureHome));
                }
                previousFixtureHome = fixture.Home;
            }
            finally
            {
                fixture.Dispose();
            }
            Assert.False(Directory.Exists(fixture.Home));
            Assert.Equal(originalHome, Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable));
        }
    }
}
