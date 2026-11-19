# draw_audit

Automates the SharePoint drawing-hierarchy extraction described in
[doc/SharePoint_Drawing_Hierarchy_Extraction_Prompt.md](doc/SharePoint_Drawing_Hierarchy_Extraction_Prompt.md).
It reads engineering drawing PDFs and the per-Sub Excel delivery lists (DL), then produces a
summary Excel workbook with one sheet per Sub (drawing hierarchy Levels 1–5, quantities,
revisions, scope, ITP/EXC/painting/ISO fields) plus an Inventory audit sheet.

Each hierarchy row is coloured across columns A–W by its level. A **Warnings Summary** sheet
(first tab) lists every warning from all Subs with the Sub folder, issue type, affected item, the
drawing/DL that generates it, its source file, and a link to the affected row in the Sub sheet.

## Desktop application (DrawAudit.exe)

Source: [tools/DrawAuditApp](tools/DrawAuditApp) (WPF). Build a single self-contained exe (no .NET
install needed on the target PC):

```powershell
dotnet publish tools\DrawAuditApp -c Release -o publish
```

This produces a single file, `publish\DrawAudit.exe` (template and default colours built in). In the app:

1. **Documents folder** – the folder containing the `Sub...` folders. The Subs are found by scanning it.
2. **Inventory (optional)** – auto-detected (`*inventory*.xls*` in the folder); used only to cross-check
   the scan and report differences. *Clear* removes it.
3. **Subs** – all selected by default; *Select all* / *Deselect all* or tick a subset. Subs with several
   DLs show which DL is used; Sub folders without DL/PDF files are greyed out.
4. **Output file** – default `out.xlsx` next to the documents folder.
5. **Run** – progress, log and *Cancel*; afterwards *Open output* / *Open folder*.

Choices (folder, output, deselected Subs) are remembered in `%APPDATA%\DrawAudit\settings.json`.

## How files are selected

- Every `Sub...` folder in the documents folder is scanned. Files under a DL folder (`1 DL`, `01 DL`,
  `DL`) are Delivery Lists, files under a PDF folder (`3 PDF`, `03 PDF`, `PDF`) are drawings. `Old`
  folders and unsupported file types are skipped (listed as "No" on the Inventory sheet).
- Per document the latest revision is used; for a DL the Excel file over the PDF. One DL per Sub:
  if differently named DLs remain, the highest revision is used (warning; equal revisions are
  reported as ambiguous).
- PDFs elsewhere in a Sub (e.g. `Revision 2025-10-13`, `Revised on ...`) are not used, but a warning
  is raised when they hold a newer revision than the one used, or the only copy of a drawing.
- With an inventory: Sub folders/files missing from it, or listed but not on disk, are warnings.

The summary template is **built into the application** (`tools/Pipeline/Resources/SummaryTemplate.xlsx`,
version from its Info!B1). To update it, replace that file and rebuild.

## Command line

The pipeline can also be run with the `RunAll` tool from the repo root (`c:\src\draw_audit`):

```powershell
dotnet run --project tools\RunAll -- `
  --root "source\huashang" `
  --inventory "source\huashang\Hierarchy inventory - Biotek.xlsm" `
  --out "out.xlsx"
```

Required: `--root`, `--out`. Optional: `--inventory` (cross-check), `--sub`, `--db` (default
`store.sqlite`, wiped each run), `--config`, `--template` (overrides the built-in template, for testing).

`--sub` takes one Sub or a comma-separated list, e.g. `--sub "1002-1,1002-4,1012-1&2"`.
Quote the value in PowerShell (`&` is special). Sub IDs not found are reported and skipped.

## Configuration

The output colours are built into the application (defaults from
[tools/Pipeline/appsettings.json](tools/Pipeline/appsettings.json)). To use other colours, put an
`appsettings.json` with the same structure next to `DrawAudit.exe` (or pass `--config <path>` on the
command line):

```json
{
  "LevelColors": {
    "1": "221, 235, 247",
    "2": "226, 239, 218",
    "3": "255, 242, 204",
    "4": "252, 213, 180",
    "5": "230, 184, 183"
  },
  "SummaryHeaderColor": "221, 235, 247"
}
```

- `LevelColors` (required, levels 1–5): row fill (columns A–W) per hierarchy level.
- `SummaryHeaderColor` (optional, defaults to level 1): header row of the Warnings Summary sheet.
- Colours are `"#RRGGBB"` or `"R, G, B"`. The defaults match the spec (section 9).

An `appsettings.json` next to the executable overrides the built-in colours; `--config <path>` on
`RunAll` / `SummaryWriter` uses a specific file. Changing the built-in defaults means editing the
source file and rebuilding. An invalid or incomplete config stops the run with an error naming the
problem.

## Troubleshooting

### `dotnet run` fails with "No .NET SDKs were found"

Symptom:

```
The command could not be loaded, possibly because:
  * You intended to execute a .NET application: The application 'run' does not exist.
  * You intended to execute a .NET SDK command: No .NET SDKs were found.
```

Cause: an active Anaconda/Miniconda `(base)` environment ships a **runtime-only** `dotnet`
(e.g. `C:\pinokio\bin\miniconda\dotnet\dotnet.exe`) and sets `DOTNET_ROOT` to point at it.
This shadows the real SDK on `PATH`, so `dotnet run` (an SDK command) can't be resolved even
though a runtime is present.

Diagnose:

```powershell
Get-Command dotnet | Format-List Source   # points into miniconda = shadowed
$env:DOTNET_ROOT                           # points into miniconda = shadowed
& "C:\Program Files\dotnet\dotnet.exe" --list-sdks   # confirms the real SDK exists
```

Fix for the current session — put the real SDK ahead of the conda one:

```powershell
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH
dotnet --list-sdks   # should now list 10.x from C:\Program Files\dotnet\sdk
```

Permanent fix — pick one:

- Run `conda deactivate` before working in this repo, or
- Remove the `dotnet` package from the conda base environment so it stops shadowing the system SDK.
