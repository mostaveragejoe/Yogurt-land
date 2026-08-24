# How to run the terrain performance test (Windows)

**What this is.** A small test program that draws the game's terrain, digs
tunnels in it continuously for 30 seconds, and measures whether the frame rate
stays smooth. It opens a window, runs, prints numbers, and closes itself.

**Why it needs you.** The cloud container Claude runs in has no graphics card.
This measurement is meaningless without real hardware.

**Time:** about 10 minutes, most of it waiting.

**You cannot break anything.** The test only reads the project and draws to a
window. It writes one screenshot and one log file. It does not modify the game.

---

## Step 1 — Open PowerShell

Press `Windows key`, type `powershell`, press Enter. A blue window opens.
Every command below gets pasted in and followed by Enter.

## Step 2 — Download the project

```powershell
cd $HOME\Documents
git clone https://github.com/mostaveragejoe/Yogurt-land.git
cd Yogurt-land
git checkout claude/colony-sim-next-steps-9t9c26
```

If you already have the project somewhere, instead do:

```powershell
cd <path-to-your-existing-copy>
git fetch origin
git checkout claude/colony-sim-next-steps-9t9c26
git pull origin claude/colony-sim-next-steps-9t9c26
```

**Expected:** a line like `Switched to branch 'claude/colony-sim-next-steps-9t9c26'`.

## Step 3 — Build the test program

```powershell
dotnet build prototypes\terrain-spike\render\TerrainSpikeRender.csproj -c Release
```

**Expected:** `Build succeeded.` after 10-60 seconds.

**If it says errors instead:** copy the whole red text back to Claude. The code
was written without a compiler available, so a typo is possible. This is the
one step where that would show up.

## Step 4 — Find your Godot .exe

You need the path to Godot. In the folder where you unzipped Godot there are
**two** .exe files. You want the one ending in **`_console.exe`** — the other
one runs without a terminal and you would not see any of the numbers.

Set it once so the later commands are short (edit the path to match yours):

```powershell
$godot = "C:\path\to\Godot_v4.7.1-stable_mono_win64_console.exe"
```

Check it worked:

```powershell
& $godot --version
```

**Expected:** something like `4.7.1.stable.mono.official`.

**If "not recognized":** the path is wrong. Open the Godot folder in File
Explorer, click the address bar, copy the path, and rebuild the line above.

## Step 5 — Run the test (first pass, Vulkan)

```powershell
& $godot --path prototypes\terrain-spike\render --rendering-driver vulkan --max-fps 0 -- backend=gridmap_two octant=32 styles=1 2>&1 | Tee-Object -FilePath "$HOME\Documents\terrain-vulkan.txt"
```

A window opens showing a brown/grey stone cross-section. **Leave it alone** —
don't click, resize, minimise, or move it. Doing so changes what is measured.

It closes itself after roughly 35 seconds.

## Step 6 — Run the test (second pass, D3D12)

```powershell
& $godot --path prototypes\terrain-spike\render --rendering-driver d3d12 --max-fps 0 -- backend=gridmap_two octant=32 styles=1 2>&1 | Tee-Object -FilePath "$HOME\Documents\terrain-d3d12.txt"
```

Same again. Leave the window alone.

## Step 7 — Send the results

Two files are now in your Documents folder:

- `terrain-vulkan.txt`
- `terrain-d3d12.txt`

Open each in Notepad, copy everything, paste back to Claude. Or just paste the
lines starting with `RESULT`.

---

## The one line that decides the whole thing

```
RESULT VERDICT_frame_rate_clause=PASS (p99 4.210 ms vs 16.6 ms budget)
```

- **PASS** — the terrain design is fast enough. This unblocks a decision that
  has been waiting since July.
- **FAIL** — also useful. It means we found a real problem now, on a test
  program, instead of a year into building the game.

Either result is a good outcome. There is no wrong answer to report.

## Sanity check before trusting any of it

```
RESULT software_rasterizer=false
```

Must say **false**. If it says `true`, your graphics card isn't being used and
every timing number is void — tell Claude and stop there.

## If something goes wrong

Copy the error text back to Claude. Useful details: which step number, and
what the window did (opened and closed instantly / never opened / froze).

Nothing here is destructive, so retrying is always safe.
