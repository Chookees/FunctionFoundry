using FunctionFoundry.Storage;

namespace FunctionFoundry.Storage.Tests;

public sealed class ContentAddressedGcExecuteTests
{
    [Fact]
    public async Task Execute_deletes_orphan_objects_from_plan()
    {
        string root = Path.Combine(Path.GetTempPath(), "ff-cas-gc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ContentAddressedStore(root);
            ContentAddressedObjectInfo keep = await store.PutAsync("keep"u8.ToArray());
            ContentAddressedObjectInfo drop = await store.PutAsync("drop"u8.ToArray());

            ContentAddressedGcPlan plan = await store.PlanGarbageCollectionAsync(new HashSet<string>(StringComparer.Ordinal) { keep.ContentHashHex });
            Assert.Contains(plan.Candidates, c => c.ContentHashHex == drop.ContentHashHex);

            ContentAddressedGcResult result = await store.ExecuteGarbageCollectionAsync(plan);
            Assert.Equal(1, result.DeletedObjects);
            Assert.True(result.ReclaimedBytes > 0);
            Assert.False(store.Exists(drop.ContentHashHex));
            Assert.True(store.Exists(keep.ContentHashHex));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
