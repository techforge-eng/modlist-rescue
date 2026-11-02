# How to publish this on GitHub (and link it from Nexus)

You only do the setup once. After that, publishing a new version is one command (or a few clicks).

---

## Option A — GitHub website only (no commands)

### 1. Create the repository
1. Go to <https://github.com/new>.
2. Repository name: `ModlistRescue` (or whatever you like).
3. Set it to **Public**. Leave "Add a README" **unticked** (this folder already has one).
4. Click **Create repository**.

### 2. Upload these files
1. On the new repo page, click **uploading an existing file**.
2. Drag in everything from this folder **except** any `ModlistRescue.exe` you built locally:
   - `ModlistRescue.cs`
   - `build.bat`
   - `techforge.ico`
   - `README.md`
   - `LICENSE`
   - `.gitignore`
   - the `.github` folder (with `workflows/release.yml` inside)
3. Click **Commit changes**.

> Tip: the GitHub upload box can be fussy about folders. If the `.github/workflows` folder
> won't drag in, create the file manually: **Add file → Create new file**, name it
> `.github/workflows/release.yml`, and paste in the contents of that file.

### 3. Make the first release (this builds the exe for you)
1. In your repo, go to the **Releases** section (right-hand side) → **Create a new release**.
2. **Choose a tag** → type `v1.0` → **Create new tag**.
3. Title: `v1.0`. Add a few notes if you like.
4. Click **Publish release**.
5. Go to the **Actions** tab — you'll see "Build and Release" running. When it finishes
   (a minute or two), `ModlistRescue.exe` is automatically attached to your `v1.0` release.

Done. Your download link is: `https://github.com/<your-username>/ModlistRescue/releases/latest`

---

## Option B — Command line (if you have Git installed)

```bash
cd path/to/this/folder
git init
git add .
git commit -m "Tech-Forge's Modlist Rescue v1.0"
git branch -M main
git remote add origin https://github.com/<your-username>/ModlistRescue.git
git push -u origin main

# tag a release - this triggers the build + publishes the exe
git tag v1.0
git push origin v1.0
```

---

## Releasing future versions

Change the code, commit it, then push a new tag:

```bash
git add .
git commit -m "Fixes / new version"
git tag v1.1
git push && git push origin v1.1
```

GitHub rebuilds and publishes `ModlistRescue.exe` on the new release automatically.

---

## Linking it from your Nexus Mods page

Nexus prefers files hosted on Nexus, but **tools are commonly linked to GitHub**. On your
Nexus page:

- Put the description, images and instructions on Nexus.
- In the description, add a clear button/line:
  **"Download: [GitHub Releases](https://github.com/<your-username>/ModlistRescue/releases/latest)"**
- If you'd still like a file on Nexus itself, upload a small **text file or zip containing a
  link + the source**, rather than the exe, to avoid the quarantine.

This keeps the exe off Nexus's scanner entirely, while GitHub serves the build straight from
your public source.

---

## If you want the exe on Nexus too, later

To get a quarantined exe reviewed on Nexus:
1. Upload the **source** (this whole repo as a zip) as a separate file, or link this GitHub repo.
2. Add a note for moderators: *"Open-source Skyrim version downgrader. The exe downloads and runs
   Valve's SteamCMD, which triggers a false-positive. Source and CI build are public at <repo link>."*
3. Optionally link a [VirusTotal](https://www.virustotal.com) scan and point out the detections are
   generic/heuristic false positives.
