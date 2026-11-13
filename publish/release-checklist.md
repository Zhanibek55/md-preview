# Release checklist — publish MdPreview free & open

Goal: ship it free and open, then **measure demand before spending any time or money** on paid tiers
(buyer‑first; a new listing gets no traffic on its own). Nothing here costs money. Minimum owner steps
are marked **[owner]** (only you can do them — accounts/publishing); the rest an agent prepared already.

## A. Minimum to go live (GitHub + donate link)

1. **[owner] Put your name/handle in `LICENSE`** — replace `<your name or GitHub handle>` on the copyright line.
2. **[owner] Create a public GitHub repo** named `md-preview` (empty, no README/License — we already have them).
3. **Push the project as its own repo.** From the monorepo (uncommitted vendor/bin are ignored):
   ```bash
   # copy just this project out, as a clean standalone repo
   robocopy projects\md-preview %TEMP%\md-preview /E /XD bin obj node_modules .git ^
     src\MdPreview\assets\vendor package\staging package\out tests\out
   cd %TEMP%\md-preview
   git init -b main
   git add -A
   git commit -m "MdPreview: rich Markdown preview for Windows Explorer"
   git remote add origin https://github.com/<you>/md-preview.git
   git push -u origin main
   ```
   (An agent in an owner session can run this once the repo exists and git is signed in.)
4. **Build the downloadable package** and attach it to a GitHub Release:
   ```powershell
   cd projects/md-preview/product
   ./package/build-package.ps1       # -> package/out/MdPreview-<version>.zip
   ```
   **[owner]** On GitHub → Releases → *Draft a new release* → tag `v0.1.0` → drag in `MdPreview-<version>.zip` → Publish.
   (This ZIP is what the README's download link and users need.)
5. **[owner] Create a donate link** (free, pays out to PayPal — works from Kazakhstan):
   - **Ko‑fi** (ko‑fi.com) or **Payhip** "pay what you want". Connect **PayPal** as the payout.
   - *Not GitHub Sponsors* — it pays via Stripe, which isn't available in Kazakhstan.
   - Paste the URL into the **Support** line at the bottom of `README.md`, commit, push.

That is the whole free launch: an open repo + a downloadable release + a donate link.

## B. One honest announcement (optional, same day)

Post once, where the community allows it, disclosing it's **your own free, open‑source tool** (text in
[`announce.md`](announce.md)). No fake accounts, no reposting across many subs, no paid/edited reviews — that
breaks platform rules and ours. Good first spots: **Show HN**, and one relevant subreddit that permits self‑promo
(e.g. r/opensource, r/coolgithubprojects) — read its rules first.

## C. Measure for 2–4 weeks (decide, don't guess)

Watch: ⭐ stars, Release **download count**, donations, issues/feedback. This is the real buyer test.
- **Signal** (steady downloads / a few donations / people asking for a paid/Pro version) → consider a cheap
  **Pro tier** or a Microsoft Store listing then, not before.
- **No signal** → keep it as a free portfolio piece; stop investing. Reuse the engine (fast offline WebView2
  renderer + install/warm‑up/packaging) for a product with proven willingness to pay.

## D. Later / only if there's a signal (needs more work — not now)

- **Microsoft Store** (free listing): needs MSIX packaging + the free developer registration. Listing text is
  ready in [`microsoft-store.md`](microsoft-store.md). Kazakhstan payouts on Store are unconfirmed; a free listing
  with a donate link sidesteps that.
- **winget** (`winget install`): needs a proper `.exe`/`.msi` installer (our `.cmd` isn't a supported winget
  installer type). An Inno Setup per‑user installer would enable both winget and a nicer one‑click setup.
- **Paid Pro tier**: gate Mermaid/math/outline behind a one‑time key (Payhip, PayPal) — only if B/C show demand.
