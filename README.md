<div align="center">

# GothicSaveEditor Redux v1.0.0

**A modern, high-performance savegame editor for Gothic 1, Gothic 2 (Night of the Raven), and large-scale mods such as Odyssee.**

[![English](https://img.shields.io/badge/Language-English-2563EB?style=for-the-badge)](README.md)
[![Deutsch](https://img.shields.io/badge/Sprache-Deutsch-4B5563?style=for-the-badge)](README_DE.md)
[![Русский](https://img.shields.io/badge/Язык-Русский-4B5563?style=for-the-badge)](README_RU.md)

</div>

---

## Highlights & Features

- **Structured `BinSafeReader` (.NET 10 / AnyCPU 64-bit)**
  - Thread-safe, type-driven parser for the ZenGin `BIN_SAFE` format (`SAVEDAT.SAV`) built on `ReadOnlySpan<byte>`.
  - Fixes legacy array-index shifting, header misinterpretation, and `OverflowException` crashes on large mod saves (e.g. Odyssee with 40,000–100,000+ variables).
  - Allocation-free natural sorting (`VariablesComparer`) with support for arbitrary-length numeric sequences.
- **Atomic & Integrity-Checked Saving (`SaveDatWriter`)**
  - Patches only modified integer values into a temporary `.tmp` file, flushes to disk (`Flush(true)`), and atomically replaces the target via `File.Replace`.
  - Verifies file size and SHA-256 hash before writing to detect external modifications on disk.
- **Automatic Folder Backups (`BackupService`)**
  - Copies the entire `savegameN` slot folder to `%LOCALAPPDATA%\GothicSaveEditor\Backups\<Mod>\<Slot>_<Timestamp>\` before every save, with automatic rotation.
  - Aborts saving if backup creation fails so your savegame is never left unprotected.
- **Modern & Responsive WPF UI**
  - MaterialDesign 3 styling with **Dark Mode** (default) and **Light Mode** toggle.
  - `PerMonitorV2` HiDPI awareness and `Segoe UI Variable` typography for crisp rendering on high-resolution displays.
  - Asynchronous loading/saving with progress bar & cancellation, 250 ms debounced background search, and `DataGrid` row recycling.
- **Multilingual UI & Encodings**
  - Full interface localization in **English**, **German**, and **Russian**.
  - Selectable savegame codepages: **Windows-1252** (Western / DE / EN), **Windows-1250** (Central European / PL), and **Windows-1251** (Cyrillic / RU).

---

## Requirements & Build

- [.NET 10 SDK](https://dotnet.microsoft.com/) (Windows)

```bash
# Build the solution
dotnet build GothicSaveEditor.sln

# Run unit & roundtrip tests
dotnet test GothicSaveEditor.sln

# Launch the application
dotnet run --project GothicSaveEditor.csproj
```

---

## Project Structure

- `GothicSaveEditor` — WPF desktop application (`net10.0-windows`, `CommunityToolkit.Mvvm`, `MaterialDesignThemes` 5.x, `NLog` 5.x)
- `GothicSaveEditor.Core` — Platform-independent core library (`BinSafeReader`, `SaveDatWriter`, `BackupService`, `VariablesComparer`, `EncodingHelper`)
- `GothicSaveEditor.Tests` — xUnit test suite (legacy snapshot comparison, SHA-256 roundtrip, fuzzing, backup rotation, encoding tests)

---

## Credits

Based on the original **GothicSaveEditor** fork by **Flycrafting** (originally created by **RightMAN**).
