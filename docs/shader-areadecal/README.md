# AreaDecalShader disassembly (JR-T-003c, 15 Nov 2026)
Extracted from the game's bundles by the JR-T-003c coder to check the paint suspects.
- blob035.asm: vertex shader, DBufferMesh pass (builds the prism from triangle XZ + YMinMax)
- blob054.asm: pixel shader, DBufferMesh pass (decal-layer discard, infoview alpha, LOD fade)
- areadecal.shader.txt / areashader.shader.txt: shader dumps; mats.txt: material list
Kept locally for reference only (game-derived; this repo is never pushed).
