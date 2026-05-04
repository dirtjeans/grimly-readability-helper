# test-word-abbreviations-contractions.ps1
#
# Focused probe of two specific behaviors where Grimly might diverge from Word:
#
#   1. ABBREVIATION PERIODS — does Word count each "." inside abbreviations
#      like "U.S." or "Mr." as a sentence boundary? Grimly's current sentence
#      counter does, which would inflate Flesch on text with lots of
#      abbreviations.
#
#   2. CONTRACTIONS — does Grimly's apostrophe-as-vowel-break produce the
#      same syllable count Word does? Suspect cases: "we've" (adjacent vowels
#      split by apostrophe), "I'll" (two consecutive L's), "won't" (should
#      stay 1 syl).
#
# Run:  powershell -File tools\test-word-abbreviations-contractions.ps1

$ErrorActionPreference = 'Stop'

# ============ ABBREVIATION TESTS ============
# Each case is a full short sentence. We check Word's reported SENTENCE count.
# If Word sees "U.S. is a country." as 1 sentence, great — Grimly currently
# sees 2 (two periods). The delta here tells us whether we need to fix
# Grimly's sentence counter.

$abbreviationTests = @(
    # Baseline — plain sentences, no abbreviations
    @{ Label = 'plain: single sentence';   Text = 'The dog ran quickly.' }
    @{ Label = 'plain: two sentences';     Text = 'The dog ran. It was fast.' }

    # Single-period abbreviations mid-sentence
    @{ Label = 'Mr. mid-sentence';         Text = 'Mr. Smith ran the meeting.' }
    @{ Label = 'Dr. mid-sentence';         Text = 'Dr. Jones treated the patient.' }
    @{ Label = 'Mrs. mid-sentence';        Text = 'Mrs. Taylor read the report.' }
    @{ Label = 'Ms. mid-sentence';         Text = 'Ms. Davis wrote the memo.' }
    @{ Label = 'vs. mid-sentence';         Text = 'It was Apple vs. Samsung in court.' }
    @{ Label = 'etc. end-sentence';        Text = 'Apples, bananas, pears, etc.' }

    # Multi-period abbreviations mid-sentence
    @{ Label = 'U.S. mid-sentence';        Text = 'The U.S. is a federal republic.' }
    @{ Label = 'U.K. mid-sentence';        Text = 'The U.K. joined the coalition.' }
    @{ Label = 'U.S.A. mid-sentence';      Text = 'The U.S.A. won the match.' }
    @{ Label = 'e.g. mid-sentence';        Text = 'Use a sample file, e.g. config.json.' }
    @{ Label = 'i.e. mid-sentence';        Text = 'The right answer, i.e. three.' }
    @{ Label = 'Inc. in company';          Text = 'Acme Inc. filed the lawsuit.' }

    # Compliance / legal phrasing packed with abbreviations
    @{ Label = 'compliance phrasing';      Text = 'The U.S. Department of Defense said Acme Inc. had met the requirement.' }
    @{ Label = 'three abbrev one sent';    Text = 'Mr. Smith, Dr. Jones, and Mrs. Taylor all work for Acme Inc.' }

    # End-of-sentence decision: is the final period after "U.S." treated as
    # ending the sentence, or as part of the abbreviation?
    @{ Label = 'end with U.S.';            Text = 'The company left the U.S.' }
    @{ Label = 'end with etc.';            Text = 'The list included apples, pears, etc.' }
)

# ============ CONTRACTION TESTS ============
# Same carrier pattern as the prior script so we can subtract a known baseline.
# All these contractions should be 1 or 2 syllables in normal speech.

$CARRIER_PREFIX = 'After careful review the team decided that the correct answer was'
$contractionCarrier = "$CARRIER_PREFIX"

$contractionTests = @(
    # 1-syllable contractions (consonant + n't)
    @{ Label = "don't";              ExpectedSyl = 1 }
    @{ Label = "can't";              ExpectedSyl = 1 }
    @{ Label = "won't";              ExpectedSyl = 1 }
    @{ Label = "isn't";              ExpectedSyl = 2 }  # "iz-unt" — debatable, often 2
    @{ Label = "aren't";             ExpectedSyl = 1 }  # "arnt"
    @{ Label = "wasn't";             ExpectedSyl = 2 }
    @{ Label = "weren't";            ExpectedSyl = 1 }  # "wernt"
    @{ Label = "hasn't";             ExpectedSyl = 2 }
    @{ Label = "haven't";            ExpectedSyl = 2 }
    @{ Label = "hadn't";             ExpectedSyl = 2 }

    # 've contractions (suspect: apostrophe splits adjacent vowels into 2 groups)
    @{ Label = "I've";               ExpectedSyl = 1 }
    @{ Label = "we've";              ExpectedSyl = 1 }  # suspicious
    @{ Label = "you've";             ExpectedSyl = 1 }  # suspicious
    @{ Label = "they've";            ExpectedSyl = 1 }  # suspicious
    @{ Label = "would've";           ExpectedSyl = 2 }
    @{ Label = "should've";          ExpectedSyl = 2 }
    @{ Label = "could've";           ExpectedSyl = 2 }

    # 'll contractions
    @{ Label = "I'll";               ExpectedSyl = 1 }
    @{ Label = "we'll";              ExpectedSyl = 1 }
    @{ Label = "you'll";             ExpectedSyl = 1 }
    @{ Label = "they'll";            ExpectedSyl = 1 }
    @{ Label = "it'll";              ExpectedSyl = 2 }
    @{ Label = "that'll";            ExpectedSyl = 2 }

    # 're / 'm / 's
    @{ Label = "I'm";                ExpectedSyl = 1 }
    @{ Label = "we're";              ExpectedSyl = 1 }
    @{ Label = "you're";             ExpectedSyl = 1 }
    @{ Label = "they're";            ExpectedSyl = 1 }
    @{ Label = "it's";               ExpectedSyl = 1 }
    @{ Label = "that's";             ExpectedSyl = 1 }
    @{ Label = "she's";              ExpectedSyl = 1 }
    @{ Label = "he's";               ExpectedSyl = 1 }

    # 'd contractions
    @{ Label = "I'd";                ExpectedSyl = 1 }
    @{ Label = "we'd";               ExpectedSyl = 1 }
    @{ Label = "you'd";              ExpectedSyl = 1 }
    @{ Label = "they'd";             ExpectedSyl = 1 }

    # Multi-syllable
    @{ Label = "shouldn't";          ExpectedSyl = 2 }
    @{ Label = "couldn't";           ExpectedSyl = 2 }
    @{ Label = "wouldn't";           ExpectedSyl = 2 }
    @{ Label = "mustn't";            ExpectedSyl = 2 }

    # Tricky multi-contractions
    @{ Label = "shouldn't've";       ExpectedSyl = 3 }
    @{ Label = "y'all";              ExpectedSyl = 1 }
)

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
    # ----------- ABBREVIATIONS: focus on sentence count -----------
    Write-Host ""
    Write-Host "=== ABBREVIATIONS (focus: sentence count) ===" -ForegroundColor Yellow
    Write-Host "  If Word reports S=1 for 'Mr. Smith ran the meeting.' we know it's"
    Write-Host "  smart about abbreviation periods. If S=2, Word splits on every period."
    Write-Host ""
    Write-Host ("{0,-26} {1,-55} {2,3} {3,3} {4,5}" -f 'Label', 'Text', 'W', 'S', 'Flesch') -ForegroundColor DarkGray
    Write-Host ('-' * 95) -ForegroundColor DarkGray
    foreach ($t in $abbreviationTests) {
        try {
            $r = Measure-Text $word $t.Text
            # Color rows where sentence count looks surprising
            $color = 'White'
            if ($r.Sentences -gt 1 -and $t.Text -notmatch '\.\s+[A-Z]') { $color = 'Magenta' }  # flagged: multi-sent but no real break
            Write-Host ("{0,-26} {1,-55} {2,3} {3,3} {4,5:F1}" -f `
                $t.Label, $t.Text, $r.Words, $r.Sentences, $r.Flesch) -ForegroundColor $color
        } catch {
            Write-Host ("{0,-26} ERROR: {1}" -f $t.Label, $_.Exception.Message) -ForegroundColor Red
        }
    }

    # ----------- CONTRACTIONS: focus on syllable count -----------
    # Calibrate baseline (using "none" as in prior scripts).
    $baseline = Measure-Text $word "$contractionCarrier none."
    $carrierOnlySyl = [math]::Round($baseline.Syllables - 1, 2)

    Write-Host ""
    Write-Host "=== CONTRACTIONS (focus: syllable count) ===" -ForegroundColor Yellow
    Write-Host ("  Carrier: '$contractionCarrier X.' -- carrier-only syl = {0:F2}" -f $carrierOnlySyl)
    Write-Host ""
    Write-Host ("{0,-18} {1,3} {2,3} {3,5} {4,7} {5,7} {6,6}" -f `
        'Token', 'W', 'S', 'Exp', 'Flesch', 'TotSyl', 'TokSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 60) -ForegroundColor DarkGray
    foreach ($t in $contractionTests) {
        $token = $t.Label
        $text = "$contractionCarrier $token."
        try {
            $r = Measure-Text $word $text
            $tokSyl = if ($null -ne $r.Syllables) {
                [math]::Round($r.Syllables - $carrierOnlySyl, 2)
            } else { '?' }
            $totSyl = if ($null -ne $r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
            # Color: flag if Word's count differs from expected by >=1
            $color = 'White'
            if ($tokSyl -is [double] -or $tokSyl -is [int]) {
                $delta = [math]::Abs($tokSyl - $t.ExpectedSyl)
                if ($delta -ge 1) { $color = 'Magenta' }
            }
            Write-Host ("{0,-18} {1,3} {2,3} {3,5} {4,7:F1} {5,7} {6,6}" -f `
                $token, $r.Words, $r.Sentences, $t.ExpectedSyl, $r.Flesch, $totSyl, $tokSyl) `
                -ForegroundColor $color
        } catch {
            Write-Host ("{0,-18} ERROR: {1}" -f $token, $_.Exception.Message) -ForegroundColor Red
        }
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
Write-Host ""
Write-Host "How to read this:" -ForegroundColor Cyan
Write-Host "  ABBREVIATIONS: if S=1 in rows like 'Mr. mid-sentence', Word is smart"
Write-Host "    and Grimly should NOT count every period as sentence-end."
Write-Host ""
Write-Host '  CONTRACTIONS: TokSyl = Word syllable count. Exp = expected.'
Write-Host '    Magenta rows are where Word surprised us.'
