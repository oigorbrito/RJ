# Synthetic materialization execution protocol

Frozen source: 08ed1661f7c9375f4d52cd2c9e67e789e82a2d59.

The current full-suite CI does not by itself execute the Wave J/K canonical CLI self-tests. Add two independent jobs to the existing ci workflow and run the existing scripts with PowerShell on the hosted Linux runner. Check out the literal PR head and print it before execution. Preserve each gate's hashes, materialization assertions and temporary-fixture cleanup.

Acceptance requires actual job steps, zero exit status and the respective WAVE_J_GATE=PASS / WAVE_K_GATE=PASS marker on the same head. A workflow present, a skipped job or an earlier PASS is insufficient. Any failure must be inspected and corrected before merge.

Scope is synthetic generation/retrieval observation materialization mechanics. It does not admit a real corpus, validate live providers, promote treatments, qualify Windows execution or close the project. No external provider credentials are configured. The repository steward remains read/report only.
