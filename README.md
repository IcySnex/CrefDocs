# CrefDocs

CrefDocs generates compact, linked Markdown API references from .NET projects. It captures a released public API into a deterministic `crefdocs.json` snapshot, then renders that snapshot without rebuilding the original project.

## Commands

Capture a release:

```bash
crefdocs capture \
  --project Source/MyLibrary/MyLibrary.csproj \
  --framework net10.0 \
  --package MyLibrary \
  --version 1.2.0 \
  --source-root Source/MyLibrary \
  --output artifacts/crefdocs.json
```

Render the snapshot:

```bash
crefdocs render \
  --snapshot artifacts/crefdocs.json \
  --output Docs/content/reference \
  --structure namespace \
  --base-route /reference
```

For local previews, `generate` captures and renders in one invocation:

```bash
crefdocs generate \
  --project Source/MyLibrary/MyLibrary.csproj \
  --framework net10.0 \
  --package MyLibrary \
  --version 1.2.0 \
  --output Docs/content/reference \
  --structure source
```

Run `crefdocs --help` for every option.

## Reference structure

The output structure is selected when rendering, so it is not baked into the release snapshot:

- `namespace` mirrors CLR namespaces.
- `source` mirrors folders below `--source-root`.
- `flat` places every type directly beneath the reference root.

CrefDocs creates one page per public type and directory index pages. Internal types link to their generated pages; framework types link to Microsoft Learn. A generated-file manifest lets subsequent runs remove stale pages without deleting handwritten files in the same directory.

## Development

```bash
dotnet restore CrefDocs.slnx
dotnet test CrefDocs.slnx
dotnet pack src/CrefDocs/CrefDocs.csproj --configuration Release --output artifacts
```

Install the locally packed tool with:

```bash
dotnet tool install --global CrefDocs.Tool --add-source artifacts
```
