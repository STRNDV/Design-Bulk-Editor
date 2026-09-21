# AI disclosure

**This project was built with heavy AI assistance (Claude, Anthropic).** Most
of the code, tests, and documentation in this repository were written by an
AI coding agent working with the maintainer, not typed by hand line-by-line.

We're saying this plainly and up front because plugins in the FFXIV modding
community run inside the game process, and this community rightly treats
AI-generated code with extra scrutiny. You should know what you're looking
at before you trust it with your Glamourer design library or load it into
your game.

## What that means in practice

- **Review the code yourself, or have someone you trust review it**,
  especially [`DesignRepository.cs`](src/DesignBulkEditor.Core/Services/DesignRepository.cs)
  (the part that writes to your actual design files) and
  [`GlamourerApiClient.cs`](src/DesignBulkEditor.Plugin/GlamourerApiClient.cs)
  (the part that talks to Glamourer's live IPC). These are the two places a
  bug could actually cost you data or misbehave in-game.
- **Automated tests exist and are meant to be trusted more than the prose.**
  The [`tests/`](tests/) folder covers the file-writing, backup, and
  character-assignment logic with real, runnable xUnit tests - see the CI
  badge in the [README](README.md) for the current status. Passing tests are
  a stronger claim than any comment or commit message.
- **Every write to a design file is preceded by an automatic backup** (see
  `BackupService.cs`) specifically because AI-assisted code, like
  human-written code, can have bugs, and we did not want a bug in this
  project to be able to destroy your design library.
- **The Glamourer.Api integration is built against the actual, official,
  versioned NuGet package** (`Glamourer.Api`, MIT-licensed, maintained by
  Ottermandias) rather than guessed or reverse-engineered IPC calls. That
  part of the integration is not "vibecoded" - it's the same interface any
  other Dalamud plugin author would use.
- **`automation.json` support is best-effort and explicitly documented as
  such.** It's Glamourer's own undocumented internal file format; parsing of
  it is deliberately defensive (a version mismatch or malformed entry is
  silently ignored, never crashes anything) and is only ever used to
  *suggest* a character assignment, never to apply one automatically.

## What we'd ask of you

If you find a bug, a bad assumption, or something that looks like it was
generated without enough thought - please open an issue. This project is
meant to hold up to real scrutiny, not just look plausible.
