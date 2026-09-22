# Independent Phase 4 acceptance probes

Run from the repository root:

```powershell
dotnet run --project tools/YfmCompanion.Phase4Audit -c Release -- . docs/audit/phase4-acceptance-results.json
```

Exit 0 means all implemented probes pass. Exit 1 means one or more acceptance requirements fail; inspect the result JSON. Unexpected probe exceptions are recorded as failures. An incorrect invocation exits 2. The tool is intentionally outside the normal solution test suite: the current 165-test regression suite and these independent acceptance checks are different evidence. The JSON retains commit `974cce4` as the historical pre-repair baseline while hashing the production source actually tested.

Fixtures use synthetic cards and synthetic in-memory save images. Mutation probes write copies of research JSON into a fresh temporary directory. They do not edit the source data, user saves, game files, or runtime application code. Temporary mutation fixtures remain available for inspection under the system temporary folder. The requested JSON output file is replaced on each run.

The output identifies the original audit baseline and hashes the actual current production source, so a later rerun can be distinguished from the initial evidence. It contains no user card collection or save path. These probes are a regression set for concrete audit findings, not a comprehensive gameplay simulator or a replacement for the full acceptance matrix in `docs/audit/PHASE4_FINAL_AUDIT.md`.
