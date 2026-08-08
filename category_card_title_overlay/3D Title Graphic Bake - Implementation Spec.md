# 3D Category Title Graphic — Implementation Spec

Extends the existing Title Graphics feature (`TitleGraphicsConfigView`,
`TitleGraphicsSettings`, `CategoryTitleGraphic`, on/off + Top/Middle/Bottom
placement — all already built and wired into `CategoryCard`) with the
real-3D-rendered look from `racing-3d-title.html`, baked to an image per
category instead of drawn live from stacked 2D TextBlocks.

## Final visual design (source of truth: `racing-3d-title.html`)

- Extruded block-letter title, camera front-on, no tilt/skew (straightened per feedback)
- Front face: gold vertical gradient (`#B8860B` → `#E8C848` → `#FFF3C4`)
- Bevel (the extruded edge between front face and back): purple (`#A855FF`, emissive `#5B1E8A`)
- A second, slightly larger silhouette layer sitting just behind the front face acts as a red outline/depth trace (`#C41E1E`, emissive `#6E0A0A`)
- Scene lit by two colored point lights close to the object: yellow (`#F5C518`) front, dark purple (`#3D1466`) side — real illumination, not just emissive material color
- No black extrusion block (removed per feedback)

This is a real three.js scene (`TextGeometry` + `MeshStandardMaterial`, per-group
materials for front-cap vs. bevel/side faces), not CSS/XAML approximation —
see the file for the exact geometry parameters.

## Why bake instead of render live in Avalonia

Avalonia has no 3D renderer. Getting this exact look (true bevel lighting,
colored point lights, gradient-mapped extrusion) at runtime would mean
embedding a real 3D engine in the .NET app — out of scope for a launcher UI.
Instead: render it once per category (headless, off the user's critical path),
save the result as a transparent PNG, and display that PNG through the
**existing** on/off + placement system — no runtime 3D dependency in the
running app.

## Bake pipeline

1. **Renderer**: an offscreen `Microsoft.Web.WebView2` control (first-party,
   Windows-only, already viable in an Avalonia desktop app) hosts a local copy
   of the three.js scene (same import map + script as `racing-3d-title.html`,
   parameterized — see below). This is the "headless browser" doing the actual
   3D rendering; the .NET app never touches WebGL directly.
2. **Trigger**: bake runs whenever a category is created or saved
   (`CategoriesConfigViewModel.SaveAsync`/create flow) using that category's
   `Label` as the title text, and whenever the global Title Graphics style
   settings (colors/angle — see below) change on the Title Graphics tab, which
   invalidates and re-bakes every existing category's cached image.
3. **Capture**: after the WebView2 page signals the scene is rendered (a
   `window.chrome.webview.postMessage` call from the page once
   `stage.setObject` + camera framing complete), call
   `CoreWebView2.CapturePreviewAsync` to a transparent PNG stream.
4. **Cache**: save to `%LocalAppData%/UGL/TitleGraphics/{categoryId}.png`,
   alongside a small sidecar (`{categoryId}.json`) recording the settings hash
   used to produce it, so a later settings change or category rename can
   detect staleness and re-bake lazily instead of blocking category save.
5. **Fallback**: if the bake hasn't completed yet (first launch, still queued),
   `CategoryCard` shows the existing live 2D `CategoryTitleGraphic` control as
   a placeholder — this control already exists and looks reasonable — then
   swaps to the baked PNG once ready (`TitleGraphicsSettings`-style live
   update, same pattern already used for placement changes).

## Making colors/angle editable in the Title Graphics tab

Add controls to `TitleGraphicsConfigView` (reusing the existing
`ColorWheelPicker` control already in `Views/Controls/`):

- **Fill gradient**: 3 color pickers (top/mid/bottom stops)
- **Bevel color**: 1 color picker
- **Outline/depth color**: 1 color picker
- **Light colors**: 2 color pickers (front, side)
- **Rotation angle**: a slider (`-15°` to `+15°`, default `0°` per the
  straightened final state)

New `AppSettings` fields (mirror the existing `TitleGraphicsEnabled`/
`TitleGraphicsPlacement` pattern):

```
TitleGraphicsFillTopColor, TitleGraphicsFillMidColor, TitleGraphicsFillBottomColor,
TitleGraphicsBevelColor, TitleGraphicsOutlineColor,
TitleGraphicsLightFrontColor, TitleGraphicsLightSideColor,
TitleGraphicsRotationDegrees
```

`TitleGraphicsConfigViewModel` gains matching `[ObservableProperty]` fields,
pushes them live into `TitleGraphicsSettings` (already the static live-settings
bridge used for enabled/placement), and `SaveAsync` persists them plus
triggers the "re-bake every category" pass described above.

## Files touched

- `ViewModels/Config/TitleGraphicsConfigViewModel.cs` — new style properties, re-bake-all trigger on save
- `Views/Config/TitleGraphicsConfigView.axaml` — new color pickers + angle slider, preview swapped to show the baked-image path (or a live WebView2 preview panel, optional)
- `Core/Models/AppSettings.cs` — new fields listed above
- New: `Services/TitleGraphicsBaker.cs` — owns the offscreen WebView2, the parameterized HTML/JS template (based on `racing-3d-title.html`), capture, and disk cache read/write
- `ViewModels/Config/CategoriesConfigViewModel.cs` — call the baker on category create/save
- `Views/CategoryCard.axaml.cs` — prefer the cached PNG (`Image` control) when present; fall back to the existing live `CategoryTitleGraphic` control otherwise
- `ViewModels/TitleGraphicsSettings.cs` — extend the static bridge with the new style fields so live preview/re-bake can read current values

## Reference

- `racing-3d-title.html` (this project) — the exact three.js scene/geometry/material parameters to templatize for the baker
- `category_card_title_overlay/README.md` — the earlier 2D-layered-TextBlock version (`CategoryTitleGraphic`, already implemented) — kept as the fallback rendering path
