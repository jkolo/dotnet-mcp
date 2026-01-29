# Quickstart: CI/CD Pipeline

## Trigger a Release

```bash
# Tag a version on main
git tag v1.0.0
git push origin v1.0.0
```

The release workflow will automatically:
1. Build the project
2. Run all tests
3. Pack the NuGet package with version `1.0.0`
4. Publish to NuGet.org and GitHub Packages
5. Create a GitHub Release with the `.nupkg` attached

## Pre-release

```bash
git tag v1.1.0-beta.1
git push origin v1.1.0-beta.1
```

## Run After Release

```bash
# No installation needed (.NET 10+)
dnx netinspect-mcp

# Or one-shot execution
dotnet tool exec netinspect-mcp

# Or install globally
dotnet tool install -g NetInspect.Mcp
```

## Required Secrets

Set these in GitHub repository settings → Secrets:

- `NUGET_API_KEY` — API key from nuget.org for publishing
- `MATRIX_HOMESERVER` — (optional) Matrix server URL for failure notifications
- `MATRIX_TOKEN` — (optional) Matrix access token
- `MATRIX_ROOM_ID` — (optional) Matrix room ID for notifications

## Verify CI

Push any branch or open a PR — the CI workflow runs automatically and reports build + test results.
