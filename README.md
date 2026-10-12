# windu

[![CI](https://github.com/jagjordi/windu/actions/workflows/ci.yml/badge.svg)](https://github.com/jagjordi/windu/actions/workflows/ci.yml)
[![Release](https://github.com/jagjordi/windu/actions/workflows/release.yml/badge.svg)](https://github.com/jagjordi/windu/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/jagjordi/windu?sort=semver)](https://github.com/jagjordi/windu/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A disk usage analyzer for Windows, in the style of [ncdu](https://dev.yorhel.nl/ncdu)
and [gdu](https://github.com/dundee/gdu). It scans a directory tree, then lets you
walk it in the terminal, sorted largest-first, and delete what you no longer want.

```
windu 1.0.0 ~ Use the arrow keys to navigate, press ? for help
--- C:\Program Files\dotnet\sdk\8.0.424 ------------------------------------
   89.5 MiB [##########] /DotnetTools
   82.7 MiB [######### ] /Sdks
   63.5 MiB [#######   ] /FSharp
   49.2 MiB [######    ] /Roslyn
   25.8 MiB [###       ] /Containers
   11.5 MiB [#         ] /TestHostNetFramework
    5.1 MiB [#         ] Microsoft.Build.dll
    4.9 MiB [#         ] /Microsoft
 Total disk usage: 396.9 MiB   Apparent size: 386.6 MiB   Items: 4242
```

## Installing

Download `windu-<version>-win-x64.msi` from the
[latest release](https://github.com/jagjordi/windu/releases/latest) and
double-click it. It installs to `C:\Program Files\windu`, puts `windu` on your
`PATH`, and registers in Add/Remove Programs, so a new terminal gives you:

```powershell
windu "C:\Program Files"
```

Take the `win-arm64.msi` instead on Arm64 Windows. To install unattended:

```powershell
msiexec /i windu-<version>-win-x64.msi /qn
```

The installer is unsigned, so SmartScreen warns the first time — choose
**More info → Run anyway**. Uninstall from Add/Remove Programs; it removes the
`PATH` entry with it.

### Or just the executable

If you would rather not install anything, each release also ships a zip holding
a single `windu.exe` that writes nothing outside the folder you unpack it into.

| Asset | Needs .NET 8 installed? |
| --- | --- |
| `windu-<version>-win-x64.zip` | **No** — self-contained, ~35 MB |
| `windu-<version>-win-arm64.zip` | **No** — self-contained, ~35 MB |
| `windu-<version>-win-x64-framework-dependent.zip` | Yes — ~200 KB |
| `windu-<version>-win-arm64-framework-dependent.zip` | Yes — ~200 KB |

Every download can be verified against the `SHA256SUMS.txt` published with the
release.

## Building

Requires the .NET 8 SDK.

```bash
dotnet build src/Windu/Windu.csproj -c Release
```

The executable lands in `src/Windu/bin/Release/net8.0/windu.exe`. To produce a
single self-contained file:

```bash
dotnet publish src/Windu/Windu.csproj -c Release -r win-x64 --self-contained
```

To build the MSI exactly as the release workflow does (this installs the WiX
tool on first run):

```powershell
./installer/build-msi.ps1 -Version 1.1.0 -Runtime win-x64 -Output dist
```

The installer pins [WiX](https://wixtoolset.org/) 5, the last release under the
MS-RL; WiX 7 requires accepting the Open Source Maintenance Fee EULA.

## Usage

```bash
windu "C:\Program Files"
```

With no argument it scans the current directory. Scanning runs on a background
thread while a progress window shows the running item count and total; press `q`
to abort it.

### Keys

| Key | Action |
| --- | --- |
| `up`, `down`, `k`, `j` | Move the cursor |
| `right`, `enter`, `l` | Open the selected directory |
| `left`, `backspace`, `h` | Go to the parent directory |
| `home`, `end` | Jump to the first or last item |
| `page up`, `page down` | Scroll a whole screen |
| `n` | Sort by name (again to reverse) |
| `s` | Sort by size (again to reverse) |
| `C` | Sort by item count |
| `M` | Sort by modification time |
| `t` | Toggle directories before files |
| `a` | Toggle apparent size and disk usage |
| `g` | Cycle the percentage and graph columns |
| `u` | Toggle SI (kB) and base-2 (KiB) units |
| `e` | Show or hide hidden items |
| `i` | Show information about the selected item |
| `d`, `delete` | Delete the selected file or directory |
| `b` | Spawn a shell in the current directory |
| `?` | Help |
| `q` | Quit |

### Flags in the left column

| Flag | Meaning |
| --- | --- |
| `!` | The directory could not be read |
| `.` | A subdirectory below it could not be read |
| `@` | A reparse point (symlink, junction) that was not followed |
| `H` | A hard link whose blocks are counted elsewhere |
| `e` | An empty directory |

## Command line

```
Modes:
  -o FILE                  Export the scan as ncdu-compatible JSON ('-' for stdout)
  -f FILE                  Open a previously exported file instead of scanning
  -q, --quiet              Do not draw the scan progress window

Scanning:
  -x                       Stay on one filesystem (the default)
      --cross-file-system  Descend into other volumes and mount points
  -L, --follow-symlinks    Follow symlinks and junctions
      --exclude PATTERN    Skip names matching PATTERN (may be repeated)
  -X, --exclude-from FILE  Read exclude patterns from FILE, one per line
      --hard-link-detection
                           Count each hard link once instead of per name

Display:
      --apparent-size      Start in apparent-size mode instead of disk usage
      --si                 Use kB/MB (powers of 1000) instead of KiB/MiB
      --dirs-first         List directories before files
      --show-percent       Show the percentage column at startup
      --no-graph           Hide the size graph at startup
      --sort FIELD         Sort by name, size, count or mtime
```

## Notes on the numbers

**Disk usage vs apparent size.** Apparent size is the sum of the files' logical
lengths. Disk usage rounds every file up to the volume's allocation unit and adds
a cluster for each directory, so it is the space actually consumed. Press `a` to
switch between them; the footer names the active mode.

**Hard links.** Windows reports a file's link count only through an open handle,
so counting hard links once costs one `CreateFile` per file — around 40x slower on
a large tree. It is therefore off by default; enable it with
`--hard-link-detection` when the accuracy matters.

**Reparse points.** Symlinks, junctions and mount points are listed but not
followed, so their targets are not counted twice. Pass `-L` to follow them.

**Long paths.** Deletion uses the extended-length path prefix, so entries beyond
the 260-character limit can still be removed.

**Permissions.** A directory that cannot be read is flagged rather than aborting
the scan, and its parents are marked so you can see that a total is incomplete.

## Export format

`-o` writes the same JSON that ncdu produces, so the files are interchangeable:

```
[1, 2, {"progname":"windu", ...}, [{dir info}, {child}, [{subdir info}, ...]]]
```

Each entry records its **own** size; subtree totals are recomputed on load. A
scan exported and re-imported is byte-identical.

## Tests

```bash
dotnet test tests/Windu.Tests/Windu.Tests.csproj
```

83 tests cover size formatting and column alignment, the glob matcher, scanning
(aggregation, cluster rounding, exclusions, cancellation, unreadable
directories), the export round-trip, and the browser itself — navigation,
sorting, the toggles, popups, scrolling, narrow and short terminals, and the
handling of control characters in filenames.

The browser tests drive the real rendering code through an offscreen buffer via
`--render`, a hidden diagnostic flag that replays a key script and dumps the
resulting screen as text:

```bash
windu -f scan.json --render "<enter><down>i" --render-size 92x22
```
