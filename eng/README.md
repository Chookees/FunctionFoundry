# Engineering notes

## Package project template properties

Recommended properties for a core library:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <RootNamespace>FunctionFoundry.Area</RootNamespace>
  <AssemblyName>FunctionFoundry.Area</AssemblyName>
  <PackageId>FunctionFoundry.Area</PackageId>
  <Description>...</Description>
  <PackageTags>functionfoundry;area</PackageTags>
  <IsTrimmable>false</IsTrimmable>
  <IsAotCompatible>false</IsAotCompatible>
</PropertyGroup>
```

Set `IsTrimmable` / `IsAotCompatible` only after smoke tests pass.

## Test conventions

* Framework: xUnit
* Deterministic seeds for randomized tests
* No mocking frameworks unless interaction-based mocking is required
* Target: ≥85% line coverage across production `src/` (enforced in CI)
* Prefer ≥90% branch coverage on critical algorithms where practical; document justified exclusions

## Benchmark conventions

* Framework: BenchmarkDotNet
* Record environment and runtime version
* Measure throughput, latency, and allocated bytes
