# Contributing to Grimly

Issues and pull requests are welcome.

## Reporting bugs

Open an issue with:

- What you did
- What you expected
- What actually happened
- OS + version (Windows 11 24H2, macOS 14.5, etc.) and which binary you're running (ARM64 / x64 / macOS)

For security issues, **don't open a public issue** — see [SECURITY.md](SECURITY.md).

## Suggesting features

Open an issue describing the use case. The bar is "would this make Grimly better at clarifying writing for most users?" — features specific to one workflow are usually a no.

## Pull requests

1. **Open an issue first** for anything beyond a typo or one-line bug fix. Saves you from writing code I won't merge.
2. **Keep PRs small and focused.** One change per PR.
3. **Match the existing style.** No reformatting unrelated code.
4. **Test on your platform.** Build and exercise the change end-to-end before opening the PR. CI verifies the Windows build but doesn't run the app.
5. **Note OS coverage.** If you only tested Windows, say so — I'll verify Mac before merging (and vice versa).

## Building

See the **Build from source** sections in [README.md](README.md).

## License

By contributing, you agree your contribution is licensed under the [MIT License](LICENSE).
