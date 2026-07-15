# Release build scripts

Scripts in this folder produce a top-level `Release/` folder with FunctionFoundry deliverables.

## Linux / macOS

```bash
./build/publish-release.sh
```

## Windows

```bat
build\publish-release.cmd
```

## What the scripts do

1. `dotnet restore`
2. `dotnet build -c Release`
3. `dotnet pack -c Release` (NuGet packages for distribution)
4. Copy production library outputs and packages into `Release/`:

```text
Release/
  MANIFEST.txt
  bin/
    FunctionFoundry.Security/
      FunctionFoundry.Security.dll
      FunctionFoundry.Security.xml
      ...
    ...
  packages/
    FunctionFoundry.*.nupkg
    FunctionFoundry.*.snupkg
```

Test, sample, and benchmark binaries are not copied into `Release/bin`.

`Release/` is gitignored.
