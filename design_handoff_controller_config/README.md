# Handoff: Controller Configuration UI (UGL Settings → Controllers)

## Overview
A full-screen controller configuration surface for UGL. It lets the user pick a detected
device, see a diagram of that device, remap any control by pressing a physical button, and
manage named per-device profiles (save / save-as / load / reset / export / import).

Three device classes are supported, each with its own diagram and control list:
1. **XInput Gamepad** (Xbox-style pad)
2. **Light Gun** (X-Gunner)
3. **Steering Wheel** (Logitech G923-style, wheel + pedals + shifter)

## About the design files
`Controller Config Utility.dc.html` (+ `support.js`, its runtime) is a **design reference
prototype written in HTML/JS**. It is not production code and should not be embedded or
ported literally. The task is to **recreate this design inside the existing UGL app**:

- Target: **.NET / Avalonia**, `src/UGL.App` — AXAML views + `ViewModels/Config`.
- It belongs in the existing Settings shell: `Views/ConfigEditorView.axaml`, as a
  Controllers/Peripherals tab, following the sidebar + tab-content structure already there.
- Follow existing conventions: `x:DataType` typed bindings, `UserControl.Styles` selectors,
  `{DynamicResource UGL.*}` theme keys from `UGL.Themes/ThemeKeys.cs` — **never hardcoded
  hex** (the prototype's hex values below are only there to describe intent).
- Data/services already present: `UGL.Input/PeripheralRegistry.cs` (device list, hardware
  paths, player index, `config/controllers.json`), `XInputPollingService`, `RawInputService`.

## Fidelity
**Mid-to-high fidelity.** Layout, information architecture, control lists, flows and copy
are settled and should be followed closely. Two areas are explicitly **not** final and
should be improved during implementation:
- **Controller artwork** — the prototype's inline SVG diagrams are approximations. Replace
  them with proper vector/raster art per device (see *Assets*).
- **Visual polish** — spacing, elevation, hover/focus states should be brought in line with
  the rest of UGL rather than copied pixel-for-pixel.

## Layout (single screen, no page scroll)
Everything fits one screen; only the mapping list scrolls.

```
┌──────────────────────────────────────────────────────────────┐
│ Header: "Control Center" / "XInput Device Configuration"     │  fixed
├──────────────────────────────────────────────────────────────┤
│ [ XInput Gamepad | Light Gun | Steering Wheel ]  (segmented) │  fixed
│ Device dropdown + "Connected — <name>"    Profile actions →  │  fixed
├───────────────────────────────┬──────────────────────────────┤
│ Diagram card (1.15fr)         │ Mapping list (1fr, scrolls)  │  fills
│  clickable hotspots on art    │  Label · [current] · Remap   │
└───────────────────────────────┴──────────────────────────────┘
```
- Outer container: `height:100vh`, `overflow:hidden`, max content width 1320px, 28px
  horizontal padding, 14px top padding.
- Two-column body: CSS grid `1.15fr 1fr`, gap 20px, both columns `min-height:0`.
- Diagram card: surface background, 14px padding, SVG scales with `width:100%;height:100%`.
- Mapping list: vertical flex, 6px gap, `overflow-y:auto`, 6px thin scrollbar.
- Wheel tab adds a **Shifter mode** segmented control (Sequential / H-Pattern / Paddles Only)
  above the two-column body.

### Components
**Header** — title 18px heading font; subtitle 10px uppercase, letter-spacing .09em, 50% opacity.

**Tab segmented control** — 3 options, 8px×20px padding, 13px heading font.

**Device dropdown** — labelled "Detected device", 13px, min-width 280px. Below it a status
line: 6px accent dot + `Connected — <device name>` at 10px accent color.
Populate from `PeripheralRegistry.KnownDevices` filtered by device class, using
`DisplayLabel` (already disambiguates two identical models as P1/P2 or #1/#2).

**Profile bar** (right-aligned, 7px gap, wraps): outline tag `Profile: <name>`, then
buttons `Save`, `Save As…`, `Load…`, `Reset` (secondary) and `Export`, `Import` (ghost).
All 12px / 7px×14px padding.

**Diagram card** — device art with transparent circular/rect hotspots over each control.
Clicking a hotspot starts remap for that control. The active control's hotspot fills with
accent color; its row in the list also gets an accent border + 10% accent tint background.
Caption under the art, 11px, 55% opacity, e.g. "Xbox-style wireless controller — click a
control to remap".

**Mapping row** — flex, space-between, 8px×12px padding, 1px divider border, `radius-md`:
control label (12px) · current binding as an outline tag (11px, min-width 90–100px, centered)
· `Remap` ghost button (11px).

**Dialogs** (modal, backdrop scrim):
- *Press any button* — title "Press any button", body "Waiting for input for **<control>** on
  <device>…" with a pulsing ellipsis (1.1s ease-in-out opacity .35→1), Cancel button.
- *Save Profile As* — text field "Profile name", Cancel / Save.
- *Load Profile* — one full-width secondary button per saved profile, Cancel.

**Toast** — bottom-center, surface bg, 1px divider, 10px×20px, `radius-md`, large shadow,
13px, auto-dismiss after 2400ms.

## Control lists (exact)
**Gamepad** (16): Left/Right Trigger (LT/RT), Left/Right Bumper (LB/RB), Left/Right Stick
Click (L3/R3), D-Pad Up/Down/Left/Right, A, B, X, Y, View (Back), Menu (Start).

**Light gun** (11): Trigger (RT), Pump/Reload (LT), A (front), B (side), Back, Start,
Home/Guide, D-Pad Up/Down/Left/Right.

**Wheel** (15 + shifter): Y, X, B, A, Start, Back, D-Pad ×4, Left Paddle (LB), Right Paddle
(RB), Gas Pedal (RT axis), Brake Pedal (LT axis), Clutch Pedal (Left Stick Y axis).
Shifter set depends on mode:
- *Sequential*: Shift Up (RB), Shift Down (LB)
- *H-Pattern*: Gear 1 (X), 2 (A), 3 (B), 4 (Y), 5 (LB), 6 (RB), Reverse (L3 + R3)
- *Paddles Only*: none — show the note "Shifting is handled entirely by the L/R paddles —
  no separate shifter mapping needed."

Binding value pool (buttons): A, B, X, Y, LB, RB, LT, RT, Back, Start, L3, R3, DPad Up/Down/
Left/Right. Axis pool (pedals): RT (axis), LT (axis), Left Stick Y (axis), Right Stick Y
(axis), Left Stick X (axis).

## Interactions & behavior
1. **Remap** — click a diagram hotspot or a row's `Remap`. Capture dialog opens; app listens
   for the next physical input from *that* device (XInput poll / RawInput). On input: close
   dialog, write the binding, toast `<Control> mapped to <Binding>`. Escape or Cancel aborts.
   *(The prototype fakes capture with a 1.4s timer and a random binding — replace with real
   input capture.)*
2. **Conflict handling** — not designed yet. Recommended: if the captured binding is already
   used on the same device, warn inline or swap. Confirm with design before implementing.
3. **Tab switch** — swaps diagram, control list and device dropdown; each tab remembers its
   own selected device.
4. **Device change** — loads that device's last-used profile; toast
   `Auto-detected profile "<name>" loaded for <device>`.
5. **Save** — toast `Profile "<name>" saved`. **Save As** — new named profile becomes current.
   **Load** — pick from saved list. **Reset** — restores factory defaults for the current
   device, toast `Mapping reset to factory default`.
6. **Export / Import** — write/read a profile JSON file. Prototype toasts a path; real
   implementation should use a file dialog (or UGL's own path config).
7. **Controller navigation is required** — this screen must be fully operable with a gamepad
   like the rest of Settings: D-pad moves row focus, A = remap, B = back, and the focused row
   uses the existing `cfgField.fieldFocused` accent-border style.

## State
- `SelectedTab` (gamepad | lightgun | wheel)
- `SelectedDeviceId` per tab
- `ShifterMode` (sequential | hpattern | paddles)
- `Mappings` keyed by `tab:deviceId` → `{ controlId: binding }`
- `CurrentProfileName` and `SavedProfiles` keyed by `tab:deviceId`
- `Dialog` (null | capture{controlId,label,device} | saveAs{name} | load)
- `Toast` (message + 2400ms timer)

Persistence should go through the existing config layer (`UGL.Configuration`, alongside
`config/controllers.json`), keyed by the device's **stable hardware path**, not display name.

## Design tokens
Prototype values (map each to the `UGL.*` theme key, don't hardcode):

| Prototype | Value | UGL key |
|---|---|---|
| bg | `#141210` | `UGL.BackgroundColor` |
| surface | `#1e1a16` | `UGL.SurfaceColor` |
| text | `#f1eee7` | `UGL.TextPrimaryColor` |
| divider | `rgba(241,238,231,0.16)` | `UGL.Divider` |
| accent | design-system accent | `UGL.AccentColor` |
| active row tint | 10% accent | `UGL.SidebarSelectionTint` |
| modal scrim | dark scrim | `UGL.ScrimHeavy` |

Type scale: 18 (title) / 13 (controls, tabs) / 12 (row label) / 11 (tags, buttons, captions) /
10 (status, overline). Spacing: 6 / 8 / 12 / 14 / 20 / 28. Radius: `radius-md` (~6px) for rows,
tags and cards; pill for tags. Shadow: `0 14px 34px rgba(0,0,0,.6)` for dialogs/toast.
SVG art shading: body gradient `#2b2621 → #171310`, strokes `#3a332c` / `#4a4038`, recessed
parts `#0f0d0b`–`#221d19`; face-button accent strokes Y `#5a4a35`, X `#355a45`, B `#7a3550`,
A `#3a6a4a`.

## Assets
None shipped. The diagrams are hand-authored SVG in the prototype and are the weakest part of
it — **replace them**. Recommended: commission or source per-device vector art (top-down
gamepad, side-profile light gun, wheel + pedal set + shifter), export as SVG or high-res PNG,
and keep a separate hotspot map (control id → shape/coords) so art and hit regions can be
updated independently. Icons should use the app's existing Segoe Fluent Icons glyphs.

## Screenshots
`screenshots/` — reference captures of the prototype:
- `01-gamepad.png` — XInput Gamepad tab
- `02-lightgun.png` — Light Gun tab
- `03-wheel-sequential.png` — Steering Wheel, sequential shifter
- `04-wheel-hpattern.png` — Steering Wheel, H-pattern shifter

## Files in this bundle
- `Controller Config Utility.dc.html` — the interactive design reference (open in a browser)
- `support.js` — runtime required by the HTML file; not part of the design
- `screenshots/` — PNG captures of each tab

## Repo touchpoints
- `src/UGL.App/Views/ConfigEditorView.axaml` — settings shell, styles to reuse
- `src/UGL.App/ViewModels/Config/` — where the new view model belongs
- `src/UGL.Input/PeripheralRegistry.cs` — device list, display labels, persistence
- `src/UGL.Input/XInputPollingService.cs`, `RawInputService.cs` — input capture
- `src/UGL.Themes/ThemeKeys.cs` — theme tokens
