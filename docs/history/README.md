# Implementation history

The current checkpoint is [CHECKPOINT.md](../../CHECKPOINT.md). Historical phase notes explain decisions without keeping obsolete runtime packages. References inside them may describe original paths, paths on the author's machine, or retired artifacts; use Git history to find the matching source revision.

- [Optimizer plan](checkpoints/OPTIMIZER_UPDATE_PLAN.md)
- [Optimizer checkpoint](checkpoints/OPTIMIZER_UPDATE_CHECKPOINT.md)
- [Phase 2 checkpoint](checkpoints/PHASE_2_CHECKPOINT.md)
- [Phase 3 checkpoint](checkpoints/PHASE_3_CHECKPOINT.md)
- [Phase 4 checkpoint](checkpoints/PHASE_4_CHECKPOINT.md)
- [Historical release notes](RELEASE_NOTES.md)

Automated tests, package hashes, scaled WPF renders, paused reads, and unpaused changing-board observations are separate evidence. A later build does not retroactively complete an interrupted audit. Current acceptance is recorded in [release audit](../audit/RELEASE_2_0_1.md).

Use `git log -- <path>` and `git show <commit>:<path>` to trace changes. Old GitHub release binaries can be retired while commits and tags remain available for code inspection.
