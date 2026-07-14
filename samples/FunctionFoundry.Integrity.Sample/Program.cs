using System.Text;
using FunctionFoundry.Integrity;

byte[] canonical = CanonicalJson.Canonicalize("{\"b\":2,\"a\":1}");
Console.WriteLine($"Canonical JSON: {Encoding.UTF8.GetString(canonical)}");

var manifestBuilder = StreamingHashManifest.CreateBuilder(metadataPolicy: new ManifestMetadataPolicy(AllowCustomMetadata: true));
manifestBuilder.AddMetadata("purpose", "sample");
manifestBuilder.AddEntry("hello.txt", new MemoryStream("integrity"u8.ToArray()));
byte[] manifest = manifestBuilder.Build();
StreamingHashManifestDocument parsed = StreamingHashManifest.Parse(manifest);
Console.WriteLine($"Manifest entries: {parsed.Entries.Count}, algorithm: {parsed.Algorithm}");

byte[][] leaves = ["a"u8.ToArray(), "b"u8.ToArray(), "c"u8.ToArray()];
MerkleTree tree = MerkleTree.Build(leaves);
MerkleProof proof = tree.CreateProof(1);
MerkleProofVerificationResult proofOk = MerkleTree.VerifyProof(tree.Root.Span, leaves[1], proof);
Console.WriteLine($"Merkle proof valid: {proofOk.IsValid}");

HashChainRecord genesis = HashChain.CreateGenesis("{\"event\":\"boot\"}"u8);
HashChainRecord second = HashChain.Append(genesis, "tick"u8);
HashChainVerificationResult chain = HashChain.Verify([genesis, second]);
Console.WriteLine($"Hash chain valid: {chain.IsValid}, head: {chain.HeadHashHex?[..16]}...");
