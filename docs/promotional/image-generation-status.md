# Image Generation Status (t2i)

Attempted to generate all requested promotional images with `t2i`:

- LinkedIn square (1:1)
- Blog hero (16:9)
- Repository social card

Current status: **blocked by provider timeouts** in this environment for `foundry-gpt-image-2` (retries exhausted).

Example error:

```text
Retry failed after 4 tries. The operation was cancelled because it exceeded the configured timeout.
```

## Commands attempted

```bash
t2i "<linkedin prompt>" --provider foundry-gpt-image-2 --width 1200 --height 1200 --out "docs/promotional/images/linkedin-square-1200x1200.png"
t2i "<blog prompt>" --provider foundry-gpt-image-2 --width 1600 --height 904 --out "docs/promotional/images/blog-hero-1600x904.png"
t2i "<social prompt>" --provider foundry-gpt-image-2 --width 1200 --height 632 --out "docs/promotional/images/repo-social-1200x632.png"
```

## Next action after credentials are fixed

Re-run the commands above; then crop to the target display sizes where needed (for example, `1600x904 -> 1600x900` and `1200x632 -> 1200x630`) and replace the placeholders in `docs/promotional/images/`.
