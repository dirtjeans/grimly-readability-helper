# test-word-readability-extended.ps1
#
# Extended exploration of how Word computes syllables for:
#   - letter words (short, long, tricky patterns)
#   - numbers (various sizes)
#   - numbers with symbols ($, %, etc.)
#   - hyphenated compounds
#   - acronyms and mixed-case tokens
#   - sentence-length variation (to verify rules are stable)
#
# Methodology:
#   Every test uses the SAME long carrier sentence, with only the target token
#   substituted. We first measure the carrier with a known 1-syllable token
#   ("none") as a calibration baseline. Every subsequent row subtracts that
#   baseline's syllable-count contribution to isolate the token's syllables.
#
#   The long carrier keeps total Flesch well above 0 so clamping doesn't
#   corrupt the back-computed syllable count, even for high-syllable tokens.
#
# Run:  powershell -File tools\test-word-readability-extended.ps1

$ErrorActionPreference = 'Stop'

# Long carrier: 15 words + token = 16 words. Should keep Flesch >> 0 for all
# reasonable token syllable counts. "NONE" is the calibration token (1 syl).
$CARRIER_PREFIX = 'After careful review the team decided that the correct answer to the question was'

# --- Test cases grouped by category ---
$groups = [ordered]@{
    'CALIBRATION' = @(
        @{ Token = 'none';                 Note = 'baseline: 1 syl expected' }
    )

    'SHORT WORDS (1-2 syl)' = @(
        @{ Token = 'cat' }
        @{ Token = 'dog' }
        @{ Token = 'table' }
        @{ Token = 'rapid' }
        @{ Token = 'simple' }
    )

    'LONG WORDS' = @(
        @{ Token = 'education' }
        @{ Token = 'anticipation' }
        @{ Token = 'incomprehensibility' }
        @{ Token = 'antidisestablishmentarianism' }
    )

    'SILENT-E' = @(
        @{ Token = 'make' }
        @{ Token = 'taken' }
        @{ Token = 'tale' }
        @{ Token = 'table' }
        @{ Token = 'noble' }
    )

    'SILENT-ED' = @(
        @{ Token = 'walked' }
        @{ Token = 'talked' }
        @{ Token = 'needed' }
        @{ Token = 'started' }
        @{ Token = 'announced' }
    )

    'VOWEL CLUSTERS' = @(
        @{ Token = 'queue' }
        @{ Token = 'beautiful' }
        @{ Token = 'guitar' }
        @{ Token = 'year' }
    )

    'NUMBERS' = @(
        @{ Token = '1' }
        @{ Token = '5' }
        @{ Token = '9' }
        @{ Token = '10' }
        @{ Token = '15' }
        @{ Token = '72' }
        @{ Token = '99' }
        @{ Token = '100' }
        @{ Token = '500' }
        @{ Token = '999' }
        @{ Token = '1000' }
        @{ Token = '12345' }
        @{ Token = '99999' }
    )

    'NUMBERS WITH COMMAS' = @(
        @{ Token = '1,000' }
        @{ Token = '10,000' }
        @{ Token = '100,000' }
        @{ Token = '1,000,000' }
    )

    'DECIMALS' = @(
        @{ Token = '1.5' }
        @{ Token = '3.14' }
        @{ Token = '99.99' }
    )

    'CURRENCY' = @(
        @{ Token = '$1' }
        @{ Token = '$5' }
        @{ Token = '$50' }
        @{ Token = '$500' }
        @{ Token = '$5.99' }
        @{ Token = '$1,000' }
        @{ Token = '$1,000,000' }
    )

    'PERCENTAGES' = @(
        @{ Token = '1%' }
        @{ Token = '10%' }
        @{ Token = '72%' }
        @{ Token = '100%' }
        @{ Token = '99.9%' }
    )

    'COMMON SYMBOLS' = @(
        @{ Token = '&' }
        @{ Token = '@' }
        @{ Token = '#' }
        @{ Token = '+' }
        @{ Token = '/' }
        @{ Token = 'Tom&Jerry';             Note = 'ampersand compound' }
        @{ Token = 'user@host';             Note = 'email-style' }
        @{ Token = '#topic';                Note = 'hashtag' }
        @{ Token = 'and/or';                Note = 'slash compound' }
    )

    'HYPHENATED (letters only)' = @(
        @{ Token = 'state-of-the-art' }
        @{ Token = 'twenty-seven' }
        @{ Token = 'well-known' }
        @{ Token = 'mother-in-law' }
    )

    'HYPHENATED (digits mixed in)' = @(
        @{ Token = '5-fold' }
        @{ Token = '27-year-old' }
        @{ Token = '10-K' }
        @{ Token = '9-to-5' }
    )

    'ACRONYMS' = @(
        @{ Token = 'AI' }
        @{ Token = 'API' }
        @{ Token = 'ACL' }
        @{ Token = 'FBI' }
        @{ Token = 'NASA' }
        @{ Token = 'HTTPS' }
        @{ Token = 'XML' }
        @{ Token = 'SQL' }
    )

    'MIXED CASE' = @(
        @{ Token = 'iPhone' }
        @{ Token = 'OpenBSD' }
        @{ Token = 'JavaScript' }
        @{ Token = 'camelCase' }
        @{ Token = 'PascalCase' }
    )
}

# --- Sentence-length variation: how stable are Word's per-token syllable assignments? ---
$sentenceLengthTests = @(
    @{ Name = 'SHORT (3 words)';    Text = 'The answer is seventy-two.' }
    @{ Name = 'MEDIUM (8 words)';   Text = 'The final answer to the question is seventy-two.' }
    @{ Name = 'LONG (16 words)';    Text = 'After careful review the team decided that the correct answer to the question was seventy-two.' }
    @{ Name = 'VERY LONG (30 words)'; Text = 'After careful review of all the available data the team deliberated at length and unanimously decided that the correct answer to the original question posed yesterday was seventy-two.' }
)

# --- Helpers ---
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
            Text      = $text
            Words     = $s.Words
            Sentences = $s.Sentences
            Chars     = $s.Chars
            Flesch    = $s.Flesch
            Syllables = $syl
        }
    }
    finally { $doc.Close([ref]$false) }
}

# --- Main ---
Write-Host "Starting Word (invisible)..." -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    # 1. Calibrate with the baseline token "none" (1 syl expected).
    $baselineText = "$CARRIER_PREFIX none."
    $baseline = Measure-Text $word $baselineText
    $baselineSyl = [math]::Round($baseline.Syllables, 2)

    Write-Host ""
    Write-Host "=== CALIBRATION ===" -ForegroundColor Yellow
    Write-Host ("Carrier: '{0}'" -f $baselineText)
    Write-Host ("  Words={0}  Sents={1}  Chars={2}  Flesch={3:F1}  TotalSyl={4:F2}" -f `
        $baseline.Words, $baseline.Sentences, $baseline.Chars, $baseline.Flesch, $baseline.Syllables)

    # baseline has 16 words. "none" = 1 syl. So carrier sans-token = baselineSyl - 1.
    $carrierOnlySyl = $baselineSyl - 1
    Write-Host ("  Implied 'carrier-only' (without token) syllables: {0:F2}" -f $carrierOnlySyl) -ForegroundColor DarkCyan

    # 2. Run grouped tests.
    Write-Host ""
    foreach ($groupName in $groups.Keys) {
        Write-Host ("=== {0} ===" -f $groupName) -ForegroundColor Yellow
        Write-Host ("{0,-28} {1,3} {2,3} {3,4} {4,7} {5,9} {6,7}" -f 'Token', 'W', 'S', 'Ch', 'Flesch', 'TotalSyl', 'TokSyl') -ForegroundColor DarkGray
        Write-Host ('-' * 75) -ForegroundColor DarkGray

        foreach ($t in $groups[$groupName]) {
            $token = $t.Token
            $text = "$CARRIER_PREFIX $token."
            try {
                $r = Measure-Text $word $text
                # Token syllables = total - carrier-only (approximate; Word may split token into multiple words)
                # We also report raw total so the user can do their own subtraction.
                $tokSyl = if ($null -ne $r.Syllables) { [math]::Round($r.Syllables - $carrierOnlySyl, 2) } else { '?' }
                $totSyl = if ($null -ne $r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }

                $note = if ($t.ContainsKey('Note')) { '  # ' + $t.Note } else { '' }
                Write-Host ("{0,-28} {1,3} {2,3} {3,4} {4,7:F1} {5,9} {6,7}{7}" -f `
                    $token, $r.Words, $r.Sentences, $r.Chars, $r.Flesch, $totSyl, $tokSyl, $note)
            }
            catch {
                Write-Host ("{0,-28} ERROR: {1}" -f $token, $_.Exception.Message) -ForegroundColor Red
            }
        }
        Write-Host ""
    }

    # 3. Sentence-length stability test.
    Write-Host "=== SENTENCE-LENGTH STABILITY (token 'seventy-two') ===" -ForegroundColor Yellow
    Write-Host ("{0,-22} {1,3} {2,3} {3,7} {4,9}" -f 'Name', 'W', 'S', 'Flesch', 'TotalSyl') -ForegroundColor DarkGray
    Write-Host ('-' * 50) -ForegroundColor DarkGray
    foreach ($st in $sentenceLengthTests) {
        try {
            $r = Measure-Text $word $st.Text
            $totSyl = if ($null -ne $r.Syllables) { '{0:F2}' -f $r.Syllables } else { '?' }
            Write-Host ("{0,-22} {1,3} {2,3} {3,7:F1} {4,9}" -f `
                $st.Name, $r.Words, $r.Sentences, $r.Flesch, $totSyl)
        }
        catch {
            Write-Host ("{0,-22} ERROR: {1}" -f $st.Name, $_.Exception.Message) -ForegroundColor Red
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
Write-Host "Notes:" -ForegroundColor Cyan
Write-Host "  - 'TotalSyl' is Word's implied syllable count for the whole sentence."
Write-Host "  - 'TokSyl' is the delta from the carrier-only baseline — i.e., what Word"
Write-Host "    attributed to the token itself."
Write-Host "  - 'W' may grow above 16 when Word splits hyphenated tokens into multiple words."
Write-Host "  - If TotalSyl shows a large fraction, there is an underlying non-integer from"
Write-Host "    the back-computation — usually means Flesch was clamped. Rerun with a longer"
Write-Host "    carrier if it matters."
