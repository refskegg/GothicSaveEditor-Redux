<div align="center">

# GothicSaveEditor Redux v1.0.0

**Ein moderner, hochperformanter Savegame-Editor für Gothic 1, Gothic 2 (Die Nacht des Raben) und umfangreiche Mods wie Odyssee.**

[![English](https://img.shields.io/badge/Language-English-4B5563?style=for-the-badge)](README.md)
[![Deutsch](https://img.shields.io/badge/Sprache-Deutsch-2563EB?style=for-the-badge)](README_DE.md)
[![Русский](https://img.shields.io/badge/Язык-Русский-4B5563?style=for-the-badge)](README_RU.md)

</div>

---

## Warum GothicSaveEditor Redux?

Der klassische GothicSaveEditor war über viele Jahre ein unverzichtbares Werkzeug, um Spielstände in **Gothic I & II** auszulesen, Quest-Variablen zu reparieren oder Werte anzupassen – stieß aber bei modernen, riesigen Total-Conversion-Mods wie **Odyssee** oft an seine Grenzen und stürzte ab oder fror beim Laden ein.

**GothicSaveEditor Redux** belebt das Tool von Grund auf neu: Es öffnet selbst gigantische Mod-Spielstände in wenigen Millisekunden, läuft gestochen scharf auf modernen Windows-PCs und legt vor jeder Änderung automatisch ein Sicherheits-Backup deines Spielstands an.

---

## Highlights & Features

- **Strukturierter `BinSafeReader` (.NET 10 / AnyCPU 64-Bit)**
  - Thread-sicherer, typgesteuerter Parser für das ZenGin-`BIN_SAFE`-Format (`SAVEDAT.SAV`) auf Basis von `ReadOnlySpan<byte>`.
  - Behebt Array-Index-Verschiebungen, Header-Fehlinterpretationen sowie `OverflowException`-Abstürze bei großen Mod-Spielständen (z. B. Odyssee mit 40.000–100.000+ Variablen).
  - Allokationsfreie natürliche Sortierung (`VariablesComparer`) für beliebig lange Zahlenfolgen.
- **Atomares & integritätsgeprüftes Speichern (`SaveDatWriter`)**
  - Patcht ausschließlich geänderte Integer-Werte in eine temporäre `.tmp`-Datei, erzwingt `Flush(true)` und ersetzt die Zieldatei atomar via `File.Replace`.
  - Prüft Dateigröße und SHA-256-Hash vor dem Schreiben, um externe Änderungen auf der Festplatte zu erkennen.
- **Automatische Ordner-Backups (`BackupService`)**
  - Sichert vor jedem Speichern den gesamten `savegameN`-Ordner nach `%LOCALAPPDATA%\GothicSaveEditor\Backups\<Mod>\<Slot>_<Timestamp>\` inklusive automatischer Rotation.
  - Bricht den Speichervorgang ab, falls das Backup fehlschlägt, sodass kein Spielstand ungeschützt überschrieben wird.
- **Moderne & flüssige WPF-Oberfläche**
  - MaterialDesign 3 mit **Dark Mode** (Standard) und **Light Mode**.
  - `PerMonitorV2`-DPI-Awareness und `Segoe UI Variable` für gestochen scharfe Darstellung auf HiDPI-Displays.
  - Asynchrones Laden/Speichern mit Fortschrittsbalken & Abbrechen-Funktion, 250-ms-Debounce-Suche im Hintergrund und `DataGrid`-Row-Recycling.
- **Mehrsprachigkeit & Zeichensätze**
  - Oberfläche vollständig in **Deutsch**, **Englisch** und **Russisch** umschaltbar.
  - Wählbare Spielstand-Encodings: **Windows-1252** (Westeuropäisch / DE / EN), **Windows-1250** (Mitteleuropäisch / PL) und **Windows-1251** (Kyrillisch / RU).

---

## Voraussetzungen & Build

- [.NET 10 SDK](https://dotnet.microsoft.com/) oder neuer (Windows; Desktop Runtime 10+ zum Ausführen)

```bash
# Solution bauen
dotnet build GothicSaveEditor.sln

# Unit- & Roundtrip-Tests ausführen
dotnet test GothicSaveEditor.sln

# Anwendung starten
dotnet run --project GothicSaveEditor.csproj
```

---

## Projektstruktur

- `GothicSaveEditor` — WPF-Desktop-Anwendung (`net10.0-windows`, `CommunityToolkit.Mvvm`, `MaterialDesignThemes` 5.x, `NLog` 5.x)
- `GothicSaveEditor.Core` — Plattformunabhängige Core-Bibliothek (`BinSafeReader`, `SaveDatWriter`, `BackupService`, `VariablesComparer`, `EncodingHelper`)
- `GothicSaveEditor.Tests` — xUnit-Testsuite (Snapshot-Vergleich, SHA-256-Roundtrip, Fuzz-Tests, Backup-Rotation, Encoding-Tests)

---

## Danksagung

Dieses Projekt basiert auf dem ursprünglichen **GothicSaveEditor**-Fork von **Flycrafting** (Originalprojekt von **RightMAN**).
