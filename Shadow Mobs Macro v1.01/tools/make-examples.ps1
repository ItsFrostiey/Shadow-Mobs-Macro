# Makes the example pictures shown beside each calibration step: a crop of a reference screenshot
# with everything dimmed except the thing to mark, which gets a green box.
# Output goes to assets\examples, which build.ps1 embeds in the exe. Rerun, then build, if the
# reference screenshots change.
#   tools\source\jobs-page.png   a 1440p Jobs page (saved at 2000x1124)
#   tools\source\bank-page.png   a 1440p Bank page (saved at 2000x1124)
#   tools\source\safehouse-page.png  the Safehouse page (2000x1124)
#   tools\source\pet-owned.png       the Animals window as it opens, OWNED tab (2000x1123)
#   tools\source\pet-training.png    Animals > Animal Training, pet resting (2000x1124)
#   The stamina_*.png examples were made the same way by an earlier version and are kept as they are.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'assets\examples'
New-Item -ItemType Directory -Force $out | Out-Null

$green = [Drawing.Color]::FromArgb(0x6C, 0xE8, 0x5A)
function Example($source, $name, $target, $context, $maxW) {
    $c = New-Object Drawing.Rectangle $context[0], $context[1], $context[2], $context[3]
    $t = New-Object Drawing.Rectangle ($target[0] - $context[0]), ($target[1] - $context[1]), $target[2], $target[3]
    $b = $source.Clone($c, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $dim = New-Object Drawing.Region (New-Object Drawing.Rectangle 0, 0, $b.Width, $b.Height)
    $dim.Exclude($t)
    $g.FillRegion((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(150, 0, 0, 0))), $dim)
    $g.DrawRectangle((New-Object Drawing.Pen $green, ([Math]::Max(3, $b.Width / 240))), $t)
    $g.Dispose()
    if ($b.Width -gt $maxW) {
        $h = [int]($b.Height * $maxW / $b.Width)
        $small = New-Object Drawing.Bitmap $maxW, $h
        $g = [Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($b, 0, 0, $maxW, $h)
        $g.Dispose()
        $b.Dispose()
        $b = $small
    }
    $b.Save((Join-Path $out "$name.png"), [Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}

$jobs = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'source\jobs-page.png'))
$card = @(325, 350, 1615, 110)
Example $jobs 'jobs_heading'   @(312, 166, 104, 38)    @(290, 140, 760, 100)     720
Example $jobs 'jobs_level'     @(296, 74, 46, 40)      @(232, 6, 750, 118)       720
Example $jobs 'jobs_energy'    @(1438, 9, 424, 36)     @(1300, 2, 610, 125)      720
Example $jobs 'jobs_xp'        @(374, 78, 324, 32)     @(232, 6, 750, 118)       720
Example $jobs 'jobs_name1'     @(352, 358, 362, 34)    $card                     720
Example $jobs 'jobs_button1'   @(1498, 372, 394, 62)   $card                     720
Example $jobs 'jobs_mastery1'  @(355, 422, 892, 26)    $card                     720
Example $jobs 'jobs_name2'     @(352, 468, 192, 34)    @(325, 350, 1615, 220)    720
Example $jobs 'jobs_list'      @(322, 228, 1620, 874)  @(270, 140, 1730, 984)    720
Example $jobs 'jobs_jobsbtn'    @(12, 194, 240, 46)     @(0, 120, 520, 260)       720
Example $jobs 'jobs_overlay'   @(0, 0, 228, 130)       @(0, 0, 1000, 330)        720
Example $jobs 'jobs_page'      @(10, 192, 245, 50)     @(0, 0, 2000, 1124)       720
$jobs.Dispose()

# tools\source\bank-page.png: the user's Bank page (saved at 2000x1124).
$bank = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'source\bank-page.png'))
Example $bank 'bank_heading'   @(308, 166, 104, 38)    @(290, 140, 760, 100)     720
Example $bank 'bank_onhand'    @(985, 358, 421, 36)    @(830, 330, 610, 140)     720
Example $bank 'bank_deposit'   @(845, 625, 287, 52)    @(830, 540, 610, 150)     720
Example $bank 'bank_amount'    @(845, 468, 582, 70)    @(830, 440, 610, 250)     720
Example $bank 'bank_withdraw'  @(1142, 552, 285, 62)   @(830, 440, 610, 250)     720
Example $bank 'bank_bankbtn'   @(12, 504, 240, 48)     @(0, 400, 520, 260)       720
Example $bank 'bank_page'      @(845, 625, 287, 52)    @(0, 0, 2000, 1124)       720
$bank.Dispose()

# Auto-train pet: three screenshots (the Safehouse page, the Animals window on OWNED, then on Animal Training).
$owned = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'source\pet-owned.png'))
Example $owned 'pet_owned'       @(420, 246, 136, 46)  @(390, 150, 1230, 200)    720
Example $owned 'pet_hunger'      @(722, 793, 44, 22)   @(420, 700, 380, 260)     720
Example $owned 'pet_food'        @(505, 840, 196, 22)  @(420, 700, 380, 260)     720
Example $owned 'pet_feed10'      @(446, 877, 102, 57)  @(420, 700, 380, 260)     720
Example $owned 'pet_feed50'      @(552, 877, 103, 57)  @(420, 700, 380, 260)     720
Example $owned 'pet_window'      @(440, 790, 325, 148) @(0, 0, 2000, 1123)       720
$owned.Dispose()
$safe = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'source\safehouse-page.png'))
Example $safe 'pet_safehousebtn' @(12, 142, 240, 46)   @(0, 90, 520, 260)        720
Example $safe 'pet_animals'      @(967, 731, 202, 58)  @(600, 640, 800, 220)     720
Example $safe 'pet_safehouse'    @(967, 731, 202, 58)  @(0, 0, 2000, 1124)       720
$safe.Dispose()
$pet = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'source\pet-training.png'))
Example $pet 'pet_tab'           @(900, 248, 244, 44)  @(390, 150, 1230, 200)    720
Example $pet 'pet_train'         @(1057, 747, 296, 70) @(820, 600, 780, 300)     720
Example $pet 'pet_close'         @(1536, 182, 44, 42)  @(1200, 140, 420, 150)    720
Example $pet 'pet_page'          @(900, 248, 244, 44)  @(0, 0, 2000, 1124)       720
$pet.Dispose()
Get-ChildItem $out | Select-Object Name, Length
