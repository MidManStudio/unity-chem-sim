# com.midmanstudio.questly

## Overview

Generic quest engine on top of the DixScript (`.mdix`) database. Quests (objectives, prerequisites,
rewards) are declared in `.mdix`, loaded through `MdixQuestTable` or from a baked `QuestDataAsset`,
and driven at run time by `QuestRuntime`. The package is headless: it has no run-time UI. The editor
tooling is the Questly Studio window.

Only the files touched so far are covered here. The rest of the package is not yet documented.

## Modules

### `QuestlyStudioWindow.cs`
**What it does:** Editor window, opened from `Window > MDIX > Questly Studio`. It has three tabs.
Overview lists every `MdixAsset` in the project and jumps to Validate or Bake for the one picked.
Validate loads a `.mdix` quest database and lists the problems found by `QuestlySchema.Validate`.
Bake fills a `QuestDataAsset` from a quest database, for builds that skip the native mdix library.

**Decisions:**
- Validation keeps the `List<MdixValidationError>` that `QuestlySchema.Validate` returns. It does not
  call `MdixDatabase.Validate`, which takes an `IMdixSchemaSource`. `QuestlySchema` is not one: it
  combines a structural schema with a check of the loaded quest graph, and a `MdixValidationReport`
  cannot be built outside Mdix.Core.
- A problem from the quest graph check has `Expected` set to "no error" and the text in `Actual`, so
  the window shows `Actual` alone for those. Other problems show "expected X, got Y".
- `Missing` is drawn as an error and the other kinds as warnings.
- Bake with no target asset creates `<source name>_Baked.asset` beside the source file, using
  `AssetDatabase.GenerateUniqueAssetPath`, so an earlier bake is never overwritten.

**Tests:** none. The `packages-test` harness in the GrandTheftGrimoire repository compiles the
`Runtime` folder only, so this window is not compiled outside Unity.

## CI and Workflows

No workflow in this repository builds or tests this package.

## Fixes and Problems

### `QuestlyStudioWindow.cs`
**Problem:** The first Unity compile of the package reported two errors in this file. `error.Message`
does not exist, because `MdixValidationError` has `Path`, `Expected`, `Actual` and `Kind` (CS1061).
`db.Validate(new QuestlySchema())` does not convert, because `QuestlySchema` does not implement
`IMdixSchemaSource` (CS1503).

**Fix:** The window now stores the list that `new QuestlySchema().Validate(db)` returns, counts and
loops over that list, and builds the text from `Expected` and `Actual`. Nothing compiled this file
outside Unity before, so both errors went unseen until the first Unity run.
