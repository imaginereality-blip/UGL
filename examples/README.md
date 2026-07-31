# Examples

## GameCard_ComfyUI_workflow.json

A ComfyUI workflow (exported in API format) used with UGL's Games tab → **Generate
Card (ComfyUI)** action (Settings → 🖼 Scraper → ComfyUI Workflow JSON). References
specific local models (`qwen_3_4b_fp8_mixed.safetensors`, `z_image_turbo_bf16.safetensors`,
`ae.safetensors`) — swap the `CLIPLoader`/`UNETLoader`/`VAELoader` node inputs for
whatever you have installed.

The positive `CLIPTextEncode` node's `text` field contains the literal token
`{{PROMPT}}` — UGL substitutes this with a prompt built from the game's title/genre
before submitting. Any other text in that field (style suffixes, etc.) is left as-is.
