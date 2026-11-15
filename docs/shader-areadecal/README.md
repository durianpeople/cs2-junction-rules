# AreaDecalShader disassembly (JR-T-003c, 15 Nov 2026)
Extracted from the game's bundles by the JR-T-003c coder to check the paint suspects.
- blob035.asm: vertex shader, DBufferMesh pass (builds the prism from triangle XZ + YMinMax)
- blob054.asm: pixel shader, DBufferMesh pass (decal-layer discard, infoview alpha, LOD fade)
- areadecal.shader.txt / areashader.shader.txt: shader dumps; mats.txt: material list
Kept locally for reference only (game-derived; this repo is never pushed).
- blob044.params.txt: shader parameter blob 44 (constant-buffer layout: UnityPerMaterial offsets, e.g. colossal_EdgeNoise
  at byte 196 = cb4[12].yz, colossal_AreaParameters at 272 = cb4[17]). JR-T-003d used it to read blob054's
  `1 / (EdgeNoise.y * expand)` at lines 208-210: size 0 makes every pixel's alpha 0 (PROBE-STAGE3-PAINT.md section 13).
