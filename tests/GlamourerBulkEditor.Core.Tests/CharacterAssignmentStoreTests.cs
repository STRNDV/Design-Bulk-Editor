using GlamourerBulkEditor.Core.Services;

namespace GlamourerBulkEditor.Core.Tests;

public class CharacterAssignmentStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public CharacterAssignmentStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "gbe-assignment-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private string StorePath => Path.Combine(_tempDirectory, "assignments.json");

    [Fact]
    public async Task GetReturnsNullForUnknownDesignWithoutLoadedFile()
    {
        var store = new CharacterAssignmentStore(StorePath);
        await store.LoadAsync();

        Assert.Null(store.Get("some-design-id"));
    }

    [Fact]
    public async Task SetAsyncPersistsAssignmentAcrossNewStoreInstance()
    {
        var store = new CharacterAssignmentStore(StorePath);
        await store.LoadAsync();
        await store.SetAsync("design-1", "Aeryn Vale");

        var reloaded = new CharacterAssignmentStore(StorePath);
        await reloaded.LoadAsync();

        Assert.Equal("Aeryn Vale", reloaded.Get("design-1"));
    }

    [Fact]
    public async Task ClearAsyncRemovesAssignment()
    {
        var store = new CharacterAssignmentStore(StorePath);
        await store.LoadAsync();
        await store.SetAsync("design-1", "Aeryn Vale");

        await store.ClearAsync("design-1");

        Assert.Null(store.Get("design-1"));
    }

    [Fact]
    public async Task LoadAsyncRecoversFromCorruptedFileWithoutThrowing()
    {
        await File.WriteAllTextAsync(StorePath, "{ not valid json");

        var store = new CharacterAssignmentStore(StorePath);
        var exception = await Record.ExceptionAsync(() => store.LoadAsync());

        Assert.Null(exception);
        Assert.Null(store.Get("design-1"));
    }
}
