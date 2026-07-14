using FunctionFoundry.Text;

var spoofDetector = new UnicodeSpoofDetector();
UnicodeSpoofAnalysisResult spoof = spoofDetector.Analyze("pаypal");
Console.WriteLine($"Spoof findings: {spoof.Findings.Count}; skeleton={spoof.Skeleton}; data={spoof.DataVersion}");

var secretScanner = new SecretCandidateScanner();
IReadOnlyList<SecretCandidateFinding> secrets = secretScanner.Scan("api_key=AKIA1234567890ABCDEF");
Console.WriteLine($"Secret findings: {secrets.Count}; preview={secrets[0].RedactedPreview}");

var nearDup = new NearDuplicateTextIndex();
nearDup.Upsert("doc1", "the quick brown fox jumps over the lazy dog");
nearDup.Upsert("doc2", "the quick brown fox jumps over the lazy cat");
Console.WriteLine($"Near-dup candidates: {nearDup.Query("doc1").Count}");

const string csv = "id,name\n1,alice\n2,bob\n";
var dialect = new DelimitedTextDialectInferrer();
DelimitedTextDialectInferenceResult dialects = dialect.Infer(csv);
Console.WriteLine($"Top dialect delimiter='{dialects.Candidates[0].Delimiter}' confidence={dialects.Candidates[0].Confidence:F2}");
