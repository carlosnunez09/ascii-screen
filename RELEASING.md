# Releasing

Releases are built by GitHub Actions ([release.yml](.github/workflows/release.yml)) on a Windows runner. Every run builds and packages `AsciiScreen-vX.Y.Z-win-x64.zip` plus a `.sha256` file, so packaging is always tested. Only the two routes below create a tag and a GitHub release.

## Test build (no tag, no release)

Actions tab, **release**, **Run workflow**, leave **Publish** unticked. The zip appears as a build artifact on the run. The same test build runs on pull requests that change the workflow, `build.ps1` or `install.ps1`.

## Publish a release

Either:

- **Run workflow** with a version such as `0.0.1` and **Publish** ticked. The workflow creates tag `v0.0.1` on `main` and the release. Tick **Draft** to review it before it goes public, **Pre-release** for a beta.
- Or push a tag yourself from a commit on `main`: `git tag v0.0.1` then `git push origin v0.0.1`.

A version with a dash (`v0.1.0-rc1`) is marked as a pre-release automatically. A release only goes out from a commit on `main`, and a tag that already exists is refused.

The programs are not code-signed; the release notes say so and include how to check the SHA-256.
