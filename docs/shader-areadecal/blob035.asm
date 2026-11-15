vs_5_0
.input
.param POSITION.xyz, v0.xyz, float
.param NORMAL.xyz, v1.xyz, float
.param TANGENT.xyzw, v2.xyzw, float
.param TEXCOORD.xyzw, v3.xyzw, float
.param SV_InstanceID.x, v4.x, uint, INSTID
.output
.param SV_POSITION.xyzw, o0.xyzw, float, POS
.param INTERP.xyzw, o1.xyzw, float
.param INTERP1.xyzw, o2.xyzw, float
.param INTERP2.xyz, o3.xyz, float
.param INTERP3.xyz, o4.xyz, float
.param CUSTOM_INSTANCE_ID.x, o5.x, uint
.text
dcl_globalFlags refactoringAllowed
dcl_constantBuffer cb0[56], immediateIndexed
dcl_constantBuffer cb1[4], immediateIndexed
dcl_constantBuffer cb2[1], immediateIndexed
dcl_constantBuffer cb3[20], immediateIndexed
dcl_resource_structured t0, 112
dcl_resource_structured t1, 4
dcl_input v0.xyz
dcl_input v1.xyz
dcl_input v2.xyzw
dcl_input v3.xyzw
dcl_input_sgv v4.x, instance_id
dcl_output_siv o0.xyzw, position
dcl_output o1.xyzw
dcl_output o2.xyzw
dcl_output o3.xyz
dcl_output o4.xyz
dcl_output o5.x
dcl_temps 5
mov r0.x, cb1[0].x
mov r0.y, cb1[1].x
mov r0.z, cb1[2].x
add r1.xyw, -cb0[44].xyxz, cb1[3].xyxz
mov r0.w, r1.x
mov r2.xyz, v0.xyzx
mov r2.w, l(1.00000000e+00)
dp4 r0.x, r0.xyzw, r2.xyzw
eq r0.y, cb0[55].w, l(0.00000000e+00)
movc r3.x, r0.y, -r0.x, cb0[0].z
mov r4.w, r1.y
mov r4.x, cb1[0].y
mov r4.y, cb1[1].y
mov r4.z, cb1[2].y
dp4 r0.x, r4.xyzw, r2.xyzw
movc r3.y, r0.y, -r0.x, cb0[1].z
mov r1.x, cb1[0].z
mov r1.y, cb1[1].z
mov r1.z, cb1[2].z
dp4 r0.x, r1.xyzw, r2.xyzw
movc r3.z, r0.y, -r0.x, cb0[2].z
dp3 r0.x, r3.xyzx, r3.xyzx
rsq r0.x, r0.x
mul r0.xyz, r0.x, r3.xyzx
mul r0.xyz, r0.xyzx, cb3[19].x
eq r1.xy, cb3[18].z, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00)
and r0.xyz, r0.xyzx, r1.x
iadd r0.w, v4.x, cb2[0].x
ld_structured_indexable(structured_buffer, stride=4)(mixed,mixed,mixed,mixed) r0.w, r0.w, l(0), t1.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.xy, r0.w, l(20), t0.xyxx
mov r3.w, r2.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r3.x, r0.w, l(0), t0.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r3.yz, r0.w, l(8), t0.xxyx
add r1.xz, -r3.xxyx, r3.zzwz
mad r1.xz, v0.x, r1.xxzx, r3.xxyx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.z, r0.w, l(32), t0.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.xw, r0.w, l(84), t0.xxxy
add r2.yz, -r1.xxzx, r2.yyzy
mad r3.xy, v0.z, r2.yzyy, r1.xzxx
mov r3.zw, -cb0[44].xxxz
dp2 r4.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r3.xzxx
dp2 r4.z, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r3.ywyy
add r0.w, -r2.x, r2.w
mad r2.x, v0.y, r0.w, r2.x
mov r2.y, -cb0[44].y
dp2 r4.y, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r2.xyxx
add r0.xyz, r0.xyzx, r4.xyzx
mul r2.xyzw, r0.y, cb0[21].xyzw
mad r2.xyzw, cb0[20].xyzw, r0.x, r2.xyzw
mad r2.xyzw, cb0[22].xyzw, r0.z, r2.xyzw
mov o3.xyz, r0.xyzx
add r0.xyzw, r2.xyzw, cb0[23].xyzw
add r1.x, r0.z, -cb3[18].w
movc o0.z, r1.y, r1.x, r0.z
mov o0.xyw, r0.xyxw
dp3 r0.x, v2.xyzx, v2.xyzx
max r0.x, r0.x, l(1.17549435e-38)
rsq r0.x, r0.x
mul o1.xyz, r0.x, v2.xyzx
mov o1.w, v2.w
mov o2.xyzw, v3.xyzw
dp3 r0.x, v1.xyzx, v1.xyzx
max r0.x, r0.x, l(1.17549435e-38)
rsq r0.x, r0.x
mul o4.xyz, r0.x, v1.xyzx
mov o5.x, v4.x
ret
