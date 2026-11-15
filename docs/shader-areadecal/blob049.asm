ps_5_0
dcl_globalFlags refactoringAllowed
dcl_immediateConstantBuffer {
    {0x3f800000, 0x00000000, 0x00000000, 0x00000000},
    {0x00000000, 0x3f800000, 0x00000000, 0x00000000},
    {0x00000000, 0x00000000, 0x3f800000, 0x00000000},
    {0x00000000, 0x00000000, 0x00000000, 0x3f800000},
}
dcl_constantBuffer cb0[25], immediateIndexed
dcl_constantBuffer cb1[110], immediateIndexed
dcl_constantBuffer cb2[10], immediateIndexed
dcl_constantBuffer cb3[1], immediateIndexed
dcl_constantBuffer cb4[19], immediateIndexed
dcl_constantBuffer cb5[405], immediateIndexed
dcl_sampler s0
dcl_sampler s1
dcl_sampler s2
dcl_sampler s3
dcl_resource_structured t0, 128
dcl_resource_texture2darray (float,float,float,float) t1
dcl_resource_texture2d (float,float,float,float) t2
dcl_resource_texture2darray (float,float,float,float) t3
dcl_resource_texture2darray (float,float,float,float) t4
dcl_resource_texture2darray (float,float,float,float) t5
dcl_resource_texture2d (float,float,float,float) t6
dcl_resource_texture2d (float,float,float,float) t7
dcl_resource_structured t8, 112
dcl_resource_structured t9, 12
dcl_resource_structured t10, 4
dcl_resource_texture2d (float,float,float,float) t11
dcl_resource_texture2darray (float,float,float,float) t12
dcl_resource_texture2darray (float,float,float,float) t13
dcl_input_ps_siv linear noperspective v0.xy, position
dcl_input_ps linear v1.xyzw
dcl_input_ps linear v4.xyz
dcl_input_ps constant v5.x
dcl_output o0.xyzw
dcl_output o1.xyzw
dcl_output o2.xyzw
dcl_output o3.xy
dcl_temps 24
iadd r0.x, v5.x, cb3[0].x
dp3 r0.y, v4.xyzx, v4.xyzx
sqrt r0.y, r0.y
max r0.y, r0.y, l(1.17549435e-38)
div r0.y, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00), r0.y
lt r0.z, l(0.00000000e+00), v1.w
ge r0.w, cb2[9].w, l(0.00000000e+00)
movc r0.zw, r0.zzzw, l(0, 0, 1.00000000e+00, 1.00000000e+00), l(0, 0, -1.00000000e+00, -1.00000000e+00)
mul r0.z, r0.w, r0.z
mul r1.xyz, v1.yzxy, v4.zxyz
mad r1.xyz, v4.yzxy, v1.zxyz, -r1.xyzx
mul r1.xyz, r0.z, r1.xyzx
mul r2.xyz, r0.y, v1.xyzx
mul r1.xyz, r0.y, r1.xyzx
mul r0.yzw, r0.y, v4.xxyz
ftou r3.xy, v0.xyxx
mov r3.zw, l(0, 0, 0, 0)
ld_indexable(texture2darray) r1.w, r3.xyww, t1.yzwx
mul r4.xy, v0.xyxx, cb1[46].zwzz
mad r4.xy, r4.xyxx, l(2.00000000e+00, 2.00000000e+00, 0.00000000e+00, 0.00000000e+00), l(-1.00000000e+00, -1.00000000e+00, 0.00000000e+00, 0.00000000e+00)
mul r5.xyzw, -r4.y, cb1[29].xyzw
mad r4.xyzw, cb1[28].xyzw, r4.x, r5.xyzw
mad r4.xyzw, cb1[30].xyzw, r1.w, r4.xyzw
add r4.xyzw, r4.xyzw, cb1[31].xyzw
div r4.xyz, r4.yxzy, r4.w
if_nz cb1[109].w
    ld_indexable(texture2darray) r1.w, r3.xyzw, t13.xywz
    mul r1.w, r1.w, l(2.55500000e+02)
    ftou r1.w, r1.w
    movc r1.w, r1.w, r1.w, l(1)
    and r1.w, r1.w, cb4[18].x
    movc r1.w, r1.w, l(1.00000000e+00), l(-1.00000000e+00)
else
    mov r1.w, l(1.00000000e+00)
endif
lt r1.w, r1.w, l(0.00000000e+00)
discard_nz r1.w
mov r4.w, cb1[44].x
dp2 r3.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r4.ywyy
mov r4.yw, cb1[44].yyyz
dp2 r4.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r4.xyxx
dp2 r3.y, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r4.zwzz
dp3 r1.w, r0.yzwy, r0.yzwy
rsq r1.w, r1.w
mul r5.xyz, r0.wyzw, r1.w
lt r1.w, cb1[54].x, l(0.00000000e+00)
add r2.w, -v0.y, cb1[56].y
movc r6.y, r1.w, r2.w, v0.y
mov r6.x, v0.x
div r6.xy, r6.xyxx, cb1[56].xyxx
ld_structured_indexable(structured_buffer, stride=4)(mixed,mixed,mixed,mixed) r0.x, r0.x, l(0), t10.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r7.x, r0.x, l(0), t8.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r7.yz, r0.x, l(8), t8.xyxx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r8.xy, r0.x, l(20), t8.xyxx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r9.xyzw, r0.x, l(32), t8.yzwx
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r10.xyzw, r0.x, l(48), t8.xyzw
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r11.xyzw, r0.x, l(64), t8.xzyw
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r12.x, r0.x, l(80), t8.x
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r13.xyzw, r0.x, l(92), t8.xyzw
ld_structured_indexable(structured_buffer, stride=112)(mixed,mixed,mixed,mixed) r1.w, r0.x, l(108), t8.x
ld_structured_indexable(structured_buffer, stride=12)(mixed,mixed,mixed,mixed) r0.x, r0.x, l(8), t9.x
add r6.z, -r6.y, l(1.00000000e+00)
mul r4.zw, r6.xxxz, cb1[46].xxxy
ftou r14.xy, r4.zwzz
mov r14.zw, l(0, 0, 0, 0)
ld_indexable(texture2darray) r2.w, r14.xyzw, t1.yzwx
mad r4.zw, r6.xxxz, l(0.00000000e+00, 0.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(0.00000000e+00, 0.00000000e+00, -1.00000000e+00, -1.00000000e+00)
mul r6.xyzw, -r4.w, cb1[29].xyzw
mad r6.xyzw, cb1[28].xyzw, r4.z, r6.xyzw
mad r6.xyzw, cb1[30].xyzw, r2.w, r6.xyzw
add r6.xyzw, r6.xyzw, cb1[31].xyzw
div r6.xyz, r6.xyzx, r6.w
add r4.zw, r6.xxxz, cb1[44].xxxz
dp3 r2.w, r6.xyzx, r6.xyzx
sqrt r2.w, r2.w
lt r6.xyz, r2.w, cb0[16].wzyw
ieq r14.xyz, cb0[17].x, l(3, 2, 1, 0)
or r6.xyz, r6.xyzx, r14.xyzx
ge r14.xyz, r4.z, cb0[12].wzyw
and r6.xyz, r6.xyzx, r14.xyzx
ge r14.xyz, cb0[14].wzyw, r4.z
and r6.xyz, r6.xyzx, r14.xyzx
ge r14.xyz, r4.w, cb0[13].wzyw
and r6.xyz, r6.xyzx, r14.xyzx
ge r14.xyz, cb0[15].wzyw, r4.w
and r6.xyz, r6.xyzx, r14.xyzx
and r2.w, r6.z, l(1065353216)
movc r2.w, r6.y, l(2.00000000e+00), r2.w
movc r2.w, r6.x, l(3.00000000e+00), r2.w
ge r6.xyz, r2.w, l(5.00000000e-01, 1.50000000e+00, 2.50000000e+00, 0.00000000e+00)
and r6.xyz, r6.xyzx, l(1065353216, 1065353216, 1065353216, 0)
add r2.w, r6.y, r6.x
add r6.z, r6.z, r2.w
ftoi r2.w, r6.z
dp4 r14.x, cb0[14].xyzw, icb[r2.w + 0].xyzw
dp4 r14.y, cb0[15].xyzw, icb[r2.w + 0].xyzw
dp4 r15.x, cb0[12].xyzw, icb[r2.w + 0].xyzw
dp4 r15.y, cb0[13].xyzw, icb[r2.w + 0].xyzw
add r14.xy, r14.xyxx, -r15.xyxx
add r4.zw, r4.zzzw, -r15.xxxy
div r4.zw, r4.zzzw, r14.xxxy
dp4 r2.w, cb0[19].xyzw, icb[r2.w + 0].xyzw
div r14.xy, r2.w, r14.xyxx
mov r15.x, -r14.x
mov r15.z, l(0)
add r6.xy, r4.zwzz, r15.xzxx
sample_l_indexable(texture2darray) r5.w, r6.xyzx, t12.yzwx, s3, l(0.00000000e+00)
mov r14.z, l(0)
add r6.xy, r4.zwzz, r14.xzxx
sample_l_indexable(texture2darray) r6.w, r6.xyzx, t12.yzwx, s3, l(0.00000000e+00)
mov r14.w, -r14.y
add r6.xy, r4.zwzz, r14.zwzz
sample_l_indexable(texture2darray) r8.w, r6.xyzx, t12.yzwx, s3, l(0.00000000e+00)
add r6.xy, r4.zwzz, r14.zyzz
sample_l_indexable(texture2darray) r4.z, r6.xyzx, t12.yzxw, s3, l(0.00000000e+00)
add r2.w, r2.w, r2.w
div r6.y, r2.w, cb0[18].x
add r6.x, r5.w, -r6.w
add r6.z, -r4.z, r8.w
dp3 r2.w, r6.xyzx, r6.xyzx
rsq r2.w, r2.w
mul r14.xyz, r2.w, r6.xyzx
add r4.zw, -r3.xxxy, r13.xxxy
mul r4.zw, r4.zzzw, cb4[10].z
mov r15.yz, -r13.wwzw
mul r13.xy, r13.wzww, l(1.00000000e+00, -1.00000000e+00, 0.00000000e+00, 0.00000000e+00)
dp2 r13.x, r13.xyxx, r4.zwzz
dp2 r13.y, r13.zwzz, r4.zwzz
mov r7.w, r8.x
add r4.zw, -r7.xxxz, r7.yyyw
dp2 r5.w, r4.zwzz, r4.zwzz
rsq r5.w, r5.w
mul r16.xy, r4.zwzz, r5.w
mov r8.z, r9.w
add r4.zw, -r7.yyyw, r8.yyyz
dp2 r5.w, r4.zwzz, r4.zwzz
rsq r5.w, r5.w
mul r17.xy, r4.zwzz, r5.w
add r4.zw, r7.xxxz, -r8.yyyz
dp2 r5.w, r4.zwzz, r4.zwzz
rsq r5.w, r5.w
mul r18.xy, r4.zwzz, r5.w
add r4.zw, r3.xxxy, -r7.xxxz
mov r16.z, -r16.y
dp2 r5.w, r4.wzww, r16.xzxx
add r8.xw, r3.xxxy, -r7.yyyw
mov r17.z, -r17.y
dp2 r6.w, r8.wxww, r17.xzxx
add r13.zw, r3.xxxy, -r8.yyyz
mov r18.z, -r18.y
dp2 r14.w, r13.wzww, r18.xzxx
min r6.w, r6.w, r14.w
min r5.w, r5.w, r6.w
mul r6.w, cb4[12].z, cb4[17].x
div r6.w, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00), r6.w
mul r16.xy, r3.yxyy, r6.w
round_ni r17.xyzw, r16.xxyy
frc r16.xy, r16.xyxx
mul r18.xyzw, r17.wywy, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r18.xyzw, r18.xyzw, -r18.zwzw
movc r18.xyzw, r18.xyzw, l(2.89000000e+02, 2.89000000e+02, 3.46020772e-03, 3.46020772e-03), l(-2.89000000e+02, -2.89000000e+02, -3.46020772e-03, -3.46020772e-03)
mul r19.xyzw, r17.yyww, r18.wwzz
frc r19.xyzw, r19.xyzw
mul r18.xyzw, r18.yyxx, r19.xyzw
mad r19.xyzw, r18.wwyy, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r19.xyzw, r18.wwyy, r19.xyzw
mul r20.xyzw, r19.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r20.xyzw, r20.xyzw, -r20.yyww
movc r20.xyzw, r20.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r19.xyzw, r19.xyzw, r20.yyww
frc r19.xyzw, r19.xyzw
mad r18.xyzw, r20.xxzz, r19.xyzw, r18.xyzw
mad r19.xyzw, r18.yyww, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r18.xyzw, r18.xyzw, r19.xyzw
mul r19.xyzw, r18.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r19.xyzw, r19.xyzw, -r19.yyww
movc r19.xyzw, r19.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r18.xyzw, r18.xyzw, r19.yyww
frc r18.xyzw, r18.xyzw
mul r18.xyzw, r18.xyzw, r19.xxzz
mul r18.xyzw, r18.xyzw, l(2.43902430e-02, 2.43902430e-02, 2.43902430e-02, 2.43902430e-02)
frc r18.xyzw, r18.xyzw
mad r18.xyzw, r18.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(-1.00000000e+00, -5.00000000e-01, -1.00000000e+00, -5.00000000e-01)
round_ni r16.zw, r18.yyyw
add r19.xy, -r16.zwzz, r18.xzxx
add r19.zw, |r18.xxxz|, l(0.00000000e+00, 0.00000000e+00, -5.00000000e-01, -5.00000000e-01)
dp2 r6.w, r19.xzxx, r19.xzxx
rsq r6.w, r6.w
mul r16.zw, r6.w, r19.xxxz
dp2 r6.w, r16.wzww, r16.xyxx
add r18.xyzw, r17.wywy, l(0.00000000e+00, 1.00000000e+00, 1.00000000e+00, 0.00000000e+00)
mul r20.xyzw, r18.xyzw, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r20.xyzw, r20.xyzw, -r20.xyzw
movc r21.xyzw, r20.xyxy, l(2.89000000e+02, 2.89000000e+02, 3.46020772e-03, 3.46020772e-03), l(-2.89000000e+02, -2.89000000e+02, -3.46020772e-03, -3.46020772e-03)
mul r22.xyzw, r18.yyxx, r21.wwzz
frc r22.xyzw, r22.xyzw
mul r21.xyzw, r21.yyxx, r22.xyzw
mad r22.xyzw, r21.wwyy, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r22.xyzw, r21.wwyy, r22.xyzw
mul r23.xyzw, r22.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r23.xyzw, r23.xyzw, -r23.yyww
movc r23.xyzw, r23.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r22.xyzw, r22.xyzw, r23.yyww
frc r22.xyzw, r22.xyzw
mad r21.xyzw, r23.xxzz, r22.xyzw, r21.xyzw
mad r22.xyzw, r21.yyww, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r21.xyzw, r21.xyzw, r22.xyzw
mul r22.xyzw, r21.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r22.xyzw, r22.xyzw, -r22.yyww
movc r22.xyzw, r22.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r21.xyzw, r21.xyzw, r22.yyww
frc r21.xyzw, r21.xyzw
mul r21.xyzw, r21.xyzw, r22.xxzz
mul r21.xyzw, r21.xyzw, l(2.43902430e-02, 2.43902430e-02, 2.43902430e-02, 2.43902430e-02)
frc r21.xyzw, r21.xyzw
mad r21.xyzw, r21.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(-1.00000000e+00, -5.00000000e-01, -1.00000000e+00, -5.00000000e-01)
round_ni r16.zw, r21.yyyw
add r22.xy, -r16.zwzz, r21.xzxx
add r22.zw, |r21.xxxz|, l(0.00000000e+00, 0.00000000e+00, -5.00000000e-01, -5.00000000e-01)
dp2 r14.w, r22.xzxx, r22.xzxx
rsq r14.w, r14.w
mul r16.zw, r14.w, r22.xxxz
add r21.xyzw, r16.yxyx, l(-0.00000000e+00, -1.00000000e+00, -1.00000000e+00, -0.00000000e+00)
dp2 r14.w, r16.zwzz, r21.xyxx
movc r20.xyzw, r20.zwzw, l(2.89000000e+02, 2.89000000e+02, 3.46020772e-03, 3.46020772e-03), l(-2.89000000e+02, -2.89000000e+02, -3.46020772e-03, -3.46020772e-03)
mul r18.xyzw, r18.wwzz, r20.wwzz
frc r18.xyzw, r18.xyzw
mul r18.xyzw, r18.xyzw, r20.yyxx
mad r20.xyzw, r18.wwyy, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r20.xyzw, r18.wwyy, r20.xyzw
mul r23.xyzw, r20.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r23.xyzw, r23.xyzw, -r23.yyww
movc r23.xyzw, r23.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r20.xyzw, r20.xyzw, r23.yyww
frc r20.xyzw, r20.xyzw
mad r18.xyzw, r23.xxzz, r20.xyzw, r18.xyzw
mad r20.xyzw, r18.yyww, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r18.xyzw, r18.xyzw, r20.xyzw
mul r20.xyzw, r18.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r20.xyzw, r20.xyzw, -r20.yyww
movc r20.xyzw, r20.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r18.xyzw, r18.xyzw, r20.yyww
frc r18.xyzw, r18.xyzw
mul r18.xyzw, r18.xyzw, r20.xxzz
mul r18.xyzw, r18.xyzw, l(2.43902430e-02, 2.43902430e-02, 2.43902430e-02, 2.43902430e-02)
frc r18.xyzw, r18.xyzw
mad r18.xyzw, r18.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(-1.00000000e+00, -5.00000000e-01, -1.00000000e+00, -5.00000000e-01)
round_ni r16.zw, r18.yyyw
add r20.xy, -r16.zwzz, r18.xzxx
add r20.zw, |r18.xxxz|, l(0.00000000e+00, 0.00000000e+00, -5.00000000e-01, -5.00000000e-01)
dp2 r15.w, r20.xzxx, r20.xzxx
rsq r15.w, r15.w
mul r16.zw, r15.w, r20.xxxz
dp2 r15.w, r16.zwzz, r21.zwzz
add r17.xyzw, r17.xyzw, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r18.xyzw, r17.wywy, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r18.xyzw, r18.xyzw, -r18.zwzw
movc r18.xyzw, r18.xyzw, l(2.89000000e+02, 2.89000000e+02, 3.46020772e-03, 3.46020772e-03), l(-2.89000000e+02, -2.89000000e+02, -3.46020772e-03, -3.46020772e-03)
mul r17.xyzw, r17.xyzw, r18.wwzz
frc r17.xyzw, r17.xyzw
mul r17.xyzw, r17.xyzw, r18.yyxx
mad r18.xyzw, r17.wwyy, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r18.xyzw, r17.wwyy, r18.xyzw
mul r23.xyzw, r18.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r23.xyzw, r23.xyzw, -r23.yyww
movc r23.xyzw, r23.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r18.xyzw, r18.xyzw, r23.yyww
frc r18.xyzw, r18.xyzw
mad r17.xyzw, r23.xxzz, r18.xyzw, r17.xyzw
mad r18.xyzw, r17.yyww, l(3.40000000e+01, 3.40000000e+01, 3.40000000e+01, 3.40000000e+01), l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
mul r17.xyzw, r17.xyzw, r18.xyzw
mul r18.xyzw, r17.yyww, l(2.89000000e+02, 2.89000000e+02, 2.89000000e+02, 2.89000000e+02)
ge r18.xyzw, r18.xyzw, -r18.yyww
movc r18.xyzw, r18.xyzw, l(2.89000000e+02, 3.46020772e-03, 2.89000000e+02, 3.46020772e-03), l(-2.89000000e+02, -3.46020772e-03, -2.89000000e+02, -3.46020772e-03)
mul r17.xyzw, r17.xyzw, r18.yyww
frc r17.xyzw, r17.xyzw
mul r17.xyzw, r17.xyzw, r18.xxzz
mul r17.xyzw, r17.xyzw, l(2.43902430e-02, 2.43902430e-02, 2.43902430e-02, 2.43902430e-02)
frc r17.xyzw, r17.xyzw
mad r17.xyzw, r17.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(-1.00000000e+00, -5.00000000e-01, -1.00000000e+00, -5.00000000e-01)
round_ni r16.zw, r17.yyyw
add r18.xy, -r16.zwzz, r17.xzxx
add r18.zw, |r17.xxxz|, l(0.00000000e+00, 0.00000000e+00, -5.00000000e-01, -5.00000000e-01)
dp2 r16.z, r18.xzxx, r18.xzxx
rsq r16.z, r16.z
mul r16.zw, r16.z, r18.xxxz
add r17.xy, r16.xyxx, l(-1.00000000e+00, -1.00000000e+00, 0.00000000e+00, 0.00000000e+00)
dp2 r16.z, r16.wzww, r17.xyxx
mul r17.zw, r16.yyyx, r16.yyyx
mul r17.zw, r16.yyyx, r17.zzzw
mad r18.xz, r16.yyxy, l(6.00000000e+00, 0.00000000e+00, 6.00000000e+00, 0.00000000e+00), l(-1.50000000e+01, 0.00000000e+00, -1.50000000e+01, 0.00000000e+00)
mad r18.xz, r16.yyxy, r18.xxzx, l(1.00000000e+01, 0.00000000e+00, 1.00000000e+01, 0.00000000e+00)
mul r17.zw, r17.zzzw, r18.xxxz
add r14.w, -r6.w, r14.w
mad r6.w, r17.w, r14.w, r6.w
add r14.w, -r15.w, r16.z
mad r14.w, r17.w, r14.w, r15.w
add r14.w, -r6.w, r14.w
mad r23.x, r17.z, r14.w, r6.w
dp2 r6.w, r19.ywyy, r19.ywyy
rsq r6.w, r6.w
mul r16.zw, r6.w, r19.yyyw
dp2 r6.w, r16.zwzz, r16.xyxx
dp2 r14.w, r20.ywyy, r20.ywyy
rsq r14.w, r14.w
mul r16.xy, r14.w, r20.ywyy
dp2 r14.w, r16.yxyy, r21.zwzz
dp2 r15.w, r22.ywyy, r22.ywyy
rsq r15.w, r15.w
mul r16.xy, r15.w, r22.ywyy
dp2 r15.w, r16.yxyy, r21.xyxx
dp2 r16.x, r18.ywyy, r18.ywyy
rsq r16.x, r16.x
mul r16.xy, r16.x, r18.ywyy
dp2 r16.x, r16.xyxx, r17.xyxx
add r14.w, -r6.w, r14.w
mad r6.w, r17.z, r14.w, r6.w
add r14.w, -r15.w, r16.x
mad r14.w, r17.z, r14.w, r15.w
add r14.w, -r6.w, r14.w
mad r23.y, r17.w, r14.w, r6.w
mov r9.w, r10.x
add r16.xyzw, r7.xzyw, -r9.xyzw
mov r17.x, r10.w
mov r17.y, r11.x
add r18.xy, -r7.xzxx, r17.xyxx
add r18.zw, r3.xxxy, -r9.xxxy
dp2 r19.x, r16.xyxx, r18.zwzz
dp2 r19.y, r18.xyxx, r4.zwzz
dp2 r20.x, r16.xyxx, r16.xyxx
dp2 r20.y, r18.xyxx, r18.xyxx
max r19.zw, r20.xxxy, l(0.00000000e+00, 0.00000000e+00, 9.99999975e-05, 9.99999975e-05)
div_sat r19.xy, r19.xyxx, r19.zwzz
mad r21.yz, -r16.xxyx, r19.x, r18.zzwz
mad r19.yz, -r18.xxyx, r19.y, r4.zzwz
dp2 r4.z, r21.yzyy, r21.yzyy
sqrt r21.x, r4.z
dp2 r4.z, r19.yzyy, r19.yzyy
sqrt r19.x, r4.z
lt r4.z, r19.x, r21.x
movc r19.xyz, r4.z, r19.xyzx, r21.xyzx
mov r17.z, r9.x
mov r17.w, r7.x
add r20.xzw, r3.x, -r17.zzwx
mov r21.z, r9.y
mov r21.x, r7.z
mov r21.y, r17.y
add r22.xyz, -r21.zxyz, r21.xyzx
add r21.xyz, r3.y, -r21.zxyz
add r17.xyz, -r17.wxzw, r17.zwxz
mul r17.xyz, r17.xyzx, r21.xyzx
mad r17.xyz, r20.xzwx, r22.xyzx, r17.xyzx
lt r20.xzw, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00), r17.xxyz
lt r17.xyz, r17.xyzx, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00)
iadd r17.xyz, -r20.xzwx, r17.xyzx
itof r17.xyz, r17.xyzx
add r4.z, r17.y, r17.x
add r4.z, r17.z, r4.z
lt r4.z, l(2.50000000e+00), |r4.z|
dp2 r4.w, -r16.xyxx, -r16.xyxx
rsq r4.w, r4.w
mul r21.xy, r4.w, -r16.xyxx
rsq r4.w, r20.y
mul r9.xy, r4.w, r18.xyxx
mov r21.z, -r21.y
dp2 r4.w, r21.xyxx, r9.xyxx
max r4.w, r4.w, l(-1.00000000e+00)
min r4.w, r4.w, l(1.00000000e+00)
add r6.w, -|r4.w|, l(1.00000000e+00)
sqrt r6.w, r6.w
mad r10.w, |r4.w|, l(-1.87292993e-02), l(7.42610022e-02)
mad r10.w, r10.w, |r4.w|, l(-2.12114394e-01)
mad r10.w, r10.w, |r4.w|, l(1.57072878e+00)
mul r14.w, r6.w, r10.w
mad r14.w, r14.w, l(-2.00000000e+00), l(3.14159274e+00)
lt r4.w, r4.w, -r4.w
and r4.w, r4.w, r14.w
mad r4.w, r10.w, r6.w, r4.w
mul r4.w, r4.w, l(5.00000000e-01)
sincos r16.x, r18.x, r4.w
div r4.w, r16.x, r18.x
max r4.w, r4.w, l(9.99999975e-05)
div r16.xy, r21.xyxx, r4.w
mad r16.xy, r21.zxzz, r17.x, r16.xyxx
mad r16.xy, r16.xyxx, cb4[17].x, r7.xzxx
add r17.yz, -r3.xxyx, r16.xxyx
dp2 r4.w, r17.yzyy, r21.xyxx
dp2 r6.w, r17.yzyy, r9.xyxx
min r4.w, r4.w, r6.w
lt r4.w, l(0.00000000e+00), r4.w
dp2 r6.w, r17.yzyy, r17.yzyy
sqrt r6.w, r6.w
add r17.x, -r6.w, cb4[17].x
movc r17.xyz, r4.w, r17.xyzx, r19.xyzx
movc r17.xyz, r4.z, r17.xyzx, r19.xyzx
add r4.zw, -r7.yyyw, r11.zzzy
add r9.xy, r3.xyxx, -r9.zwzz
dp2 r16.x, r16.zwzz, r9.xyxx
dp2 r16.y, r4.zwzz, r8.xwxx
dp2 r18.x, r16.zwzz, r16.zwzz
dp2 r18.y, r4.zwzz, r4.zwzz
max r18.xz, r18.xxyx, l(9.99999975e-05, 0.00000000e+00, 9.99999975e-05, 0.00000000e+00)
div_sat r16.xy, r16.xyxx, r18.xzxx
mad r19.yz, -r16.zzwz, r16.x, r9.xxyx
mad r20.yz, -r4.zzwz, r16.y, r8.xxwx
dp2 r6.w, r19.yzyy, r19.yzyy
sqrt r19.x, r6.w
dp2 r6.w, r20.yzyy, r20.yzyy
sqrt r20.x, r6.w
lt r6.w, r20.x, r19.x
movc r18.xzw, r6.w, r20.xxyz, r19.xxyz
mov r7.x, r9.z
mov r7.z, r11.z
add r9.xyz, r3.x, -r7.xyzx
mov r11.z, r9.w
mov r11.x, r7.w
add r19.xyz, -r11.zxyz, r11.xyzx
add r11.xyz, r3.y, -r11.zxyz
add r20.xyz, -r7.yzxy, r7.xyzx
mul r11.xyz, r11.xyzx, r20.xyzx
mad r9.xyz, r9.xyzx, r19.xyzx, r11.xyzx
lt r11.xyz, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00), r9.xyzx
lt r9.xyz, r9.xyzx, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00)
iadd r9.xyz, -r11.xyzx, r9.xyzx
itof r9.xyz, r9.xyzx
add r6.w, r9.y, r9.x
add r6.w, r9.z, r6.w
lt r6.w, l(2.50000000e+00), |r6.w|
dp2 r7.x, -r16.zwzz, -r16.zwzz
rsq r7.x, r7.x
mul r11.xy, r7.x, -r16.zwzz
rsq r7.x, r18.y
mul r4.zw, r4.zzzw, r7.x
mov r11.z, -r11.y
dp2 r7.x, r11.xyxx, r4.zwzz
max r7.x, r7.x, l(-1.00000000e+00)
min r7.x, r7.x, l(1.00000000e+00)
add r7.z, -|r7.x|, l(1.00000000e+00)
sqrt r7.z, r7.z
mad r8.x, |r7.x|, l(-1.87292993e-02), l(7.42610022e-02)
mad r8.x, r8.x, |r7.x|, l(-2.12114394e-01)
mad r8.x, r8.x, |r7.x|, l(1.57072878e+00)
mul r8.w, r7.z, r8.x
mad r8.w, r8.w, l(-2.00000000e+00), l(3.14159274e+00)
lt r7.x, r7.x, -r7.x
and r7.x, r7.x, r8.w
mad r7.x, r8.x, r7.z, r7.x
mul r7.x, r7.x, l(5.00000000e-01)
sincos r7.x, r8.x, r7.x
div r7.x, r7.x, r8.x
max r7.x, r7.x, l(9.99999975e-05)
div r7.xz, r11.xxyx, r7.x
mad r7.xz, r11.zzxz, r9.x, r7.xxzx
mad r7.xy, r7.xzxx, cb4[17].x, r7.ywyy
add r7.yz, -r3.xxyx, r7.xxyx
dp2 r7.w, r7.yzyy, r11.xyxx
dp2 r4.z, r7.yzyy, r4.zwzz
min r4.z, r4.z, r7.w
lt r4.z, l(0.00000000e+00), r4.z
dp2 r4.w, r7.yzyy, r7.yzyy
sqrt r4.w, r4.w
add r7.x, -r4.w, cb4[17].x
movc r7.xyz, r4.z, r7.xyzx, r18.xzwx
movc r7.xyz, r6.w, r7.xyzx, r18.xzwx
add r4.zw, r8.yyyz, -r10.yyyz
mov r12.y, r11.w
add r8.xw, -r8.yyyz, r12.yyyx
add r9.xy, r3.xyxx, -r10.yzyy
dp2 r11.x, r4.zwzz, r9.xyxx
dp2 r11.y, r8.xwxx, r13.zwzz
dp2 r16.x, r4.zwzz, r4.zwzz
dp2 r16.y, r8.xwxx, r8.xwxx
max r9.zw, r16.xxxy, l(0.00000000e+00, 0.00000000e+00, 9.99999975e-05, 9.99999975e-05)
div_sat r9.zw, r11.xxxy, r9.zzzw
mad r9.yz, -r4.zzwz, r9.z, r9.xxyx
mad r11.yz, -r8.xxwx, r9.w, r13.zzwz
dp2 r6.w, r9.yzyy, r9.yzyy
sqrt r9.x, r6.w
dp2 r6.w, r11.yzyy, r11.yzyy
sqrt r11.x, r6.w
lt r6.w, r11.x, r9.x
movc r9.xyz, r6.w, r11.xyzx, r9.xyzx
mov r12.z, r10.y
mov r12.w, r8.y
add r11.xyz, r3.x, -r12.zwyz
mov r10.x, r8.z
mov r10.y, r12.x
add r16.xzw, -r10.zzxy, r10.xxyz
add r10.xyz, r3.y, -r10.zxyz
add r12.xyz, -r12.wyzw, r12.zwyz
mul r10.xyz, r10.xyzx, r12.xyzx
mad r10.xyz, r11.xyzx, r16.xzwx, r10.xyzx
lt r11.xyz, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00), r10.xyzx
lt r10.xyz, r10.xyzx, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 0.00000000e+00)
iadd r10.xyz, -r11.xyzx, r10.xyzx
itof r10.xyz, r10.xyzx
add r6.w, r10.y, r10.x
add r6.w, r10.z, r6.w
lt r6.w, l(2.50000000e+00), |r6.w|
dp2 r7.w, -r4.zwzz, -r4.zwzz
rsq r7.w, r7.w
mul r11.xy, -r4.zwzz, r7.w
rsq r4.z, r16.y
mul r4.zw, r4.z, r8.xxxw
mov r11.z, -r11.y
dp2 r7.w, r11.xyxx, r4.zwzz
max r7.w, r7.w, l(-1.00000000e+00)
min r7.w, r7.w, l(1.00000000e+00)
add r8.x, -|r7.w|, l(1.00000000e+00)
sqrt r8.x, r8.x
mad r8.w, |r7.w|, l(-1.87292993e-02), l(7.42610022e-02)
mad r8.w, r8.w, |r7.w|, l(-2.12114394e-01)
mad r8.w, r8.w, |r7.w|, l(1.57072878e+00)
mul r9.w, r8.x, r8.w
mad r9.w, r9.w, l(-2.00000000e+00), l(3.14159274e+00)
lt r7.w, r7.w, -r7.w
and r7.w, r7.w, r9.w
mad r7.w, r8.w, r8.x, r7.w
mul r7.w, r7.w, l(5.00000000e-01)
sincos r8.x, r12.x, r7.w
div r7.w, r8.x, r12.x
max r7.w, r7.w, l(9.99999975e-05)
div r8.xw, r11.xxxy, r7.w
mad r8.xw, r11.zzzx, r10.x, r8.xxxw
mad r8.xy, r8.xwxx, cb4[17].x, r8.yzyy
add r8.yz, -r3.xxyx, r8.xxyx
dp2 r7.w, r8.yzyy, r11.xyxx
dp2 r4.z, r8.yzyy, r4.zwzz
min r4.z, r4.z, r7.w
lt r4.z, l(0.00000000e+00), r4.z
dp2 r4.w, r8.yzyy, r8.yzyy
sqrt r4.w, r4.w
add r8.x, -r4.w, cb4[17].x
movc r8.xyz, r4.z, r8.xyzx, r9.xyzx
movc r8.xyz, r6.w, r8.xyzx, r9.xyzx
lt r4.z, r7.x, r17.x
movc r7.xyz, r4.z, r7.xyzx, r17.xyzx
lt r4.z, r8.x, r7.x
movc r7.xyz, r4.z, r8.xyzx, r7.xyzx
dp2 r4.z, r7.yzyy, r7.yzyy
rsq r4.z, r4.z
mul r4.zw, r4.z, r7.yyyz
mul r6.w, cb4[17].x, l(5.00000000e-01)
mad r7.y, -cb4[17].x, l(5.00000000e-01), r7.x
mad r7.y, -cb4[17].x, l(5.00000000e-01), |r7.y|
div r7.z, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00), -r6.w
mul_sat r7.y, r7.z, r7.y
mad r7.z, r7.y, l(-2.00000000e+00), l(3.00000000e+00)
mul r7.y, r7.y, r7.y
mul r7.y, r7.y, r7.z
dp2 r7.z, r23.xyxx, r4.zwzz
mul r6.w, r6.w, r7.z
mul r6.w, r6.w, cb4[12].y
mad r6.w, r6.w, r7.y, r7.x
lt r5.w, r5.w, l(0.00000000e+00)
discard_nz r5.w
mul r7.xy, cb4[11].yzyy, cb4[17].x
mad r7.xy, cb4[11].xwxx, cb4[17].x, -r7.xyxx
mad r7.zw, -cb4[11].yyyz, cb4[17].x, r6.w
div r7.xy, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00), r7.xyxx
mul_sat r7.xy, r7.xyxx, r7.zwzz
mad r7.zw, r7.xxxy, l(0.00000000e+00, 0.00000000e+00, -2.00000000e+00, -2.00000000e+00), l(0.00000000e+00, 0.00000000e+00, 3.00000000e+00, 3.00000000e+00)
mul r7.xy, r7.xyxx, r7.xyxx
mul r7.xy, r7.xyxx, r7.zwzz
mov r3.zw, -cb1[44].xxxz
dp2 r3.z, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r3.xzxx
mov r4.y, -cb1[44].y
dp2 r4.x, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r4.xyxx
dp2 r3.w, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00), r3.ywyy
mul r8.xyz, r4.x, cb1[1].xyzx
mad r8.xyz, cb1[0].xyzx, r3.z, r8.xyzx
mad r8.xyz, cb1[2].xyzx, r3.w, r8.xyzx
add r8.xyz, r8.xyzx, cb1[3].xyzx
mul r1.w, r1.w, cb0[24].x
dp3 r3.z, r8.xyzx, r8.xyzx
mul r1.w, r1.w, r1.w
div r1.w, r3.z, r1.w
mad r1.w, -r1.w, r1.w, l(1.00000000e+00)
max r1.w, r1.w, l(0.00000000e+00)
mul r1.w, r1.w, r7.x
min r3.z, |cb4[12].x|, l(9.90000010e-01)
mul r3.z, r3.z, r7.y
lt r3.w, l(0.00000000e+00), cb4[12].x
lt r4.x, cb4[12].x, l(0.00000000e+00)
iadd r3.w, -r3.w, r4.x
itof r3.w, r3.w
mul r4.xz, r3.w, -r4.zzwz
mov r4.y, l(0)
mad r4.xyz, -r6.xyzx, r2.w, r4.xyzx
mad r4.xyz, r3.z, r4.xyzx, r14.xyzx
dp3 r2.w, r4.xyzx, r4.xyzx
rsq r2.w, r2.w
mul r4.xyz, r2.w, r4.xyzx
mul r6.xy, r15.yzyy, r4.yzyy
mov r6.z, l(0)
mov r15.x, l(0)
mad r6.xyz, -r4.zxyz, r15.xyzx, r6.xyzx
dp3 r2.w, r6.xyzx, r6.xyzx
rsq r2.w, r2.w
mul r7.xyz, r2.w, r6.xyzx
ftoi r2.w, cb4[3].w
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r3.zw, r2.w, l(32), t0.xxxy
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r8.xy, r2.w, l(48), t0.xyxx
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r9.xyzw, r2.w, l(4), t0.xyzw
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r10.xyzw, r2.w, l(64), t0.xyzw
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r11.xyzw, r2.w, l(80), t0.xyzw
ld_structured_indexable(structured_buffer, stride=128)(mixed,mixed,mixed,mixed) r12.xyzw, r2.w, l(96), t0.xyzw
deriv_rtx_coarse r15.xyz, r13.xyxx
deriv_rty_coarse r16.xyz, r13.xyxx
mul r15.xyz, r15.xyzx, cb4[0].zwzz
mul r16.xyz, r16.xyzx, cb4[0].zwzz
mul r13.zw, r9.yyyz, r15.zzzy
mul r9.yz, r9.yyzy, r16.zzyz
dp2 r2.w, r13.zwzz, r13.zwzz
dp2 r4.w, r9.yzyy, r9.yzyy
max r5.w, r2.w, r4.w
min r2.w, r2.w, r4.w
log r4.w, r5.w
mul r5.w, r4.w, l(5.00000000e-01)
log r2.w, r2.w
mad r2.w, -r2.w, l(5.00000000e-01), r5.w
min r2.w, r9.x, r2.w
mad r2.w, r4.w, l(5.00000000e-01), -r2.w
add r2.w, r2.w, l(-5.00000000e-01)
max r2.w, r2.w, l(0.00000000e+00)
min r2.w, r2.w, cb4[1].x
add r2.w, r2.w, l(5.00000000e-01)
round_ni r2.w, r2.w
frc r9.xy, r13.xyxx
eq r13.zw, cb4[1].w, l(0.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
add r17.xy, -cb4[1].yzyy, l(1.00000000e+00, 1.00000000e+00, 0.00000000e+00, 0.00000000e+00)
max r17.zw, r13.xxxy, cb4[1].yyyz
min r17.xy, r17.xyxx, r17.zwzz
movc r13.xy, r13.w, r17.xyxx, r13.xyxx
movc r9.xy, r13.z, r9.xyxx, r13.xyxx
mad r9.xy, r9.xyxx, cb4[0].zwzz, cb4[0].xyxx
mul r8.z, r8.y, r8.x
mul r8.xz, r8.xxzx, r9.xxyx
exp r4.w, -r2.w
mul r8.xz, r4.w, r8.xxzx
round_ni r8.xz, r8.xxzx
ftoi r13.xy, r8.xzxx
ftoi r13.zw, r2.w
ld_indexable(texture2d) r8.xzw, r13.xyzw, t2.xwyz
ubfe r13.xyzw, l(10, 10, 7, 7), l(14, 4, 24, 24), r8.xxxz
and r17.xyz, r8.xzwx, l(15, 15, 15, 0)
utof r17.xyz, r17.xyzx
exp r17.xyz, r17.xyzx
mul r18.xyz, r8.y, r17.xzyx
mov r17.w, r18.x
mul r8.xy, r9.xyxx, r17.xwxx
frc r8.xy, r8.xyxx
utof r13.xyzw, r13.xyzw
mad r8.xy, r8.xyxx, r3.zwzz, r13.xyxx
mad r13.xy, r8.xyxx, r10.xyxx, r10.zwzz
mul r19.xyz, r3.zwzz, r17.xwxx
mul r10.xyz, r10.xyxx, r19.xyzx
mul r10.xyz, r9.w, r10.xyzx
mul r19.xyz, r10.zyzz, r15.zyzz
mul r10.xyz, r10.xyzx, r16.zyzz
sample_d_indexable(texture2darray) r10.xyzw, r13.xyzx, t3.wxyz, s0, r19.xyzx, r10.xyzx
ubfe r19.xyzw, l(10, 10, 7, 10), l(14, 4, 24, 14), r8.zzww
mov r18.w, r17.y
mul r8.xy, r9.xyxx, r18.wzww
frc r8.xy, r8.xyxx
utof r17.xyw, r19.xyxz
mad r8.xy, r8.xyxx, r3.zwzz, r17.xyxx
mad r13.xy, r8.xyxx, r11.xyxx, r11.zwzz
mul r8.xyz, r3.zwzz, r18.wzww
mul r8.xyz, r11.xyxx, r8.xyzx
mul r8.xyz, r9.w, r8.xyzx
mul r11.xyz, r8.zyzz, r15.zyzz
mul r8.xyz, r8.xyzx, r16.zyzz
sample_d_indexable(texture2darray) r8.xyz, r13.xywx, t4.xywz, s0, r11.xyzx, r8.xyzx
ubfe r2.w, l(10), l(4), r8.w
mov r18.xz, r17.z
mul r9.xy, r9.xyxx, r18.xyxx
frc r9.xy, r9.xyxx
utof r11.x, r19.w
utof r11.y, r2.w
mad r9.xy, r9.xyxx, r3.zwzz, r11.xyxx
mad r17.xy, r9.xyxx, r12.xyxx, r12.zwzz
mul r9.xyz, r3.zwzz, r18.xyzx
mul r9.xyz, r12.xyxx, r9.xyzx
mul r9.xyz, r9.w, r9.xyzx
mul r11.xyz, r9.xyzx, r15.xyzx
mul r9.xyz, r9.xyzx, r16.xyzx
sample_d_indexable(texture2darray) r9.xyzw, r17.xywx, t5.xyzw, s0, r11.xyzx, r9.xyzx
mul r8.x, r8.x, r8.z
mad r3.zw, r8.xxxy, l(0.00000000e+00, 0.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(0.00000000e+00, 0.00000000e+00, -1.00000000e+00, -1.00000000e+00)
dp2 r2.w, r3.zwzz, r3.zwzz
min r2.w, r2.w, l(1.00000000e+00)
add r2.w, -r2.w, l(1.00000000e+00)
sqrt r2.w, r2.w
max r2.w, r2.w, l(1.00000002e-16)
mul r8.xyz, r10.yzwy, cb4[5].xyzx
add r9.yz, -r10.x, r9.zzyz
mad r9.yz, cb4[10].xxyx, r9.yyzy, r10.x
mul r10.x, r10.x, cb4[5].w
mul r10.yz, r9.yyzy, cb4[9].zzwz
mul r10.xyz, r1.w, r10.xyzx
mul r6.xyz, r6.xyzx, r3.w
mad r6.xyz, r3.z, r7.xyzx, r6.xyzx
mad r4.xyz, r2.w, r4.xyzx, r6.xyzx
mul r3.zw, r9.xxxw, cb4[9].xxxy
mul r6.xyzw, r3.xyxy, l(2.99999993e-02, 2.99999993e-02, 8.99999985e-04, 8.99999985e-04)
mad r7.xy, r3.xyxx, l(2.99999993e-02, 2.99999993e-02, 0.00000000e+00, 0.00000000e+00), cb0[6].xzxx
mul r7.xy, r7.xyxx, cb0[5].xzxx
sample_l_indexable(texture2d) r7.xy, r7.xyxx, t11.xyzw, s2, l(0.00000000e+00)
mul r7.x, r7.x, r7.x
mul r7.xy, r7.xyxx, cb0[22].xyxx
mul_sat r7.xy, r7.xyxx, l(2.00000000e+02, 2.00000000e+02, 0.00000000e+00, 0.00000000e+00)
max r2.w, r5.z, l(0.00000000e+00)
mad r4.w, -cb4[14].x, r7.y, l(9.49999988e-01)
add r4.w, -r4.w, l(2.50000000e-01)
mul_sat r4.w, r4.w, l(1.00000000e+01)
mad r5.w, r4.w, l(-2.00000000e+00), l(3.00000000e+00)
mul r4.w, r4.w, r4.w
mul r4.w, r4.w, r5.w
log r4.w, r4.w
mul r4.w, r4.w, l(1.40000000e+01)
exp r4.w, r4.w
mul r2.w, r2.w, r4.w
mul r4.w, r2.w, cb4[14].z
mad r7.yzw, r8.xxyz, r8.xxyz, -r8.xxyz
mad r7.yzw, r4.w, r7.yyzw, r8.xxyz
mad r8.xy, -r9.xwxx, cb4[9].xyxx, l(5.00000000e-01, 8.99999976e-01, 0.00000000e+00, 0.00000000e+00)
mad r3.zw, r2.w, r8.xxxy, r3.zzzw
sample_b_indexable(texture2d) r8.xyzw, r6.zwzz, t7.xyzw, s1, cb1[79].y
add r2.w, r7.x, l(-8.00000012e-01)
mul r2.w, r2.w, l(5.00000048e+00)
max r2.w, r2.w, l(0.00000000e+00)
add r4.w, -r7.x, l(9.75000024e-01)
mul r9.xyzw, r3.xyxy, l(1.97999984e-01, 1.97999984e-01, 9.89999920e-02, 9.89999920e-02)
round_ni r11.xyzw, r9.xyzw
frc r9.xyzw, r9.xyzw
mul r12.xyzw, r9.xyzw, r9.xyzw
mad r9.xyzw, -r9.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(3.00000000e+00, 3.00000000e+00, 3.00000000e+00, 3.00000000e+00)
mul r9.xyzw, r9.xyzw, r12.xyzw
add r12.xyzw, r11.xyxy, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
add r13.xyzw, r11.xyzw, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
ftoi r15.xyzw, r11.xyzw
xor r6.zw, r15.yyyw, l(0, 0, 1103515245, 1103515245)
iadd r11.xy, r6.zwzz, r15.xzxx
imul null, r6.zw, r6.zzzw, r11.xxxy
ushr r11.xy, r6.zwzz, l(5, 5, 0, 0)
xor r6.zw, r6.zzzw, r11.xxxy
imul null, r6.zw, r6.zzzw, l(0, 0, 668265261, 668265261)
utof r6.zw, r6.zzzw
mul r6.zw, r6.zzzw, l(0.00000000e+00, 0.00000000e+00, 2.32830644e-10, 2.32830644e-10)
ftoi r12.xyzw, r12.xyzw
xor r11.xy, r12.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r12.xy, r11.xyxx, r12.xzxx
imul null, r11.xy, r11.xyxx, r12.xyxx
ushr r12.xy, r11.xyxx, l(5, 5, 0, 0)
xor r11.xy, r11.xyxx, r12.xyxx
imul null, r11.xy, r11.xyxx, l(668265261, 668265261, 0, 0)
utof r11.xy, r11.xyxx
mul r5.w, r11.y, l(2.32830644e-10)
ftoi r12.xyzw, r13.xyzw
xor r12.yw, r12.yyyw, l(0, 1103515245, 0, 1103515245)
iadd r12.xz, r12.yywy, r12.xxzx
imul null, r12.xy, r12.ywyy, r12.xzxx
ushr r12.zw, r12.xxxy, l(0, 0, 5, 5)
xor r12.xy, r12.zwzz, r12.xyxx
imul null, r12.xy, r12.xyxx, l(668265261, 668265261, 0, 0)
utof r12.xy, r12.xyxx
mad r7.x, r11.x, l(2.32830644e-10), -r6.z
mad r6.z, r9.x, r7.x, r6.z
mad r7.x, r12.x, l(2.32830644e-10), -r5.w
mad r5.w, r9.x, r7.x, r5.w
add r5.w, -r6.z, r5.w
mad r5.w, r9.y, r5.w, r6.z
add r11.xyzw, r11.zwzw, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
ftoi r11.xyzw, r11.xyzw
xor r9.xy, r11.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r11.xy, r9.xyxx, r11.xzxx
imul null, r9.xy, r9.xyxx, r11.xyxx
ushr r11.xy, r9.xyxx, l(5, 5, 0, 0)
xor r9.xy, r9.xyxx, r11.xyxx
imul null, r9.xy, r9.xyxx, l(668265261, 668265261, 0, 0)
utof r9.xy, r9.xyxx
mul r6.z, r9.y, l(2.32830644e-10)
mad r7.x, r9.x, l(2.32830644e-10), -r6.w
mad r6.w, r9.z, r7.x, r6.w
mad r7.x, r12.y, l(2.32830644e-10), -r6.z
mad r6.z, r9.z, r7.x, r6.z
add r6.z, -r6.w, r6.z
mad r6.z, r9.w, r6.z, r6.w
mul r6.z, r6.z, l(2.50000000e-01)
mad r5.w, r5.w, l(1.25000000e-01), r6.z
mul r9.xyzw, r3.xyxy, l(4.94999960e-02, 4.94999960e-02, 5.39999977e-02, 5.39999977e-02)
round_ni r11.xyzw, r9.xyzw
frc r9.xyzw, r9.xyzw
mul r12.xyzw, r9.xyzw, r9.xyzw
mad r9.xyzw, -r9.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(3.00000000e+00, 3.00000000e+00, 3.00000000e+00, 3.00000000e+00)
mul r9.xyzw, r9.xyzw, r12.xyzw
add r12.xyzw, r11.xyxy, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
add r13.xyzw, r11.xyzw, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
ftoi r15.xyzw, r11.xyzw
xor r6.zw, r15.yyyw, l(0, 0, 1103515245, 1103515245)
iadd r11.xy, r6.zwzz, r15.xzxx
imul null, r6.zw, r6.zzzw, r11.xxxy
ushr r11.xy, r6.zwzz, l(5, 5, 0, 0)
xor r6.zw, r6.zzzw, r11.xxxy
imul null, r6.zw, r6.zzzw, l(0, 0, 668265261, 668265261)
utof r6.zw, r6.zzzw
mul r6.zw, r6.zzzw, l(0.00000000e+00, 0.00000000e+00, 2.32830644e-10, 2.32830644e-10)
ftoi r12.xyzw, r12.xyzw
xor r11.xy, r12.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r12.xy, r11.xyxx, r12.xzxx
imul null, r11.xy, r11.xyxx, r12.xyxx
ushr r12.xy, r11.xyxx, l(5, 5, 0, 0)
xor r11.xy, r11.xyxx, r12.xyxx
imul null, r11.xy, r11.xyxx, l(668265261, 668265261, 0, 0)
utof r11.xy, r11.xyxx
mul r7.x, r11.y, l(2.32830644e-10)
ftoi r12.xyzw, r13.xyzw
xor r12.yw, r12.yyyw, l(0, 1103515245, 0, 1103515245)
iadd r12.xz, r12.yywy, r12.xxzx
imul null, r12.xy, r12.ywyy, r12.xzxx
ushr r12.zw, r12.xxxy, l(0, 0, 5, 5)
xor r12.xy, r12.zwzz, r12.xyxx
imul null, r12.xy, r12.xyxx, l(668265261, 668265261, 0, 0)
utof r12.xy, r12.xyxx
mad r10.w, r11.x, l(2.32830644e-10), -r6.z
mad r6.z, r9.x, r10.w, r6.z
mad r10.w, r12.x, l(2.32830644e-10), -r7.x
mad r7.x, r9.x, r10.w, r7.x
add r7.x, -r6.z, r7.x
mad r6.z, r9.y, r7.x, r6.z
mad r5.w, r6.z, l(5.00000000e-01), r5.w
add r11.xyzw, r11.zwzw, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
ftoi r11.xyzw, r11.xyzw
xor r9.xy, r11.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r11.xy, r9.xyxx, r11.xzxx
imul null, r9.xy, r9.xyxx, r11.xyxx
ushr r11.xy, r9.xyxx, l(5, 5, 0, 0)
xor r9.xy, r9.xyxx, r11.xyxx
imul null, r9.xy, r9.xyxx, l(668265261, 668265261, 0, 0)
utof r9.xy, r9.xyxx
mul r6.z, r9.y, l(2.32830644e-10)
mad r7.x, r9.x, l(2.32830644e-10), -r6.w
mad r6.w, r9.z, r7.x, r6.w
mad r7.x, r12.y, l(2.32830644e-10), -r6.z
mad r6.z, r9.z, r7.x, r6.z
add r6.z, -r6.w, r6.z
mad r6.z, r9.w, r6.z, r6.w
mul r9.xyzw, r3.xyxy, l(2.69999988e-02, 2.69999988e-02, 1.34999994e-02, 1.34999994e-02)
round_ni r11.xyzw, r9.xyzw
frc r9.xyzw, r9.xyzw
mul r12.xyzw, r9.xyzw, r9.xyzw
mad r9.xyzw, -r9.xyzw, l(2.00000000e+00, 2.00000000e+00, 2.00000000e+00, 2.00000000e+00), l(3.00000000e+00, 3.00000000e+00, 3.00000000e+00, 3.00000000e+00)
mul r9.xyzw, r9.xyzw, r12.xyzw
add r12.xyzw, r11.xyxy, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
add r13.xyzw, r11.xyzw, l(1.00000000e+00, 1.00000000e+00, 1.00000000e+00, 1.00000000e+00)
ftoi r15.xyzw, r11.xyzw
xor r11.xy, r15.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r15.xy, r11.xyxx, r15.xzxx
imul null, r11.xy, r11.xyxx, r15.xyxx
ushr r15.xy, r11.xyxx, l(5, 5, 0, 0)
xor r11.xy, r11.xyxx, r15.xyxx
imul null, r11.xy, r11.xyxx, l(668265261, 668265261, 0, 0)
utof r11.xy, r11.xyxx
mul r11.xy, r11.xyxx, l(2.32830644e-10, 2.32830644e-10, 0.00000000e+00, 0.00000000e+00)
ftoi r12.xyzw, r12.xyzw
xor r12.yw, r12.yyyw, l(0, 1103515245, 0, 1103515245)
iadd r12.xz, r12.yywy, r12.xxzx
imul null, r12.xy, r12.ywyy, r12.xzxx
ushr r12.zw, r12.xxxy, l(0, 0, 5, 5)
xor r12.xy, r12.zwzz, r12.xyxx
imul null, r12.xy, r12.xyxx, l(668265261, 668265261, 0, 0)
utof r12.xy, r12.xyxx
mul r6.w, r12.y, l(2.32830644e-10)
ftoi r13.xyzw, r13.xyzw
xor r12.yz, r13.yywy, l(0, 1103515245, 1103515245, 0)
iadd r13.xy, r12.yzyy, r13.xzxx
imul null, r12.yz, r12.yyzy, r13.xxyx
ushr r13.xy, r12.yzyy, l(5, 5, 0, 0)
xor r12.yz, r12.yyzy, r13.xxyx
imul null, r12.yz, r12.yyzy, l(0, 668265261, 668265261, 0)
utof r12.yz, r12.yyzy
mad r7.x, r12.x, l(2.32830644e-10), -r11.x
mad r7.x, r9.x, r7.x, r11.x
mad r10.w, r12.y, l(2.32830644e-10), -r6.w
mad r6.w, r9.x, r10.w, r6.w
add r6.w, -r7.x, r6.w
mad r6.w, r9.y, r6.w, r7.x
mul r6.w, r6.w, l(2.50000000e-01)
mad r6.z, r6.z, l(1.25000000e-01), r6.w
add r13.xyzw, r11.zwzw, l(1.00000000e+00, 0.00000000e+00, 0.00000000e+00, 1.00000000e+00)
ftoi r13.xyzw, r13.xyzw
xor r9.xy, r13.ywyy, l(1103515245, 1103515245, 0, 0)
iadd r11.xz, r9.xxyx, r13.xxzx
imul null, r9.xy, r9.xyxx, r11.xzxx
ushr r11.xz, r9.xxyx, l(5, 0, 5, 0)
xor r9.xy, r9.xyxx, r11.xzxx
imul null, r9.xy, r9.xyxx, l(668265261, 668265261, 0, 0)
utof r9.xy, r9.xyxx
mul r6.w, r9.y, l(2.32830644e-10)
mad r7.x, r9.x, l(2.32830644e-10), -r11.y
mad r7.x, r9.z, r7.x, r11.y
mad r9.x, r12.z, l(2.32830644e-10), -r6.w
mad r6.w, r9.z, r9.x, r6.w
add r6.w, -r7.x, r6.w
mad r6.w, r9.w, r6.w, r7.x
mad r6.z, r6.w, l(5.00000000e-01), r6.z
add r6.z, -r5.w, r6.z
mad r5.w, r6.z, l(5.00000000e-01), r5.w
mul r3.xy, r3.xyxx, l(1.79999997e-03, 1.79999997e-03, 0.00000000e+00, 0.00000000e+00)
round_ni r6.zw, r3.xxxy
frc r3.xy, r3.xyxx
ftoi r9.xy, r6.zwzz
xor r7.x, r9.y, l(1103515245)
iadd r9.x, r7.x, r9.x
imul null, r7.x, r7.x, r9.x
ushr r9.x, r7.x, l(5)
xor r7.x, r7.x, r9.x
imul null, r7.x, r7.x, l(668265261)
utof r7.x, r7.x
mad r9.yz, r7.x, l(0.00000000e+00, 2.32830644e-10, 2.32830644e-10, 0.00000000e+00), l(0.00000000e+00, 5.00000000e-01, -5.00000000e-01, 0.00000000e+00)
round_ni r9.y, r9.y
mad r9.x, r7.x, l(2.32830644e-10), -r9.y
dp2 r7.x, r9.xzxx, r9.xzxx
rsq r7.x, r7.x
mul r9.xy, r7.x, r9.xzxx
dp2 r7.x, r9.xyxx, r3.xyxx
add r9.xyzw, r6.zwzw, l(0.00000000e+00, 1.00000000e+00, 1.00000000e+00, 0.00000000e+00)
ftoi r9.xyzw, r9.xyzw
xor r9.yw, r9.yyyw, l(0, 1103515245, 0, 1103515245)
iadd r9.xz, r9.yywy, r9.xxzx
imul null, r9.xy, r9.ywyy, r9.xzxx
ushr r9.zw, r9.xxxy, l(0, 0, 5, 5)
xor r9.xy, r9.zwzz, r9.xyxx
imul null, r9.xy, r9.xyxx, l(668265261, 668265261, 0, 0)
utof r9.xy, r9.xyxx
mad r11.xyzw, r9.xyxy, l(2.32830644e-10, 2.32830644e-10, 2.32830644e-10, 2.32830644e-10), l(5.00000000e-01, 5.00000000e-01, -5.00000000e-01, -5.00000000e-01)
round_ni r9.zw, r11.xxxy
mad r11.xy, r9.xyxx, l(2.32830644e-10, 2.32830644e-10, 0.00000000e+00, 0.00000000e+00), -r9.zwzz
dp2 r9.x, r11.xzxx, r11.xzxx
rsq r9.x, r9.x
mul r9.xy, r9.x, r11.xzxx
add r12.xyzw, r3.xyxy, l(-0.00000000e+00, -1.00000000e+00, -1.00000000e+00, -0.00000000e+00)
dp2 r9.x, r9.xyxx, r12.xyxx
dp2 r9.y, r11.ywyy, r11.ywyy
rsq r9.y, r9.y
mul r9.yz, r9.y, r11.yywy
dp2 r9.y, r9.yzyy, r12.zwzz
add r6.zw, r6.zzzw, l(0.00000000e+00, 0.00000000e+00, 1.00000000e+00, 1.00000000e+00)
ftoi r6.zw, r6.zzzw
xor r6.w, r6.w, l(1103515245)
iadd r6.z, r6.w, r6.z
imul null, r6.z, r6.w, r6.z
ushr r6.w, r6.z, l(5)
xor r6.z, r6.w, r6.z
imul null, r6.z, r6.z, l(668265261)
utof r6.z, r6.z
mad r11.yz, r6.z, l(0.00000000e+00, 2.32830644e-10, 2.32830644e-10, 0.00000000e+00), l(0.00000000e+00, 5.00000000e-01, -5.00000000e-01, 0.00000000e+00)
round_ni r6.w, r11.y
mad r11.x, r6.z, l(2.32830644e-10), -r6.w
dp2 r6.z, r11.xzxx, r11.xzxx
rsq r6.z, r6.z
mul r6.zw, r6.z, r11.xxxz
add r9.zw, r3.xxxy, l(0.00000000e+00, 0.00000000e+00, -1.00000000e+00, -1.00000000e+00)
dp2 r6.z, r6.zwzz, r9.zwzz
mul r9.zw, r3.xxxy, r3.xxxy
mul r9.zw, r3.xxxy, r9.zzzw
mad r11.xy, r3.xyxx, l(6.00000000e+00, 6.00000000e+00, 0.00000000e+00, 0.00000000e+00), l(-1.50000000e+01, -1.50000000e+01, 0.00000000e+00, 0.00000000e+00)
mad r3.xy, r3.xyxx, r11.xyxx, l(1.00000000e+01, 1.00000000e+01, 0.00000000e+00, 0.00000000e+00)
mul r3.xy, r3.xyxx, r9.zwzz
add r6.w, -r7.x, r9.x
mad r6.w, r3.y, r6.w, r7.x
add r6.z, -r9.y, r6.z
mad r3.y, r3.y, r6.z, r9.y
add r3.y, -r6.w, r3.y
mad r3.x, r3.x, r3.y, r6.w
add r3.x, r3.x, l(5.00000000e-01)
add r3.x, -r5.w, r3.x
mad r3.x, r3.x, l(5.00000000e-01), r5.w
add r3.x, -r4.w, r3.x
mul_sat r3.x, r3.x, l(2.00000000e+01)
mad r3.y, r3.x, l(-2.00000000e+00), l(3.00000000e+00)
mul r3.x, r3.x, r3.x
mul r3.x, r3.x, r3.y
log r3.x, r3.x
mul r3.x, r3.x, l(1.40000000e+01)
exp r3.x, r3.x
add r8.xyz, -r7.yzwy, r8.xyzx
mad o0.xyz, r3.x, r8.xyzx, r7.yzwy
mad r3.y, r8.w, l(2.00000000e+00), l(-1.00000000e+00)
mad r4.w, -r2.w, l(4.49999988e-01), l(4.49999988e-01)
max r4.w, r4.w, l(1.00000001e-01)
mad r7.z, r3.y, l(1.00000001e-01), r4.w
mov r7.x, l(0)
add r6.zw, -r3.zzzw, r7.xxxz
mad r3.xy, r3.x, r6.zwzz, r3.zwzz
mul r0.x, r0.x, r1.w
ieq r1.w, cb5[404].y, l(1)
movc o0.w, r1.w, r0.x, r10.x
movc o2.xz, r1.w, l(0, 0, 0, 0), r3.xxyx
mul r3.xyz, r1.zxyz, r5.zxyz
mad r3.xyz, r1.yzxy, r5.xyzx, -r3.xyzx
mul r7.xyz, r2.yzxy, r5.xyzx
mad r5.xyz, r5.zxyz, r2.zxyz, -r7.xyzx
mul r7.xyz, r1.yzxy, r2.zxyz
mad r7.xyz, r2.yzxy, r1.zxyz, -r7.xyzx
dp3 r0.x, r2.xyzx, r3.xyzx
dp3 r3.x, r3.xyzx, r14.xyzx
dp3 r3.y, r5.xyzx, r14.xyzx
dp3 r3.z, r7.xyzx, r14.xyzx
lt r0.x, r0.x, l(0.00000000e+00)
movc r0.x, r0.x, l(-1.00000000e+00), l(1.00000000e+00)
mul r3.xyz, r3.xyzx, r0.x
dp3 r0.x, r3.xyzx, r3.xyzx
max r0.x, r0.x, l(1.17549435e-38)
rsq r0.x, r0.x
sample_b_indexable(texture2d) r5.xyz, r6.xyxx, t6.xywz, s1, cb1[79].y
mul r5.x, r5.x, r5.z
mad r5.xy, r5.xyxx, l(2.00000000e+00, 2.00000000e+00, 0.00000000e+00, 0.00000000e+00), l(-1.00000000e+00, -1.00000000e+00, 0.00000000e+00, 0.00000000e+00)
dp2 r1.w, r5.xyxx, r5.xyxx
min r1.w, r1.w, l(1.00000000e+00)
add r1.w, -r1.w, l(1.00000000e+00)
sqrt r1.w, r1.w
max r5.z, r1.w, l(1.00000002e-16)
mad r3.xyz, r3.xyzx, r0.x, l(0.00000000e+00, 0.00000000e+00, 1.00000000e+00, 0.00000000e+00)
mul r5.xyz, r5.xyzx, l(-1.00000000e+00, -1.00000000e+00, 1.00000000e+00, 0.00000000e+00)
div r6.xyz, r3.xyzx, r3.z
dp3 r0.x, r3.xyzx, r5.xyzx
mad r3.xyz, r6.xyzx, r0.x, -r5.xyzx
add r3.xyz, -r4.xyzx, r3.xyzx
mad r3.xyz, r2.w, r3.xyzx, r4.xyzx
dp3 r0.x, r3.xyzx, r3.xyzx
rsq r0.x, r0.x
mul r3.xyz, r0.x, r3.xyzx
mul r1.xyz, r1.xyzx, r3.y
mad r1.xyz, r3.x, r2.xyzx, r1.xyzx
mad r0.xyz, r3.z, r0.yzwy, r1.xyzx
dp3 r0.w, r0.xyzx, r0.xyzx
rsq r0.w, r0.w
mul r0.xyz, r0.w, r0.xyzx
mad o1.xyz, r0.xyzx, l(5.00000000e-01, 5.00000000e-01, 5.00000000e-01, 0.00000000e+00), l(5.00000000e-01, 5.00000000e-01, 5.00000000e-01, 0.00000000e+00)
mov o1.w, r10.y
mov o2.y, l(1.00000000e+00)
mov o2.w, r10.z
mov o3.xy, r10.z
ret
