# test-word-quotes.ps1
#
# Probes how Word handles quotation marks, initials, and related tokens —
# triggered by a specific passage about MAHA that showed unexpected
# readability behavior.
#
# Focus areas:
#   1. Smart vs straight quotes (" vs ")
#   2. Quoted words and word-count behavior
#   3. Period inside closing quote (American style)
#   4. Opening-apostrophe tokens: 'tis, '90s, rock 'n' roll
#   5. Initials in names: "Robert F. Kennedy Jr." — does F. trigger sentence end?
#   6. Possessive of abbreviation: "Jr.'s"
#   7. The full MAHA passage as one long test
#
# Run:  powershell -File tools\test-word-quotes.ps1

$ErrorActionPreference = 'Stop'

# ============ SMART vs STRAIGHT QUOTES ============
# Paired: same sentence, one with straight, one with curly. Deltas should be 0.
$quoteTests = @(
    @{ Label = 'straight: one quoted word';  Text = 'The term "organic" means raised without pesticides.' }
    @{ Label = 'smart:    one quoted word';  Text = 'The term "organic" means raised without pesticides.' }

    @{ Label = 'straight: quoted phrase';    Text = 'The group called it "Make America Healthy Again" publicly.' }
    @{ Label = 'smart:    quoted phrase';    Text = 'The group called it "Make America Healthy Again" publicly.' }

    @{ Label = 'straight: quote + period-in';   Text = 'She said "Go home." Then she walked away.' }
    @{ Label = 'smart:    quote + period-in';   Text = 'She said "Go home." Then she walked away.' }

    @{ Label = 'straight: quote + comma-in';    Text = 'She said "I agree," and nodded.' }
    @{ Label = 'smart:    quote + comma-in';    Text = 'She said "I agree," and nodded.' }
)

# ============ INITIALS AND ABBREVIATIONS IN NAMES ============
$initialsTests = @(
    @{ Label = 'plain name';                 Text = 'Robert Kennedy ran for president.' }
    @{ Label = 'middle initial (F.)';        Text = 'Robert F. Kennedy ran for president.' }
    @{ Label = 'Jr. suffix';                 Text = 'Robert Kennedy Jr. ran for president.' }
    @{ Label = 'middle + Jr.';               Text = 'Robert F. Kennedy Jr. ran for president.' }
    @{ Label = 'two initials';               Text = 'President J. F. Kennedy gave the speech.' }
    @{ Label = 'abbreviation possessive';    Text = 'Robert F. Kennedy Jr.''s policies were debated.' }

    # Ambiguous case: is "F. K" a sentence break? Word should say no.
    @{ Label = 'F. followed by uppercase';   Text = 'Robert F. Kennedy ran a campaign.' }

    # Control: a REAL sentence end that happens to follow a one-char word.
    @{ Label = 'real sentence end';          Text = 'The tests passed. Another run is needed.' }
)

# ============ OPENING-APOSTROPHE TOKENS ============
$CARRIER_PREFIX = 'After careful review the team decided that the correct answer was'

$openApostropheTests = @(
    @{ Label = "'tis";                       Text = "$CARRIER_PREFIX 'tis." }
    @{ Label = "'twas";                      Text = "$CARRIER_PREFIX 'twas." }
    @{ Label = "'90s";                       Text = "$CARRIER_PREFIX '90s." }
    @{ Label = "rock 'n' roll";              Text = "$CARRIER_PREFIX rock 'n' roll." }
)

# ============ FULL PASSAGE ============
# The exact MAHA passage the user noticed divergence on.
$fullPassage = @'
Vaccine skeptics, "organic moms" and anti-pesticide activists came together to elect President Trump. Nearly two years later, Health Secretary Robert F. Kennedy Jr.'s "Make America Healthy Again" movement is still a political force. But some MAHA voters are disillusioned.
'@

# Variant with straight quotes for comparison
$fullPassageStraight = @'
Vaccine skeptics, "organic moms" and anti-pesticide activists came together to elect President Trump. Nearly two years later, Health Secretary Robert F. Kennedy Jr.'s "Make America Healthy Again" movement is still a political force. But some MAHA voters are disillusioned.
'@

# Variant with initials removed to isolate their effect
$fullPassageNoInitials = @'
Vaccine skeptics, "organic moms" and anti-pesticide activists came together to elect President Trump. Nearly two years later, Health Secretary Robert Kennedy's "Make America Healthy Again" movement is still a political force. But some MAHA voters are disillusioned.
'@

# ============ HELPERS ============
function Get-Stats($doc) {
    $stats = $doc.Content.ReadabilityStatistics
    $byName = @{}
    foreach ($s in $stats) { $byName[$s.Name] = $s.Value }
    return [PSCustomObject]@{
        Words     = [int]$byName['Words']
        Sentences = [int]$byName['Sentences']
        Chars     = [int]$byName['Characters']
        Flesch    = [double]$byName['Flesch Reading Ease']
    }
}

function Get-Syllables($words, $sentences, $flesch) {
    if ($words -le 0 -or $sentences -le 0) { return $null }
    $asl = $words / $sentences
    $asw = (206.835 - 1.015 * $asl - $flesch) / 84.6
    return $words * $asw
}

function Measure-Text($word, $text) {
    $doc = $word.Documents.Add()
    try {
        $doc.Content.Text = $text
        $s = Get-Stats $doc
        $syl = Get-Syllables $s.Words $s.Sentences $s.Flesch
        return [PSCustomObject]@{
            Text = $text; Words = $s.Words; Sentences = $s.Sentences
            Chars = $s.Chars; Flesch = $s.Flesch; Syllables = $syl
        }
    }
    finally { $doc.Close([ref]$false) }
}

# ============ MAIN ============
Write-Host "Starting Word (invisible)..." -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    # ---- 1. Smart vs straight quotes ----
    Write-Host ""
    Write-Host "=== SMART vs STRAIGHT QUOTES ===" -ForegroundColor Yellow
    Write-Host "  Paired tests should produce identical W/S/Flesch. Deltas = bug."
    Write-Host ""
    Write-Host ("{0,-30} {1,3} {2,3} {3,5} {4,7} {5,7}" -f 'Label', 'W', 'S', 'Chars', 'Flesch', 'TotSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 70) -ForegroundColor DarkGray
    foreach ($t in $quoteTests) {
        $r = Measure-Text $word $t.Text
        $tot = if ($r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
        Write-Host ("{0,-30} {1,3} {2,3} {3,5} {4,7:F1} {5,7}" -f `
            $t.Label, $r.Words, $r.Sentences, $r.Chars, $r.Flesch, $tot)
    }

    # ---- 2. Initials and abbreviations ----
    Write-Host ""
    Write-Host "=== INITIALS / ABBREVIATIONS IN NAMES ===" -ForegroundColor Yellow
    Write-Host "  Watch S (sentences). 'F. K' should NOT be sentence-end in Word."
    Write-Host ""
    Write-Host ("{0,-30} {1,3} {2,3} {3,5} {4,7} {5,7}" -f 'Label', 'W', 'S', 'Chars', 'Flesch', 'TotSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 70) -ForegroundColor DarkGray
    foreach ($t in $initialsTests) {
        $r = Measure-Text $word $t.Text
        $tot = if ($r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
        $color = if ($r.Sentences -gt 1 -and $t.Label -notmatch 'real sentence') { 'Magenta' } else { 'White' }
        Write-Host ("{0,-30} {1,3} {2,3} {3,5} {4,7:F1} {5,7}" -f `
            $t.Label, $r.Words, $r.Sentences, $r.Chars, $r.Flesch, $tot) -ForegroundColor $color
    }

    # ---- 3. Opening-apostrophe tokens ----
    Write-Host ""
    Write-Host "=== OPENING-APOSTROPHE TOKENS (syllable focus) ===" -ForegroundColor Yellow
    $baseline = Measure-Text $word "$CARRIER_PREFIX none."
    $baselineSyl = [math]::Round($baseline.Syllables - 1, 2)
    Write-Host ("  carrier-only syl = {0:F2}" -f $baselineSyl)
    Write-Host ""
    Write-Host ("{0,-22} {1,3} {2,3} {3,7} {4,7} {5,7}" -f 'Token', 'W', 'S', 'Flesch', 'TotSyl', 'TokSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 60) -ForegroundColor DarkGray
    foreach ($t in $openApostropheTests) {
        $r = Measure-Text $word $t.Text
        $tot = if ($r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
        $tokSyl = if ($r.Syllables) { [math]::Round($r.Syllables - $baselineSyl, 2) } else { '?' }
        Write-Host ("{0,-22} {1,3} {2,3} {3,7:F1} {4,7} {5,7}" -f `
            $t.Label, $r.Words, $r.Sentences, $r.Flesch, $tot, $tokSyl)
    }

    # ---- 4. Full passage (three variants) ----
    Write-Host ""
    Write-Host "=== FULL MAHA PASSAGE (the one you flagged) ===" -ForegroundColor Yellow
    Write-Host ""
    $passages = @(
        @{ Label = 'Original (smart quotes, all initials)'; Text = $fullPassage }
        @{ Label = 'Straight quotes';                       Text = $fullPassageStraight }
        @{ Label = 'No middle initial / Jr.';               Text = $fullPassageNoInitials }
    )
    Write-Host ("{0,-42} {1,3} {2,3} {3,5} {4,7} {5,7}" -f 'Variant', 'W', 'S', 'Chars', 'Flesch', 'TotSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 80) -ForegroundColor DarkGray
    foreach ($p in $passages) {
        $r = Measure-Text $word $p.Text
        $tot = if ($r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
        Write-Host ("{0,-42} {1,3} {2,3} {3,5} {4,7:F1} {5,7}" -f `
            $p.Label, $r.Words, $r.Sentences, $r.Chars, $r.Flesch, $tot)
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    [gc]::Collect()
    [gc]::WaitForPendingFinalizers()
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
