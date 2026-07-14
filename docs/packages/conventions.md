# Packaging conventions

Each core library project under `src/FunctionFoundry.*`:

* `IsPackable=true`
* Ships `PACKAGE.md` as the package README
* Generates XML documentation
* Emits deterministic builds and snupkg symbols when Source Link metadata is available
* Has a matching unit-test project under `tests/FunctionFoundry.*.Tests`
* Has at least one sample under `samples/`
* May have a benchmark project under `benchmarks/` when performance-sensitive

Forbidden package ids ending in `.All`, `.Common`, `.Utils`, or `.Helpers` fail the build.
