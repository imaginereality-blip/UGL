# Examples

UGL's Games tab → Art sub-tab has **two separate ComfyUI-backed generators**, each
with its own workflow file (Settings → 🖼 Scraper → ComfyUI Workflow JSON / ComfyUI
Cleanup Workflow JSON):

| Action | Workflow file | Input | Job |
|---|---|---|---|
| **Clean Cover for Card** | `GameCardCleanup_ComfyUI_workflow.json` | 1 image (the scraped cover) + 1 UGL-generated mask | Step 1 of a 4-step pipeline — regenerate *only* the top band the new logo will occupy, at the cover's own aspect ratio |
| **Generate Poster Collage** | `GameCard_ComfyUI_workflow.json` | up to 3 images (cover + screenshots) | Blend the game's own scraped imagery into one cohesive poster |

Both are optional — if a workflow path isn't configured (or ComfyUI isn't reachable),
"Clean Cover for Card" falls back to a plain local resize/reflow with no AI cleanup,
and "Generate Poster Collage" simply fails with a clear status message.

## Clean Cover for Card: the 4-step pipeline

This action is deliberately split into 4 separate steps, only the first of which
touches ComfyUI at all:

1. **Remove logo/text** — `GameCardCleanup_ComfyUI_workflow.json`, at the cover's own
   aspect ratio (see below).
2. **Resize to the card size, without cropping** — `GamesConfigViewModel.ResizeCoverFit`:
   contain-fits the whole cleaned image into the actual current card resolution, no
   content cut off.
3. **Upscale/increase detail if needed** — the same `ResizeCoverFit` call: leftover
   space from the aspect-ratio mismatch is filled with a blurred extension of the same
   artwork rather than a hard crop or blank bar, and any upscaling uses high-quality
   bicubic interpolation.
4. **Composite the real, un-regenerated logo** on top, in the top 25% — `CompositeLogo`
   (same step "Generate Poster Collage" uses; see **Logo overlay** below).

Steps 2-4 always run, even if step 1 is skipped (no cleanup workflow configured, or
ComfyUI unreachable) — the difference is only whether the source image had its
logo/text removed first.

### GameCardCleanup_ComfyUI_workflow.json (step 1 only)

**Masked inpainting via Flux.1 Fill Dev, at the cover's own aspect ratio — not a
whole-image img2img pass, and not pre-squashed into the card's tall shape.** Three
earlier versions of this got it wrong in different ways, worth keeping in mind if you
ever revisit this workflow:
- Running the entire cover through `KSampler` at a raised denoise (on a general
  txt2img SD checkpoint) to try to erase baked-in text either left the text intact
  (denoise too low) or visibly distorted/reinvented the whole picture (denoise high
  enough to erase text also repaints everything else) — there's no way to target just
  the logo/text region with a single global denoise value, hence the mask (below).
- Forcing the working canvas to the card's own tall aspect ratio (e.g. stretching a
  ~2:3 box cover into a ~1:2.6 canvas) before doing anything else *distorted the art
  itself*, independent of the inpainting. The card-shape reflow is a completely
  separate concern from logo removal and now happens only in step 2, in C#, using the
  non-destructive `ResizeCoverFit` (contain-fit, no cropping) — never inside this
  workflow.
- Switching to masked inpainting fixed the "distorts everything" problem, but the
  first checkpoint used (a general-purpose, anime-style SD1.5 model +
  `VAEEncodeForInpaint`) was simply bad at the specific job of "erase this text and
  reconstruct a plain background" — it hallucinated new garbled pseudo-text instead of
  a clean fill, a well-known weakness of asking a general txt2img model to do
  inpainting via a generic node rather than a model actually trained for it. That's
  why this workflow now uses **Flux.1 Fill Dev**, a model purpose-built for
  mask-conditioned inpainting (see **Model requirements** below) — its
  `InpaintModelConditioning` node takes the mask as direct model input rather than
  just noise-seeding a region and hoping, which is a meaningfully different (and more
  reliable) mechanism than the old `VAEEncodeForInpaint` approach.

This workflow's `ImageScale` nodes (3 and 4) use `{{WORK_WIDTH}}`/`{{WORK_HEIGHT}}` —
UGL computes these from the cover's actual pixel dimensions, rounded up to the nearest
multiple of 8, so the working canvas is essentially the cover's own size (Flux
generally prefers multiples of 16, but 8 still works — not worth the added complexity
of a separate rounding rule for this one workflow). From there: `LoadImage` (cover) →
`ImageScale` to the working size, `LoadImage` (mask, `{{IMAGE_2}}`) → `ImageScale` to
match → `ImageToMask` → that mask and the reflowed cover both feed
`InpaintModelConditioning` alongside the (Flux-guidance-wrapped) prompt conditioning,
so `KSampler` only ever regenerates the masked region. `ImageCompositeMasked` then
pastes that regenerated band back onto the **original** reflowed cover as a final
guarantee — Flux Fill's own architecture already preserves unmasked content well, but
this makes it byte-exact regardless. `SaveImage` outputs at this working size, still in
the cover's own aspect ratio — the card-shape reflow (steps 2-3) happens afterward,
back in C#.

### Model requirements (Flux.1 Fill Dev)

Four files, verified against ComfyUI's own official Flux Fill Dev example workflow
(not guessed):

| File | Get it from | Goes in |
|---|---|---|
| `flux1-fill-dev.safetensors` | [black-forest-labs/FLUX.1-Fill-dev](https://huggingface.co/black-forest-labs/FLUX.1-Fill-dev) | `ComfyUI/models/diffusion_models/` |
| `clip_l.safetensors` | [comfyanonymous/flux_text_encoders](https://huggingface.co/comfyanonymous/flux_text_encoders) | `ComfyUI/models/text_encoders/` |
| `t5xxl_fp16.safetensors` | [comfyanonymous/flux_text_encoders](https://huggingface.co/comfyanonymous/flux_text_encoders) | `ComfyUI/models/text_encoders/` |
| `ae.safetensors` | [black-forest-labs/FLUX.1-schnell](https://huggingface.co/black-forest-labs/FLUX.1-schnell) (the VAE is shared across the Flux family) | `ComfyUI/models/vae/` |

Total download is roughly 23GB at full precision. If you're short on VRAM, node 6
(`UNETLoader`)'s `weight_dtype` can be switched from `default` to `fp8_e4m3fn` for a
smaller memory footprint at a small quality cost — no other node needs to change.
There's also a smaller `t5xxl_fp8_e4m3fn.safetensors` text encoder if `t5xxl_fp16` is
too large for your setup (swap the filename in node 7, `DualCLIPLoader`).

### Where the mask actually comes from

UGL doesn't guess a fixed region — not every cover puts its logo/text in the same
place, so a fixed band couldn't generalize. Instead, two local, no-cloud detectors run
before the mask is built, and their results are unioned into it
(`GamesConfigViewModel.BuildRegionsMask`, sized to match `{{WORK_WIDTH}}`x`{{WORK_HEIGHT}}`
exactly):

- **`LogoRegionDetector`** — multi-scale, alpha-masked OpenCV template matching of the
  game's own scraped logo asset (`Editor.LogoPath`) against the cover. Since scrapers
  extract that asset from the box art in the first place, this is a template-matching
  problem, not a general logo-detection one: UGL already has the "answer," it just
  needs to find where on the cover it appears, at whatever scale.
- **`TextRegionDetector`** (optional — `ScraperSettings.TextDetectionModelPath`) — a
  PP-OCRv3 "DB" text-detection ONNX model, run through OpenCV's DNN module, to catch
  other stray badges (publisher/platform logos) that aren't an asset UGL has on file
  to template-match against. OpenCvSharp doesn't wrap OpenCV's own high-level
  `TextDetectionModel_DB` class, so this is a hand-rolled, simplified port of its
  pre/post-processing (verified against the model's own reference script,
  `opencv_zoo/models/text_detection_ppocr/ppocr_det.py`, for the exact preprocessing
  constants) — simplified in one place: the reference algorithm's "unclip" step
  properly offsets each detected polygon outward; this port uses a padded bounding
  rectangle instead, which is fine for a mask region (coverage, not precise geometry
  for text reading) but means it's a best-effort port, not a byte-exact
  reimplementation.

If both detectors find nothing (no logo asset scraped, no text-detection model
configured, or neither found a confident match), the mask falls back to the earlier
fixed-top-band guess (`BuildTopBandMask`: white over the top ~28%, feathered edge) so
the action still does *something* useful rather than nothing. Whatever regions are
used, each gets a small extra pad beyond what the detector itself applied, and the
whole mask gets a soft blur so there's no hard seam at any region's edge — via a real
area-averaged downscale (`Graphics.DrawImage` + `HighQualityBicubic`), not a naive
resize: an earlier version of `BuildRegionsMask` used the plain `new Bitmap(src, w, h)`
constructor for the downscale step, whose default resampling can flat-out miss an
isolated small-to-medium rectangle on an otherwise solid-black mask (it doesn't
average over each output pixel's source block) — that silently produced a near-empty
mask and made that version regenerate almost nothing at all, anywhere, even though the
ComfyUI call itself completed successfully. The mask is uploaded as the second
reference image alongside the cover, same as before.

### Tuning the denoise value

UGL substitutes `{{DENOISE}}` (node 14) — see [Token substitution](#token-substitution).
The default is **1.0**, Flux Fill's own documented default: unlike the old
`VAEEncodeForInpaint` approach, where denoise controlled how much of the existing
(noised) latent got overwritten and a value below 1.0 was often used to avoid
distorting content the mask wasn't supposed to touch, Flux Fill's
`InpaintModelConditioning` handles that separation architecturally — the masked region
is meant to be fully regenerated, and `ImageCompositeMasked` guarantees the unmasked
region stays untouched regardless of denoise. Lower it only if the regenerated region
looks too disconnected in style from the rest of the cover.

## GameCard_ComfyUI_workflow.json

Rather than generating a fresh illustration from a text description alone, this
workflow builds a **literal collage** of up to 3 of the game's own scraped images —
the main cover first, then screenshots, then alternate covers/artwork as filler — and
runs a **low-denoise img2img pass** over it (`ImageStitch` → cover-fit `ImageScale` →
`VAEEncode` → `KSampler`) purely to harmonize lighting/edges into a cohesive poster
feel. At a low denoise the sampler doesn't have room to invent new content — it starts
from the actual reference pixels rather than empty noise. An earlier version of this
workflow used IP-Adapter style-transfer conditioning instead; that produced results
that reinterpreted the references into a new illustration style rather than a
recognizable collage, which is why this workflow doesn't use IP-Adapter at all.

### Requirements

The two workflows use **different model families** now — the collage workflow
(`GameCard_ComfyUI_workflow.json`) references a stock **SDXL or SD1.5** checkpoint
(`CheckpointLoaderSimple`), swap it for whatever you have installed; the cleanup
workflow (`GameCardCleanup_ComfyUI_workflow.json`) uses **Flux.1 Fill Dev** specifically
(see **Model requirements** above), since it needs a model actually trained for
mask-conditioned inpainting rather than a general txt2img checkpoint. No IP-Adapter or
other custom node pack is required for either; `ImageStitch`, `ImageScale`,
`VAEEncode`, `KSampler`, `UNETLoader`, `DualCLIPLoader`, `VAELoader`,
`InpaintModelConditioning`, `DifferentialDiffusion`, and `FluxGuidance` are all core
ComfyUI nodes.

Separately from ComfyUI, "Clean Cover for Card"'s mask-detection step (not part of
either workflow file) needs `OpenCvSharp4`/`OpenCvSharp4.runtime.win` (a UGL.App
package reference, already wired up — nothing to install yourself) and, optionally, a
text-detection ONNX model for `TextRegionDetector` — e.g.
`text_detection_en_ppocrv3_2023may.onnx` from `github.com/opencv/opencv_zoo`
(`models/text_detection_ppocr/`), pointed at via Settings → Scraper → Text Detection
Model. Both detectors run fully locally through OpenCV — no cloud call either way.

### Tuning the denoise value

`KSampler`'s `denoise` (node 12) is the main knob:
- **Lower** (e.g. 0.2–0.3) → more literal, visible seams between the stitched images,
  minimal reinterpretation.
- **Higher** (e.g. 0.5+) → smoother blending, but the sampler starts taking more
  liberty with the actual content (tested: 0.5 measurably hallucinated a screenshot's
  content further from the source than 0.35 did) — go higher only if you want a more
  "reimagined" look and are OK with less faithful reproduction of the references. If
  what you actually want is to erase a baked-in logo/title, that's what "Clean Cover
  for Card" (above) is for instead — raising this workflow's denoise to try to erase
  text also erases/reinvents the screenshot content you're trying to keep.

### Token substitution

Both workflows substitute these literal tokens wherever they appear before submitting:
- `{{PROMPT}}` — a prompt built from the game's title/genre (or, for the cleanup
  workflow, a fixed "remove text/logo" instruction), appended to the positive
  `CLIPTextEncode` node's `text` field. Any other text in that field (style suffixes,
  etc.) is left as-is.
- `{{DENOISE}}` — the `KSampler` node's `denoise` input, as a string placeholder
  substituted with an actual number. Optional — a workflow that hardcodes a numeric
  `denoise` value instead is left untouched.
- `{{IMAGE_1}}`, `{{IMAGE_2}}`, `{{IMAGE_3}}` — the `image` input of each `LoadImage`
  node. UGL uploads each entry of the reference-image list it passes in, in order, and
  substitutes the resulting filename. For the collage workflow that's up to 3 actual
  photos (cover + screenshots); for the cleanup workflow it's always exactly 2 —
  `{{IMAGE_1}}` the cover, `{{IMAGE_2}}` the UGL-generated mask (see above), never
  reused/interchanged with each other. If fewer reference images were available than
  `{{IMAGE_n}}` tokens in a workflow, the remaining tokens reuse the last uploaded
  image rather than being left unresolved.
- `{{WORK_WIDTH}}`, `{{WORK_HEIGHT}}` — cleanup workflow only: the two `ImageScale`
  nodes' `width`/`height` inputs, substituted with the cover's own pixel dimensions
  rounded up to the nearest multiple of 8. Not used by the collage workflow, which has
  no equivalent per-run working size.

### Output resolution

The collage workflow samples at a fixed near-756×1968 canvas (752×1968, the nearest
multiple of 8) and adds a final `ImageScale` node before `SaveImage` as a coarse safety
net for the exact card width. The cleanup workflow does **not** do this — see [the
4-step pipeline](#clean-cover-for-card-the-4-step-pipeline) above for why forcing the
card's aspect ratio inside that workflow was actively harmful. Either way, the real,
pixel-exact enforcement to the *actual current* card resolution happens afterward in
C# (`GamesConfigViewModel.ResizeCoverFit`), which also handles the case where the live
card size differs from whatever resolution a workflow file happens to target, or
changes later (different window size, theme, etc.).

### Logo overlay (not part of either workflow)

The game's actual logo is **not** generated by ComfyUI — diffusion models can't
reliably reproduce exact logo art or text. Instead, after a workflow returns its image
(or after "Clean Cover for Card" falls back to a plain local resize with no ComfyUI at
all), UGL pastes the game's real scraped logo (`Editor.LogoPath` — ScreenScraper's
`wheel`/`wheel-hd` art or TheGamesDB's `clearlogo` type; IGDB doesn't expose a clean
logo) on top in a separate C# compositing step: scaled to fit within the top 25% of
the card, horizontally and vertically centered within that band, with a soft drop
shadow for legibility. If no logo was scraped for a game, the card is produced without
one. Both workflows' negative prompts (`text, logo, ...`) discourage the model from
drawing its own competing logo/text into the scene.
