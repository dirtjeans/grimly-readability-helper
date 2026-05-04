# test-word-readability.ps1
#
# Feeds test strings to Microsoft Word via COM, reads back readability stats,
# and back-computes the syllable count Word must have used.
#
# The Flesch formula Word uses:
#   Flesch = 206.835 - 1.015 * (words / sentences) - 84.6 * (syllables / words)
# Solving for syllables:
#   syllables = words * (206.835 - 1.015 * ASL - Flesch) / 84.6
#
# Run:  pwsh -File tools\test-word-readability.ps1
# Requires: Microsoft Word installed on this machine.

$ErrorActionPreference = "Stop"

# --- Test cases ---
# Each row uses the same frame "The answer is X." so all tests share 4 words,
# 1 sentence, and identical "fixed" syllables. Any syllable difference across
# rows is attributable to the token under test.
#
# NOTE: single-quoted strings so PowerShell does not interpolate $ or %.
$tests = @(
    # --- Baselines (fully-spelled-out controls) ---
    @{ Label = 'spelled: five';           Text = 'The answer is five.' }
    @{ Label = 'spelled: fifteen';        Text = 'The answer is fifteen.' }
    @{ Label = 'spelled: seventy-two';    Text = 'The answer is seventy-two.' }
    @{ Label = 'spelled: one hundred';    Text = 'The answer is one hundred.' }
    @{ Label = 'spelled: one thousand';   Text = 'The answer is one thousand.' }
    @{ Label = 'spelled: fifty dollars';  Text = 'The answer is fifty dollars.' }
    @{ Label = 'spelled: 72 percent';     Text = 'The answer is 72 percent.' }

    # --- Pure numbers ---
    @{ Label = 'num: 5';                  Text = 'The answer is 5.' }
    @{ Label = 'num: 15';                 Text = 'The answer is 15.' }
    @{ Label = 'num: 72';                 Text = 'The answer is 72.' }
    @{ Label = 'num: 100';                Text = 'The answer is 100.' }
    @{ Label = 'num: 1000 (no comma)';    Text = 'The answer is 1000.' }
    @{ Label = 'num: 1,000';              Text = 'The answer is 1,000.' }
    @{ Label = 'num: 1000000';            Text = 'The answer is 1000000.' }
    @{ Label = 'num: 1,000,000';          Text = 'The answer is 1,000,000.' }

    # --- Symbols attached to numbers ---
    @{ Label = 'sym: 72%';                Text = 'The answer is 72%.' }
    @{ Label = 'sym: $50';                Text = 'The answer is $50.' }
    @{ Label = 'sym: $5.99';              Text = 'The answer is $5.99.' }

    # --- Hyphenated compounds ---
    @{ Label = 'hyph: 27-year-old';       Text = 'The answer is 27-year-old.' }
    @{ Label = 'hyph: 5-fold';            Text = 'The answer is 5-fold.' }
    @{ Label = 'hyph: twenty-seven';      Text = 'The answer is twenty-seven.' }

    # --- Acronyms / mixed case ---
    @{ Label = 'acr: AI';                 Text = 'The answer is AI.' }
    @{ Label = 'acr: ACL';                Text = 'The answer is ACL.' }
    @{ Label = 'acr: OpenBSD';            Text = 'The answer is OpenBSD.' }

    # --- The "read-aloud hypothesis" killer cases ---
    # If Word says seventy-two. Expects 4 syllables on the number → hypothesis confirmed.
    # If Word says 72. Gets 2 syllables (digit count) or 1 (single token heuristic) → hypothesis wrong.
    @{ Label = 'killer: 72 alone';        Text = 'The 72.' }
    @{ Label = 'killer: $5 vs five';      Text = 'The answer is $5.' }
)

# --- Helper: read stats from a document ---
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

# --- Helper: back-compute syllables from Flesch ---
function Get-Syllables($words, $sentences, $flesch) {
    if ($words -le 0 -or $sentences -le 0) { return $null }
    $asl = $words / $sentences
    $asw = (206.835 - 1.015 * $asl - $flesch) / 84.6
    return [math]::Round($words * $asw, 2)
}

Write-Host "Starting Word (invisible)..." -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0  # wdAlertsNone — suppress save/close prompts

try {
    Write-Host ""
    $header = "{0,-28} {1,-38} {2,3} {3,3} {4,4} {5,7} {6,7}" -f `
        'Label', 'Text', 'W', 'S', 'Ch', 'Flesch', 'Syl'
    Write-Host $header -ForegroundColor Yellow
    Write-Host ('-' * ($header.Length)) -ForegroundColor DarkGray

    foreach ($t in $tests) {
        $doc = $word.Documents.Add()
        try {
            $doc.Content.Text = $t.Text
            $s = Get-Stats $doc
            $syl = Get-Syllables $s.Words $s.Sentences $s.Flesch
            $sylDisplay = if ($null -eq $syl) { '?' } else { [string]$syl }

            $color = 'White'
            if ($t.Label -like 'killer:*') { $color = 'Magenta' }
            elseif ($t.Label -like 'spelled:*') { $color = 'DarkCyan' }

            Write-Host ("{0,-28} {1,-38} {2,3} {3,3} {4,4} {5,7:F1} {6,7}" -f `
                $t.Label, $t.Text, $s.Words, $s.Sentences, $s.Chars, $s.Flesch, $sylDisplay) `
                -ForegroundColor $color
        }
        catch {
            Write-Host ("{0,-28} {1,-38} ERROR: {2}" -f $t.Label, $t.Text, $_.Exception.Message) -ForegroundColor Red
        }
        finally {
            $doc.Close([ref]$false)  # don't save
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
Write-Host '  - Compare "num: 72" against "spelled: seventy-two". If Syl matches,'
Write-Host '    Word is using read-aloud syllables for numbers.'
Write-Host '  - Compare "sym: 72%" against "spelled: 72 percent". Matching Syl confirms'
Write-Host '    Word expands symbols to their spoken form.'
Write-Host '  - Compare "sym: $50" against "spelled: fifty dollars". Same check for currency.'
Write-Host '  - Hyphenated compounds show whether Word splits on hyphens.'
