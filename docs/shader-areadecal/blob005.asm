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
dcl_constantBuffer cb0[45], immediateIndexed
dcl_constantBuffer cb1[1], immediateIndexed
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
dcl_temps 3
iadd r0.x, v4.x, cb1[0].x
ld_structured_indexable(structured_buffer, stride=4)(mixed,mixed,mixed,mixed) r0.x, r0.x, l(0), t1.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r1.xy, r0.x, l(20), t0.xyxx
mov r2.w, r1.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.x, r0.x, l(0), t0.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.yz, r0.x, l(8), t0.xxyx
add r0.yz, -r2.xxyx, r2.zzwz
mad r0.yz, v0.x, r0.yyzy, r2.xxyx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r1.z, r0.x, l(32), t0.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r0.xw, r0.x, l(84), t0.xxxy
add r1.xy, -r0.yzyy, r1.yzyy
mad r1.xy, v0.z, r1.xyxx, r0.yzyy
mov r1.zw, -cb0[44].xxxz
dp2 r2.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r1.xzxx
dp2 r2.z, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r1.ywyy
add r0.y, -r0.x, r0.w
mad r0.x, v0.y, r0.y, r0.x
mov r0.y, -cb0[44].y
dp2 r2.y, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r0.xyxx
mul r0.xyzw, r2.y, cb0[21].xyzw
mov o3.xyz, r2.xyzx
mad r0.xyzw, cb0[20].xyzw, r2.x, r0.xyzw
mad r0.xyzw, cb0[22].xyzw, r2.z, r0.xyzw
add o0.xyzw, r0.xyzw, cb0[23].xyzw
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
