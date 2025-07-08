# Version Management

Version is defined in a few central locations and automatically propagated throughout the codebase.

## Version Sources

1. **`.version`** - Plain text file containing the current version (e.g., `0.10.0`)
   - Used by: Makefile, shell scripts
   
2. **`Directory.Build.props`** - MSBuild properties file
   - Used by: .NET build system, F# assemblies
   - Provides: Assembly version, file version, product version

3. **`README.md`** - Automatically updated by the version script
   - Shows the current version to users

## How Versions Are Used

- **F# Code**: Uses `Constants.getVersion()` which reads from assembly metadata
- **Makefile**: Reads from `.version` file via `$(shell cat .version)`
- **Docker Compose**: Uses `${VERSION}` environment variable set by Makefile
- **Export Metadata**: Automatically includes version in exported JSONL files
- **GitHub Releases**: Uses version from `Directory.Build.props` plus build number (e.g., `v0.14.0-11`)

## Updating the Version

Use the provided script to update all version references:

```bash
# Can be run from any directory
./scripts/update-version.sh 0.11.0

# Or from within scripts directory
./update-version.sh 0.11.0
```

This will update:
- `.version` file
- `Directory.Build.props` (Version, AssemblyVersion, FileVersion)
- `README.md` (all "Version X.Y.Z" references)

All other references will automatically pick up the new version.

## GitHub Release Versioning

The GitHub Actions workflow creates releases with the format: `v{version}-{run_number}`

For example:
- Local version: `0.14.0`
- GitHub release: `v0.14.0-11` (where 11 is the GitHub Actions run number)

This ensures each release is unique even if the version hasn't changed.

## Version Format

Use semantic versioning: `MAJOR.MINOR.PATCH`

- **MAJOR**: Breaking changes
- **MINOR**: New features, backwards compatible
- **PATCH**: Bug fixes, backwards compatible
