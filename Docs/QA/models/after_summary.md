# Model QA measurements (after)

Circumferences are hull perimeters (m) of cross-sections perpendicular to the bones of the posed meshes. Ratios are relative to the same body's LOD0 T-pose.

## T-pose (bind) by body and LOD
| body | LOD | waist | chest | hip | thigh | calf | waist w x d | ratio waist | ratio thigh | ratio calf |
|---|---|---|---|---|---|---|---|---|---|---|
| Small | LOD0 | 1.096 | 0.995 | 1.105 | 0.691 | 0.416 | 0.329 x 0.358 | 1.000 | 1.000 | 1.000 |
| Small | LOD1 | 1.095 | 0.995 | 1.105 | 0.680 | 0.415 | 0.329 x 0.357 | 1.000 | 0.983 | 0.997 |
| Small | LOD2 | 1.094 | 0.997 | 1.107 | 0.662 | 0.416 | 0.329 x 0.357 | 0.998 | 0.958 | 1.001 |
| Small | LOD3 | 1.094 | 0.998 | 1.106 | 0.665 | 0.417 | 0.333 x 0.355 | 0.998 | 0.962 | 1.002 |
| Medium | LOD0 | 1.164 | 1.087 | 1.121 | 0.701 | 0.424 | 0.334 x 0.395 | 1.000 | 1.000 | 1.000 |
| Medium | LOD1 | 1.163 | 1.088 | 1.122 | 0.690 | 0.423 | 0.334 x 0.395 | 1.000 | 0.985 | 0.997 |
| Medium | LOD2 | 1.164 | 1.087 | 1.122 | 0.686 | 0.423 | 0.335 x 0.396 | 1.000 | 0.978 | 0.996 |
| Medium | LOD3 | 1.164 | 1.090 | 1.121 | 0.672 | 0.423 | 0.335 x 0.395 | 1.000 | 0.958 | 0.996 |
| Large | LOD0 | 1.396 | 1.358 | 1.294 | 0.720 | 0.475 | 0.398 x 0.476 | 1.000 | 1.000 | 1.000 |
| Large | LOD1 | 1.396 | 1.359 | 1.294 | 0.715 | 0.474 | 0.398 x 0.475 | 1.000 | 0.993 | 0.997 |
| Large | LOD2 | 1.395 | 1.362 | 1.294 | 0.734 | 0.475 | 0.397 x 0.476 | 0.999 | 1.019 | 0.999 |
| Large | LOD3 | 1.394 | 1.360 | 1.292 | 0.722 | 0.474 | 0.400 x 0.473 | 0.998 | 1.002 | 0.997 |

## Worst ratios over all poses (vs the LOD0 T-pose of the same body)
| body | LOD | min waist (pose) | min thigh (pose) | min calf (pose) | max thigh (pose) | max leg stretch | foot min Y range |
|---|---|---|---|---|---|---|---|
| Small | LOD0 | 0.695 (qb_sack) | 0.982 (qb_stance_shotgun) | 0.976 (qb_stance_shotgun) | 1.147 (qb_sack) | 1.025 (qb_stance_shotgun) | -0.083 .. 0.775 |
| Small | LOD1 | 0.689 (qb_sack) | 0.983 (qb_stance_shotgun) | 0.979 (qb_stance_shotgun) | 1.124 (qb_sack) | 1.025 (qb_stance_shotgun) | -0.083 .. 0.775 |
| Small | LOD2 | 0.669 (qb_sack) | 0.977 (ol_block_engage) | 0.984 (qb_stance_shotgun) | 1.077 (qb_sack) | 1.025 (qb_stance_shotgun) | -0.083 .. 0.773 |
| Small | LOD3 | 0.635 (qb_sack) | 0.977 (catch_low) | 0.983 (stiff_arm) | 1.041 (qb_sack) | 1.025 (qb_stance_shotgun) | -0.079 .. 0.773 |
| Medium | LOD0 | 0.891 (qb_sack) | 0.967 (qb_stance_shotgun) | 0.967 (qb_stance_shotgun) | 1.128 (qb_sack) | 1.035 (qb_stance_shotgun) | -0.074 .. 0.820 |
| Medium | LOD1 | 0.887 (qb_sack) | 0.968 (qb_stance_shotgun) | 0.970 (qb_stance_shotgun) | 1.085 (qb_sack) | 1.035 (qb_stance_shotgun) | -0.074 .. 0.821 |
| Medium | LOD2 | 0.886 (qb_sack) | 0.969 (qb_stance_shotgun) | 0.973 (qb_stance_shotgun) | 1.072 (run) | 1.035 (qb_stance_shotgun) | -0.074 .. 0.819 |
| Medium | LOD3 | 0.884 (qb_sack) | 0.933 (be_tackled) | 0.961 (qb_stance_shotgun) | 1.045 (qb_throw_lob_long) | 1.035 (qb_stance_shotgun) | -0.074 .. 0.819 |
| Large | LOD0 | 0.893 (stiff_arm) | 0.855 (tackle_wrap) | 0.977 (fall) | 1.089 (qb_sack) | 1.019 (fall) | -0.076 .. 0.844 |
| Large | LOD1 | 0.892 (stiff_arm) | 0.840 (tackle_wrap) | 0.980 (fall) | 1.076 (qb_sack) | 1.019 (fall) | -0.076 .. 0.844 |
| Large | LOD2 | 0.895 (stiff_arm) | 0.833 (dl_bull_rush) | 0.976 (qb_stance_shotgun) | 1.108 (qb_sack) | 1.019 (fall) | -0.077 .. 0.843 |
| Large | LOD3 | 0.894 (stiff_arm) | 0.791 (dl_bull_rush) | 0.972 (fall) | 1.082 (qb_sack) | 1.019 (fall) | -0.077 .. 0.842 |
