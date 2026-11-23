# Contributing to Barcode Board

Bug reports, suggestions and pull requests are welcome. Please read this guide before opening a
pull request.

## Before you start

- Barcode Board is **source-available, not open source**. Read the [license](LICENSE.md): you
  may fork, clone and modify the code **only** to prepare issues or pull requests for this
  repository. Redistribution and republication are not allowed.
- [Section 3 of the license](LICENSE.md#3-contributions) applies to every contribution: by
  submitting one, you confirm you have the right to do so and grant the copyright holder the
  right to use, modify, publish and relicense it as part of the software.

## Workflow

1. Open an issue describing the bug or the feature, or comment on an existing one.
2. Fork the repository and create a branch from `main`.
3. Make your changes and check that:
   - `dotnet test tests/BarcodeBoard.Core.Tests` passes;
   - the app builds and runs: `dotnet run --project src/BarcodeBoard`;
   - you tried the change in the app, in the light and the dark theme.
4. Open a pull request **against `main`** and fill in the template.

Every pull request runs the automated checks: build, tests, and the commit and pull request
titles. A pull request can only be merged once all checks pass and the maintainer has reviewed
it.

### Branches and releases

- `main` always builds and passes its tests. Nothing is pushed to it directly: every change
  reaches it through a pull request.
- Contributors work on a branch of their fork, created from `main`.
- The maintainer works on `arbitro-dev`, a long-lived branch merged into `main` through pull
  requests (merge commits, so that `arbitro-dev` can then be brought up to date with `main`).
- A version is released by pushing a `vX.Y.Z` tag on `main`, by the maintainer only: the release
  workflow builds, tests, creates the installer and the portable exe, and attaches them to a
  GitHub release.
- The release notes are written from the **commit titles** since the previous version, grouped by
  type (new features, bug fixes…), by `.github/scripts/release_notes.sh`. Keep the titles
  meaningful for users. Every pull request to `main` previews them in its **Release notes** check.
- Run the release workflow by hand (**Run workflow**) to try it: it builds version `0.0.0` and
  publishes nothing.

### Actions and packages

Every action used by the workflows is pinned to a commit, its version kept in a comment: a tag
can be moved to other code, a commit cannot. Dependabot proposes updates once a week, a week
after their release: one pull request for the actions, one for the NuGet packages. They are never
merged automatically. Before merging one, the maintainer:

- reads the release notes and the comparison between the two versions, and looks for changes
  that do not match them, such as new network calls;
- checks that each new commit is the one the version tag points to in the official repository of
  the action, not a commit from a fork.

## Commit messages

Commit titles and pull request titles follow
[Conventional Commits](https://www.conventionalcommits.org):

```
<type>(<optional scope>)!: <description>
```

- Types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`,
  `revert`.
- Add `!` for a breaking change, for example a `.codes` file older versions cannot read.
- Keep the whole title under 100 characters, with a lowercase description in the imperative
  mood.
- Leave a blank line between the title and the body.

Examples: `feat(scan): compare the scanned text with the expected code`,
`fix(files): reject a .codes file with a duplicate key`.

## Development setup

- Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Inno Setup 6](https://jrsoftware.org/isinfo.php), only to build the installer.
- If the project folder has a very long path (over about 150 characters), a Release publish
  exceeds the 260-character limit of Windows and the exe fails to start: publish from a folder
  with a short path.

| Folder | Contents |
|---|---|
| `src/BarcodeBoard.Core` | Code generation (ZXing.Net), pixel layout, code images (PNG, DIB), `.codes` files, language. No UI, fully tested. |
| `src/BarcodeBoard.ViewModels` | App logic without UI: tabs, codes, scan mode, keyboard rules, settings, texts. Fully tested. |
| `src/BarcodeBoard` | The WinUI 3 app (Windows App SDK, unpackaged and self-contained) |
| `tests/BarcodeBoard.Core.Tests` | Tests of the core and of the app logic, and checks of the theme resources |
| `installer` | Inno Setup script, and the conversion of the license for its license page |
| `tools/IconGen.cs` | Icon generator |

## Rules of the project

### Codes

- Codes are always **pure black on pure white**, whatever the theme.
- Each module is a whole number of physical pixels: never smoothed, never stretched. Codes are
  shown by `PixelImage`, sized to the bitmap in logical pixels and drawn in nearest-neighbor mode;
  never by an `Image`, which cannot choose its interpolation. A scanner needs the whole code: a code too wide
  for its card gets thinner, down to one pixel per module, then is replaced by a message; it can
  still be copied or opened in full screen. It is never cut. A code with an error can be neither
  copied nor opened in full screen.
- Quiet zones: 4 modules for QR codes, 10 for Code 128.
- Any change of the drawing comes with a test that reads the code back (see `CoreTests.cs`).

### Scan mode

A USB barcode scanner types what it reads. In scan mode the keyboard must change nothing in the
tab: no shortcut, no edit, no rename. Control characters sent as Ctrl+letter go into the read
text. The rules live in `KeyRouting` and `ScanReader` (`src/BarcodeBoard.ViewModels/Keyboard.cs`)
and are unit-tested: change them there, with a test.

### Text and languages

The app is in English and French, following the language of Windows. Every user-facing text goes
through `src/BarcodeBoard.ViewModels/Loc.cs` (both languages side by side; in XAML,
`{x:Bind local:Loc.Name}`), and the messages of the core through `Lang.T`. Never hard-code a
user-facing text. The displayed name is *Barcode Board* in English and *Tableau de code-barres*
in French; the technical name (exe, folders, file format) stays `BarcodeBoard`.

To check the other language without changing Windows, run a Debug build with `--lang en` or
`--lang fr`.

### Colors and themes

Every color comes from a key of the palettes, used with `ThemeResource`, or from a WinUI theme
resource. The `Light` and `Dark` theme dictionaries of `App.xaml` define the same keys, and no
palette key may reuse another key of `App.xaml`: the tests check both, and that every requested
key exists. Backgrounds set in code use `Theme.Brush` (layers on Mica or opaque). A dialog or a
flyout is shown outside the window tree: give it the window theme (`RequestedTheme`).

### Files

The `.codes` format is readable JSON. A file the app cannot read must be refused with a message,
never crash the app: add a case to `Invalid_files_are_rejected` for each new rule. Files are
written to a temporary file, flushed, then put in place of the old one.

### Visual checks

Debug builds accept options to check a change and take screenshots:
`--demo [--scan] [--theme light|dark] [--lang en|fr] [--no-mica] --capture file.png`.
The capture is the window as Windows composes it, codes included pixel for pixel. `--no-mica`
keeps an opaque background, so the capture holds nothing of the rest of the screen: use it for
published images. `--simulate` drives the keyboard rules without any real click or key press and
writes its log next to the capture.
