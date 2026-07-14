using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Security;

byte[] key = RandomNumberGenerator.GetBytes(32);
var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["demo-key"] = key });
var encryptor = new EnvelopeEncryptor(resolver);

byte[] envelope = encryptor.Encrypt("demo-key", Encoding.UTF8.GetBytes("hello-foundry"), "aad"u8);
byte[] plaintext = encryptor.Decrypt(envelope, "aad"u8);
Console.WriteLine($"Decrypted: {Encoding.UTF8.GetString(plaintext)}");

var pseudonymizer = new Pseudonymizer(key, new PseudonymizerOptions("sample", "tenant", 1));
Console.WriteLine($"Pseudonym: {pseudonymizer.Pseudonymize("user-42"u8)}");

IReadOnlyList<SecretShare> shares = SecretSharer.Split(key, threshold: 2, shareCount: 3);
byte[] recovered = SecretSharer.Combine(shares.Take(2).ToArray());
Console.WriteLine($"Share reconstruct ok: {CryptographicOperations.FixedTimeEquals(key, recovered)}");

KeyRotationPlan plan = KeyRotationPlanner.CreatePlan(
    new Dictionary<string, EnvelopeMetadata> { ["p1"] = encryptor.GetMetadata(envelope) },
    "demo-key");
Console.WriteLine($"Rotation actions: {plan.Actions.Count}, already current: {plan.AlreadyCurrentCount}");
