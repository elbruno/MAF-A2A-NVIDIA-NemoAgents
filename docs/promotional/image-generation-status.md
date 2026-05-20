# Image Generation Status (t2i)

Attempted to generate all requested promotional images with `t2i`:

- LinkedIn square (1:1)
- Blog hero (16:9)
- Repository social card

Current status: **blocked by provider authentication (HTTP 401 Unauthorized)** from both configured cloud providers in this environment.

Example error:

```text
Access denied due to invalid subscription key or wrong API endpoint.
```

## Commands attempted

```bash
t2i "<linkedin prompt>" --provider foundry-mai2 --width 1024 --height 1024 --out "docs/promotional/images/linkedin-square-1024x1024.png"
t2i "<blog prompt>" --provider foundry-mai2 --width 1360 --height 765 --out "docs/promotional/images/blog-hero-1360x765.png"
t2i "<social prompt>" --provider foundry-mai2 --width 1200 --height 630 --out "docs/promotional/images/repo-social-1200x630.png"
```

## Next action after credentials are fixed

Re-run the commands above; output images are expected in `docs/promotional/images/`.
