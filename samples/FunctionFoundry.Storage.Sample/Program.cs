using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Storage;

byte[] payload = Encoding.UTF8.GetBytes("FunctionFoundry.Storage sample");
string work = Path.Combine(Path.GetTempPath(), "ff-storage-sample-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
string target = Path.Combine(work, "target");
string storeRoot = Path.Combine(work, "cas");
Directory.CreateDirectory(target);

var writer = await TransactionalFileSetWriter.BeginAsync(target);
await writer.StageFileAsync("app/config.json", """{"enabled":true}"""u8.ToArray());
await writer.StageFileAsync("app/version.txt", payload);
TransactionCommitResult commit = await writer.CommitAsync();
Console.WriteLine($"Committed {commit.FileCount} files (fallback={commit.UsedFallbackReplace})");

var store = new ContentAddressedStore(storeRoot);
ContentAddressedObjectInfo stored = await store.PutAsync(payload);
Console.WriteLine($"Stored object {stored.ContentHashHex} deduplicated={stored.WasDeduplicated}");
ContentAddressedObjectInfo duplicate = await store.PutAsync(payload);
Console.WriteLine($"Second put deduplicated={duplicate.WasDeduplicated}");

var chunker = new ContentDefinedChunker();
IReadOnlyList<ContentChunk> chunks = await chunker.ChunkAsync(RandomNumberGenerator.GetBytes(48 * 1024));
Console.WriteLine($"Chunked random payload into {chunks.Count} segments");

var tree = new MerkleFileTree();
MerkleFileTreeSnapshot snapshot = await tree.SnapshotAsync(target);
Console.WriteLine($"Merkle root {snapshot.RootHashHex} entries={snapshot.Entries.Count}");

ContentAddressedGcPlan plan = await store.PlanGarbageCollectionAsync(new HashSet<string>(StringComparer.Ordinal) { stored.ContentHashHex });
Console.WriteLine($"GC plan reclaimable bytes={plan.TotalReclaimableBytes} candidates={plan.Candidates.Count}");
