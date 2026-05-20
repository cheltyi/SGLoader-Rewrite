# Patch Validator

A small maintainer tool for the SGLoader patch safety catalog. Drag patch `.dll`
files onto it, press **Approve all** or **Reject all**, and it pushes the verdicts
straight to the `validated.json` / `rejected.json` files in the patch repository on
GitHub.

It is a standalone Qt/C++ app — not part of the launcher build.

## What it does

- Computes each patch's **SHA-256** (raw file bytes, lowercase hex) — the exact hash
  SGLoader's `PatchAssessor` uses.
- Accepts **many files at once** — drop a whole folder of patches into the table.
- **Checks existence before adding**: both lists are fetched once, and a patch whose
  hash is already in the target list is skipped (shown as `already approved/rejected`).
- **Approve**: adds new hashes to `validated.json` and removes them from `rejected.json`.
- **Reject**: the opposite. Both files are committed so the two lists never disagree
  (the loader treats a rejected hash as rejected even if also listed as approved).
- The whole batch is one commit per changed file.

## Build

Requires Qt 6 (or Qt 5) with the Widgets and Network modules, plus CMake.

```sh
cd tools/PatchValidator
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
./build/PatchValidator
```

On Arch: `sudo pacman -S qt6-base cmake` covers the dependencies.

## Usage

1. **Token** — paste a GitHub Personal Access Token:
   - Fine-grained PAT: grant **Contents: Read and write** on the `patch-validation` repo.
   - Or a classic PAT with the `repo` scope.
2. **Repository** — defaults to `AZERBAIJAN-TECH/patch-validation`. **Branch** — `main`.
3. Drag patch `.dll` files onto the window (or use **Add files...**). Each appears as a
   row with its name, file, SHA-256 and a status column. Select rows and press
   **Delete** to remove them from the list (**Clear list** removes everything).
4. Edit the **Patch name** cell if needed — it is the JSON key the hash is grouped under.
5. Press **Approve all** or **Reject all**. The status column fills in per patch
   (`approved`, `already approved - skipped`, `approved (moved from rejected)`, ...).

Settings (token, repo, branch) are remembered between runs via `QSettings`. The token
is stored in plain text in the local settings store — treat it like any other saved
credential.

## File format produced

```json
{
  "ExamplePatch": ["e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"],
  "AnotherPatch": ["..."]
}
```

The patch name is only a human-readable grouping; the launcher matches purely on the
hashes.
