vs_5_0
.input
.param POSITION.xyz, v0.xyz, float
.param NORMAL.xyz, v1.xyz, float
.param TANGENT.xyzw, v2.xyzw, float
.param TEXCOORD.xyzw, v3.xyzw, float
.output
.param SV_POSITION.xyzw, o0.xyzw, float, POS
.param INTERP.xyzw, o1.xyzw, float
.param INTERP1.xyzw, o2.xyzw, float
.param INTERP2.xyz, o3.xyz, float
.param INTERP3.xyz, o4.xyz, float
.text
dcl_globalFlags refactoringAllowed
dcl_constantBuffer cb0[56], immediateIndexed
dcl_constantBuffer cb1[7], immediateIndexed
dcl_constantBuffer cb2[20], immediateIndexed
dcl_input v0.xyz
dcl_input v1.xyz
dcl_input v2.xyzw
dcl_input v3.xyzw
dcl_output_siv o0.xyzw, position
dcl_output o1.xyzw
dcl_output o2.xyzw
dcl_output o3.xyz
dcl_output o4.xyz
dcl_temps 5
mov r0.x, cb1[0].x
mov r0.y, cb1[1].x
mov r0.z, cb1[2].x
add r1.xyw, -cb0[44].xyxz, cb1[3].xyxz
mov r0.w, r1.x
mov r2.xyz, v0.xyzx
mov r2.w, l(1.00000000e+00)
dp4 r0.x, r0.xyzw, r2.xyzw
eq r0.w, cb0[55].w, l(0.00000000e+00)
movc r3.x, r0.w, -r0.x, cb0[0].z
mov r4.w, r1.y
mov r4.x, cb1[0].y
mov r4.y, cb1[1].y
mov r4.z, cb1[2].y
dp4 r0.y, r4.xyzw, r2.xyzw
movc r3.y, r0.w, -r0.y, cb0[1].z
mov r1.x, cb1[0].z
mov r1.y, cb1[1].z
mov r1.z, cb1[2].z
dp4 r0.z, r1.xyzw, r2.xyzw
movc r3.z, r0.w, -r0.z, cb0[2].z
dp3 r0.w, r3.xyzx, r3.xyzx
rsq r0.w, r0.w
mul r1.xyz, r0.w, r3.xyzx
mul r1.xyz, r1.xyzx, cb2[19].x
eq r2.xy, cb2[18].z, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00)
and r1.xyz, r1.xyzx, r2.x
add r0.xyz, r0.xyzx, r1.xyzx
mul r1.xyzw, r0.y, cb0[21].xyzw
mad r1.xyzw, cb0[20].xyzw, r0.x, r1.xyzw
mad r1.xyzw, cb0[22].xyzw, r0.z, r1.xyzw
mov o3.xyz, r0.xyzx
add r0.xyzw, r1.xyzw, cb0[23].xyzw
add r1.x, r0.z, -cb2[18].w
movc o0.z, r2.y, r1.x, r0.z
mov o0.xyw, r0.xyxw
mul r0.xyz, v2.y, cb1[1].xyzx
mad r0.xyz, cb1[0].xyzx, v2.x, r0.xyzx
mad r0.xyz, cb1[2].xyzx, v2.z, r0.xyzx
dp3 r0.w, r0.xyzx, r0.xyzx
max r0.w, r0.w, l(1.17549435e-38)
rsq r0.w, r0.w
mul o1.xyz, r0.w, r0.xyzx
mov o1.w, v2.w
mov o2.xyzw, v3.xyzw
dp3 r0.x, v1.xyzx, cb1[4].xyzx
dp3 r0.y, v1.xyzx, cb1[5].xyzx
dp3 r0.z, v1.xyzx, cb1[6].xyzx
dp3 r0.w, r0.xyzx, r0.xyzx
max r0.w, r0.w, l(1.17549435e-38)
rsq r0.w, r0.w
mul o4.xyz, r0.w, r0.xyzx
ret
