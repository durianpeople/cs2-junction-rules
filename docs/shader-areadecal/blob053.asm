ps_5_0
.input
.param SV_POSITION.xyzw, v0.xy, float, POS
.param INTERP.xyzw, v1, float
.param INTERP1.xyzw, v2, float
.param INTERP2.xyz, v3, float
.param INTERP3.xyz, v4, float
.param CUSTOM_INSTANCE_ID.x, v5.x, uint
.output
.param SV_Target.xyzw, o0.xyzw, float, TARGET
.param SV_Target1.xyzw, o1.xyzw, float, TARGET
.param SV_Target2.xyzw, o2.xyzw, float, TARGET
.param SV_Target3.xy, o3.xy, float, TARGET
.text
dcl_globalFlags refactoringAllowed
dcl_constantBuffer cb0[110], immediateIndexed
dcl_constantBuffer cb1[1], immediateIndexed
dcl_constantBuffer cb2[19], immediateIndexed
dcl_resource_texture2darray (float,float,float,float) t0
dcl_resource_structured t1, 112
dcl_resource_structured t2, 4
dcl_resource_texture2darray (float,float,float,float) t3
dcl_input_ps_siv linear noperspective v0.xy, position
dcl_input_ps constant v5.x
dcl_output o0.xyzw
dcl_output o1.xyzw
dcl_output o2.xyzw
dcl_output o3.xy
dcl_temps 6
iadd r0.x, v5.x, cb1[0].x
ftou r1.xy, v0.xyxx
mov r1.zw, l(0, 0, 0, 0)
ld_indexable(texture2darray) r0.y, r1.xyww, t0.yxzw
mul r0.zw, v0.xxxy, cb0[46].zzzw
mad r0.zw, r0.zzzw, l(0.00000000e+00, 0.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(0.00000000e+00, 0.00000000e+00, -1.00000000e+00, -1.00000000e+00)
mul r2.xyz, -r0.w, cb0[29].xzwx
mad r2.xyz, cb0[28].xzwx, r0.z, r2.xyzx
mad r0.yzw, cb0[30].xxzw, r0.y, r2.xxyz
add r0.yzw, r0.yyzw, cb0[31].xxzw
div r2.xy, r0.yzyy, r0.w
if_nz cb0[109].w
    ld_indexable(texture2darray) r0.y, r1.xyzw, t3.xzyw
    mul r0.y, r0.y, l(2.55500000e+02)
    ftou r0.y, r0.y
    movc r0.y, r0.y, r0.y, l(1)
    and r0.y, r0.y, cb2[18].x
    movc r0.y, r0.y, l(1.00000000e+00), l(-1.00000000e+00)
else
    mov r0.y, l(1.00000000e+00)
endif
lt r0.y, r0.y, l(0.00000000e+00)
discard_nz r0.y
mov r2.zw, cb0[44].xxxz
dp2 r1.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r2.xzxx
dp2 r1.y, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r2.ywyy
ld_structured_indexable(structured_buffer, stride=4)(mixed,mixed,mixed,mixed) r0.x, r0.x, l(0), t2.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.x, r0.x, l(0), t1.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r2.yz, r0.x, l(8), t1.xxyx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r3.xy, r0.x, l(20), t1.xyxx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r3.z, r0.x, l(32), t1.x
mov r2.w, r3.x
add r0.xy, -r2.xyxx, r2.zwzz
dp2 r0.z, r0.xyxx, r0.xyxx
rsq r0.z, r0.z
mul r0.xy, r0.z, r0.xyxx
add r1.zw, -r2.zzzw, r3.yyyz
dp2 r0.w, r1.zwzz, r1.zwzz
rsq r0.w, r0.w
mul r4.xy, r0.w, r1.zwzz
add r1.zw, r2.xxxy, -r3.yyyz
dp2 r0.w, r1.zwzz, r1.zwzz
rsq r0.w, r0.w
mul r5.xy, r0.w, r1.zwzz
add r1.zw, r1.xxxy, -r2.xxxy
mov r0.z, -r0.y
dp2 r0.x, r1.wzww, r0.xzxx
add r0.yz, r1.xxyx, -r2.zzwz
mov r4.z, -r4.y
dp2 r0.y, r0.zyzz, r4.xzxx
add r0.zw, r1.xxxy, -r3.yyyz
mov r5.z, -r5.y
dp2 r0.z, r0.wzww, r5.xzxx
min r0.y, r0.z, r0.y
min r0.x, r0.y, r0.x
lt r0.x, r0.x, l(0.00000000e+00)
discard_nz r0.x
mov o0.xyzw, l(0, 0, 0, 0)
mov o1.xyzw, l(5.00000000e-01, 5.00000000e-01, 5.00000000e-01, 0)
mov o2.xyzw, l(0, 0, 0, 0)
mov o3.xy, l(0, 0, 0, 0)
ret
