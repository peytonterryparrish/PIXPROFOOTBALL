# Model QA measurements (before)

Circumferences are hull perimeters (m) of cross-sections perpendicular to the bones of the posed meshes. Ratios are relative to the same body's LOD0 T-pose.

## T-pose (bind) by body and LOD
| body | LOD | waist | chest | hip | thigh | calf | waist w x d | ratio waist | ratio thigh | ratio calf |
|---|---|---|---|---|---|---|---|---|---|---|
| Small | LOD0 | 1.053 | 0.953 | 1.063 | 0.647 | 0.392 | 0.316 x 0.344 | 1.000 | 1.000 | 1.000 |
| Small | LOD1 | 1.044 | 0.947 | 1.063 | 0.644 | 0.383 | 0.314 x 0.339 | 0.991 | 0.995 | 0.978 |
| Small | LOD2 | 1.026 | 0.943 | 1.046 | 0.627 | 0.353 | 0.310 x 0.337 | 0.974 | 0.969 | 0.900 |
| Small | LOD3 | 1.026 | 0.943 | 1.046 | 0.627 | 0.353 | 0.310 x 0.337 | 0.974 | 0.969 | 0.900 |
| Medium | LOD0 | 1.102 | 1.021 | 1.062 | 0.641 | 0.389 | 0.318 x 0.372 | 1.000 | 1.000 | 1.000 |
| Medium | LOD1 | 1.096 | 1.016 | 1.060 | 0.639 | 0.378 | 0.319 x 0.366 | 0.994 | 0.997 | 0.972 |
| Medium | LOD2 | 1.070 | 1.014 | 1.046 | 0.624 | 0.351 | 0.302 x 0.367 | 0.971 | 0.974 | 0.903 |
| Medium | LOD3 | 1.070 | 1.014 | 1.046 | 0.624 | 0.351 | 0.302 x 0.367 | 0.971 | 0.974 | 0.903 |
| Large | LOD0 | 1.298 | 1.247 | 1.203 | 0.686 | 0.427 | 0.372 x 0.441 | 1.000 | 1.000 | 1.000 |
| Large | LOD1 | 1.090 | 1.017 | 0.957 | 0.620 | 0.374 | 0.315 x 0.366 | 0.840 | 0.904 | 0.874 |
| Large | LOD2 | 1.067 | 1.015 | 0.945 | 0.602 | 0.343 | 0.299 x 0.370 | 0.822 | 0.878 | 0.802 |
| Large | LOD3 | 1.067 | 1.015 | 0.945 | 0.602 | 0.343 | 0.299 x 0.370 | 0.822 | 0.878 | 0.802 |

## Worst ratios over all poses (vs the LOD0 T-pose of the same body)
| body | LOD | min waist (pose) | min thigh (pose) | min calf (pose) | max thigh (pose) | max leg stretch | foot min Y range |
|---|---|---|---|---|---|---|---|
| Small | LOD0 | 0.964 (qb_stance_shotgun) | 0.972 (catch_dive) | 0.960 (catch_dive) | 1.148 (run) | 1.050 (catch_dive) | -0.083 .. 0.476 |
| Small | LOD1 | 0.955 (qb_drop_5) | 0.961 (catch_dive) | 0.922 (catch_dive) | 1.142 (sprint) | 1.050 (catch_dive) | -0.083 .. 0.476 |
| Small | LOD2 | 0.930 (qb_stance_shotgun) | 0.935 (catch_dive) | 0.841 (catch_dive) | 1.111 (run) | 1.050 (catch_dive) | -0.082 .. 0.476 |
| Small | LOD3 | 0.930 (qb_stance_shotgun) | 0.935 (catch_dive) | 0.841 (catch_dive) | 1.111 (run) | 1.050 (catch_dive) | -0.082 .. 0.476 |
| Medium | LOD0 | 0.989 (dive_forward) | 0.968 (catch_dive) | 0.963 (catch_dive) | 1.161 (run) | 1.050 (catch_dive) | -0.074 .. 0.488 |
| Medium | LOD1 | 0.981 (dive_forward) | 0.960 (catch_dive) | 0.913 (catch_dive) | 1.124 (run) | 1.050 (catch_dive) | -0.082 .. 0.480 |
| Medium | LOD2 | 0.951 (catch_low) | 0.934 (catch_dive) | 0.862 (catch_dive) | 1.082 (run) | 1.050 (catch_dive) | -0.081 .. 0.481 |
| Medium | LOD3 | 0.951 (catch_low) | 0.934 (catch_dive) | 0.862 (catch_dive) | 1.082 (run) | 1.050 (catch_dive) | -0.081 .. 0.481 |
| Large | LOD0 | 0.977 (dive_forward) | 0.972 (catch_dive) | 0.953 (qb_throw) | 1.100 (run) | 1.050 (qb_throw) | -0.076 .. 0.496 |
| Large | LOD1 | 0.833 (tackle_big_hit) | 0.787 (catch_dive) | 0.841 (catch_dive) | 1.013 (run) | 1.050 (qb_throw) | -0.091 .. 0.480 |
| Large | LOD2 | 0.817 (celebrate_td) | 0.762 (catch_dive) | 0.762 (catch_dive) | 0.975 (run) | 1.050 (qb_throw) | -0.091 .. 0.481 |
| Large | LOD3 | 0.817 (celebrate_td) | 0.762 (catch_dive) | 0.762 (catch_dive) | 0.975 (run) | 1.050 (qb_throw) | -0.091 .. 0.481 |
