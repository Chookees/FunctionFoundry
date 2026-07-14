namespace FunctionFoundry.Observability.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_masks_sensitive_property_names()
    {
        var redactor = new SensitiveDataRedactor();
        var input = new Dictionary<string, object?>
        {
            ["user"] = "alice",
            ["password"] = "hunter2",
        };

        RedactionResult result = redactor.Redact(input);

        var output = Assert.IsType<Dictionary<string, object?>>(result.Value);
        Assert.Equal("alice", output["user"]);
        Assert.Equal("[REDACTED]", output["password"]);
        Assert.Equal(1, result.RedactedFieldCount);
    }

    [Fact]
    public void Redact_detects_jwt_pattern_with_hash_strategy()
    {
        var redactor = new SensitiveDataRedactor();
        string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature";
        RedactionResult result = redactor.Redact(new Dictionary<string, object?> { ["payload"] = jwt });

        var output = Assert.IsType<Dictionary<string, object?>>(result.Value);
        string redacted = Assert.IsType<string>(output["payload"]);
        Assert.StartsWith("sha256:", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_detects_high_entropy_secret_candidates()
    {
        var redactor = new SensitiveDataRedactor(new SensitiveDataRedactorOptions
        {
            EntropyDetector = new EntropySecretDetector(entropyThreshold: 3.5, minimumLength: 16),
        });
        string secret = "aZ9+xK2mN8pQ4rT6vW1yZ3bC5dE7fG9";
        RedactionResult result = redactor.Redact(secret);
        Assert.Equal("[REDACTED]", result.Value);
    }

    [Fact]
    public void Redact_handles_nested_objects_and_cycles()
    {
        var redactor = new SensitiveDataRedactor();
        var node = new Dictionary<string, object?> { ["name"] = "root" };
        node["self"] = node;
        node["child"] = new Dictionary<string, object?> { ["value"] = "AKIAIOSFODNN7EXAMPLE" };

        RedactionResult result = redactor.Redact(node);
        var output = Assert.IsType<Dictionary<string, object?>>(result.Value);
        Assert.Equal("[CYCLE]", output["self"]);
        var child = Assert.IsType<Dictionary<string, object?>>(output["child"]);
        Assert.StartsWith("sha256:", Assert.IsType<string>(child["value"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_enforces_depth_and_item_limits()
    {
        var redactor = new SensitiveDataRedactor(new SensitiveDataRedactorOptions
        {
            MaxDepth = 2,
            MaxItems = 5,
        });

        var deep = new Dictionary<string, object?>
        {
            ["l1"] = new Dictionary<string, object?>
            {
                ["l2"] = new Dictionary<string, object?> { ["l3"] = "secret-value-never" },
            },
        };

        RedactionResult depthResult = redactor.Redact(deep);
        Assert.True(depthResult.TruncatedDueToDepth);

        var many = Enumerable.Range(0, 20).Select(i => (object?)i).ToList();
        RedactionResult itemResult = redactor.Redact(many);
        Assert.True(itemResult.TruncatedDueToItemLimit);
    }

    [Fact]
    public void Redact_removed_strategy_omits_dictionary_entries()
    {
        var redactor = new SensitiveDataRedactor(new SensitiveDataRedactorOptions
        {
            PropertyNamePolicies =
            [
                new SensitivePropertyNamePolicy(["ssn"], PropertyNameMatchMode.Exact, RedactionReplacementStrategy.Removed),
            ],
        });

        RedactionResult result = redactor.Redact(new Dictionary<string, object?> { ["ssn"] = "123-45-6789", ["id"] = 1 });
        var output = Assert.IsType<Dictionary<string, object?>>(result.Value);
        Assert.False(output.ContainsKey("ssn"));
        Assert.Equal(1, output["id"]);
    }

    [Fact]
    public void Redact_is_deterministic_for_identical_input()
    {
        var redactor = new SensitiveDataRedactor();
        var input = new Dictionary<string, object?> { ["secret"] = "value", ["note"] = "Bearer abc.def.ghi" };
        RedactionResult first = redactor.Redact(input);
        RedactionResult second = redactor.Redact(input);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.Value),
            System.Text.Json.JsonSerializer.Serialize(second.Value));
    }

    [Fact]
    public void Redact_supports_concurrent_calls()
    {
        var redactor = new SensitiveDataRedactor();
        var input = new Dictionary<string, object?> { ["password"] = "pw", ["items"] = new[] { 1, 2, 3 } };
        Parallel.For(0, 64, _ => redactor.Redact(input));
    }
}
