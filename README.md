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
  --metadata Docs/api-reference.json \
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

CrefDocs creates one page per public type and directory index pages. Generic type routes include their arity, such as `style-1` or `dictionary-2`. Internal types link to their generated pages; framework types link to Microsoft Learn. Every component of a constructed generic type links independently. A generated-file manifest lets subsequent runs remove stale pages without deleting handwritten files in the same directory.

By default, `--page-header markdown` renders the page title and linked description in the Markdown body. Documentation themes that provide their own page header can use `--page-header frontmatter` instead. That mode adds linked `markdown` and `docs: true` fields to the frontmatter while retaining the plain `description` used by navigation and SEO metadata.

## Index descriptions

An optional metadata file supplies descriptions for namespace and source-folder indexes without modifying generated XML documentation. Keep it with the documentation project, for example at `Docs/api-reference.json`:

```json
{
  "namespaces": {
    "MyLibrary": "The public MyLibrary API.",
    "MyLibrary.Models": "Models shared by library operations."
  },
  "sections": {
    "": "The public MyLibrary API organized by source folder.",
    "Models": "Models shared by library operations."
  }
}
```

Namespace keys use full CLR namespace names. Section keys use `/`-separated folders relative to `--source-root`; an empty section key describes the source root. Pass the file to `capture` or `generate` with `--metadata`. Its normalized content is embedded in the snapshot, so `render` does not need the original file.

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
