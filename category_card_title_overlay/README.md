# Category Card Title Overlay — Implementation Spec

Final direction: 28b from `UGL Category Card Title Styles.dc.html` — red→orange
gradient fill, black+purple double outline, straight-down cyan/magenta 3D
extrusion, -5° italic tilt.

## Important constraint: Avalonia has no text-stroke

The design mockups used CSS `-webkit-text-stroke` to preview the outline/extrusion
quickly. **Avalonia's `TextBlock` has no stroke property** — there is no direct
equivalent. Every "stroke" in the mockup must be built the way it's actually done
in native UI: a ring of solid-color copies of the same text, offset a few px in
each direction around a center, sitting behind the real fill text. This is the
same technique already sketched for recommendation #16 (the original comic-title
concept). Details below.

## Layer stack (back to front)

All layers are `TextBlock`s bound to the same `{Binding Category.Name}`, absolutely
positioned inside one `Grid` (or `Canvas`) with `HorizontalAlignment="Center"
VerticalAlignment="Center"`, all `FontFamily="Arial Black"` (fallback: bold system
sans), `FontWeight="Black"`, `FontStyle="Italic"`, same `FontSize` as each other.

1. **Magenta extrusion (thin/back)** — 1 layer, offset furthest, `Opacity="0.5"`, color `#6E1B57`
2. **Cyan extrusion (3 layers, straight down)** — offsets step upward in even increments toward layer 6 (see table), colors darken with distance: `#0C6874` → `#0F7784` → `#1596A6`
3. **Purple outline ring** — see "Building the outline" below, color `#A855FF`
4. **Black inner outline ring** — same technique, smaller radius, color `#0A0A12`
5. **Fill text (front)** — `LinearGradientBrush`, see gradient stops below

Wrap the whole Grid in one `Border` (or the Grid itself) with a single shared
`RenderTransform` (`RotateTransform Angle="-5"`) and one shared `Effect`
(`DropShadowEffect`) — do not put the rotation or shadow on individual layers.

### Extrusion offsets (28b: straight down, no horizontal shift)

At the reference mock's scale (54px font); scale proportionally to your actual
card's title font size (`FontSize × offset/54`):

| Layer | Vertical offset | Color | Opacity |
|---|---|---|---|
| Magenta (furthest) | +14px down | `#6E1B57` | 0.5 |
| Cyan step 1 | +10.5px down | `#0C6874` | 1.0 |
| Cyan step 2 | +7px down | `#0F7784` | 1.0 |
| Cyan step 3 | +3.5px down | `#1596A6` | 1.0 |
| (Purple/black outline + fill sit at +0) | | | |

Because the steps are only ~3.5px apart at this scale, the layers overlap almost
entirely — this is intentional, it reads as one solid gradient-colored extrusion
block, not as separate stripes.

### Building the outline (no native text-stroke)

For each outline ring (purple then black), place 8 copies of the same TextBlock
in a small circle around the center point, all in that ring's solid color, no
gradient. Radius ≈ desired stroke width. Directions: N, S, E, W, NE, NW, SE, SW.

- **Purple ring**: radius 12px (matching 28b's chosen outline weight) — the 8
  purple copies at `(±12,0) (0,±12) (±8.5,±8.5)` (8.5 ≈ 12/√2 for diagonals)
- **Black ring**: radius 4px, same 8-direction pattern, sits on top of the purple
  ring (i.e., added after it in the Grid's children so it paints over it)

This gives a clean, roughly-circular stroke around the glyph silhouette without
needing per-glyph path outlining. (16 extra TextBlocks total — trivial for a
5-card pool that only re-renders this on selection/data change, not per frame.)

### Fill gradient (front-most layer)

```xml
<TextBlock Text="{Binding Category.Name}" FontFamily="Arial Black" FontWeight="Black"
           FontStyle="Italic" FontSize="54">
  <TextBlock.Foreground>
    <LinearGradientBrush StartPoint="0%,0%" EndPoint="0%,100%">
      <GradientStop Color="#D6231E" Offset="0.0" />
      <GradientStop Color="#FF7A2E" Offset="0.38" />
      <GradientStop Color="#FFA23C" Offset="0.50" />
      <GradientStop Color="#FF7A2E" Offset="0.62" />
      <GradientStop Color="#D6231E" Offset="1.0" />
    </LinearGradientBrush>
  </TextBlock.Foreground>
</TextBlock>
```

### Shared transform + shadow (on the outer Grid/Border, once)

```xml
<Border.RenderTransform>
  <RotateTransform Angle="-5" />
</Border.RenderTransform>
<Border.Effect>
  <DropShadowEffect Color="#000000" BlurRadius="14" OffsetX="0" OffsetY="10" Opacity="0.5" />
</Border.Effect>
```

## Where this goes in the codebase

`CategoryCard.axaml` — add a new `Grid` (call it `TitleOverlay`) as a sibling to
`PlaceholderText`/`CoverImage` inside the existing `Grid`, bound
`IsVisible="{Binding !HasCoverArt}"` (or whatever the real "no art" flag ends up
being called — check `CategoryCardViewModel`). Font size should scale with the
card's actual rendered size, not a fixed 54px — reuse the same
`OnCardSizeChanged`/`CardDimensionInfo` pattern already in `CategoryCard.axaml.cs`
to compute a proportional `FontSize` (the mock used 54px against a ~340×220 card,
so roughly `cardHeight × 0.25`).

Since this is ~22 static `TextBlock`s per card and the category pool is small and
bounded (5 instances, per the existing code comments), building it once in
`OnDataContextChanged`/`SetCoverImage`-style code-behind (not re-created every
frame) keeps this cheap — same performance posture as the existing selection glow.

## Files
- `../UGL Category Card Title Styles.dc.html` — full visual exploration (28
  turns) that led to this direction; `#28b` is the exact option this spec
  documents.
