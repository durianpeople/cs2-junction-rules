ps_5_0
.input
.param SV_POSITION.xyzw, v0.xy, float, POS
.param INTERP.xyzw, v1, float
.param INTERP1.xyzw, v2, float
.param INTERP2.xyz, v3, float
.param INTERP3.xyz, v4, float
.output
.param SV_Target.xyzw, o0.xyzw, float, TARGET
.param SV_Target1.xyzw, o1.xyzw, float, TARGET
.param SV_Target2.xyzw, o2.xyzw, float, TARGET
.param SV_Target3.xy, o3.xy, float, TARGET
.text
dcl_globalFlags refactoringAllowed
dcl_constantBuffer cb0[110], immediateIndexed
dcl_constantBuffer cb1[19], immediateIndexed
dcl_resource_texture2darray (float,float,float,float) t0
dcl_input_ps_siv linear noperspective v0.xy, position
dcl_output o0.xyzw
dcl_output o1.xyzw
dcl_output o2.xyzw
dcl_output o3.xy
dcl_temps 1
if_nz cb0[109].w
    ftou r0.xy, v0.xyxx
    mov r0.zw, l(0, 0, 0, 0)
    ld_indexable(texture2darray) r0.x, r0.xyzw, t0.zxyw
    mul r0.x, r0.x, l(2.55500000e+02)
    ftou r0.x, r0.x
    movc r0.x, r0.x, r0.x, l(1)
    and r0.x, r0.x, cb1[18].x
    movc r0.x, r0.x, l(1.00000000e+00), l(-1.00000000e+00)
else
    mov r0.x, l(1.00000000e+00)
endif
lt r0.x, r0.x, l(0.00000000e+00)
discard_nz r0.x
mov o0.xyzw, l(0, 0, 0, 0)
mov o1.xyzw, l(5.00000000e-01, 5.00000000e-01, 5.00000000e-01, 0)
mov o2.xyzw, l(0, 0, 0, 0)
mov o3.xy, l(0, 0, 0, 0)
ret
