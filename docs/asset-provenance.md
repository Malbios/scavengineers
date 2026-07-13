# Asset provenance register

Every model, texture, sound, and AI-generated asset added to the project gets a row here at the time it's added — source, license, date. This is legal insurance for the "spiritual successor, no reused assets" commitment (`docs/project-plan.md` §3/§9) and costs seconds per asset if kept up to date from file #1.

AI-generated **textures, decals, and icons** are fine to log here. AI-generated **geometry** is not game-ready per the plan's art strategy — don't add AI-generated models to the project or this register.

| Asset | Source | License | Date added | Notes |
|---|---|---|---|---|
| `Assets/Textures/home_floor_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Clean/light panel pair, ~1m tiles, shallow seam groove — Home Ship floor |
| `Assets/Textures/home_wall_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Clean/light panel pair, ~2m tiles, shallow seam groove — Home Ship walls |
| `Assets/Textures/home_ceiling_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Clean/light panel pair, ~1.3m tiles, shallow seam groove — Home Ship ceiling |
| `Assets/Textures/derelict_floor_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Worn/dingy panel pair, ~1m tiles, deep seam groove — Derelict floor |
| `Assets/Textures/derelict_wall_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Worn/dingy panel pair, ~2m tiles, deep seam groove — Derelict walls |
| `Assets/Textures/derelict_ceiling_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-10 | Worn/dingy panel pair, ~1.3m tiles, deep seam groove — Derelict ceiling |
| `Shaders/starfield_sky.gdshader` | Procedurally generated (hand-authored GDShader, not AI-generated) | Project-owned | 2026-07-13 | Hash-noise starfield skybox — no baked texture, stars generated live per-pixel from EYEDIR |
| `Assets/Textures/station_floor_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-13 | Bold teal panel pair, ~1m tiles, clean (no grunge) — Station floor |
| `Assets/Textures/station_wall_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-13 | Bold teal panel pair, ~2m tiles, clean (no grunge) — Station walls |
| `Assets/Textures/station_ceiling_{albedo,normal}.png` | Procedurally generated (custom C# script, not AI-generated) | Project-owned | 2026-07-13 | Bold teal panel pair, ~1.3m tiles, clean (no grunge) — Station ceiling |
