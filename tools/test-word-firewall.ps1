# test-word-firewall.ps1
#
# Probe for the firewall sentence divergence. Hypotheses:
#   1. "rules" — Grimly gets 2 syl (plural masks silent-e), Word gets 1.
#      Same pattern: bakes, writes, hopes, makes, likes.
#   2. "firewall" — compound silent-e. Grimly gets 3, Word likely gets 2.
#   3. "enforces" — -ces adds a syllable (Word=3), does our counter agree?

$ErrorActionPreference = 'Stop'
$CARRIER_PREFIX = 'After careful review the team decided that the correct answer was'

$tests = @(
    # -es endings (silent-e masked by plural/3rd-person s)
    @{ Label = 'rules';          Text = "$CARRIER_PREFIX rules." }
    @{ Label = 'bakes';          Text = "$CARRIER_PREFIX bakes." }
    @{ Label = 'writes';         Text = "$CARRIER_PREFIX writes." }
    @{ Label = 'hopes';          Text = "$CARRIER_PREFIX hopes." }
    @{ Label = 'makes';          Text = "$CARRIER_PREFIX makes." }
    @{ Label = 'likes';          Text = "$CARRIER_PREFIX likes." }
    # Controls: -ces / -ses / -zes that ADD a syllable
    @{ Label = 'enforces';       Text = "$CARRIER_PREFIX enforces." }
    @{ Label = 'dances';         Text = "$CARRIER_PREFIX dances." }
    @{ Label = 'uses';           Text = "$CARRIER_PREFIX uses." }
    @{ Label = 'sizes';          Text = "$CARRIER_PREFIX sizes." }
    @{ Label = 'boxes';          Text = "$CARRIER_PREFIX boxes." }
    # -les endings (syllabic l, should stay 2+)
    @{ Label = 'tables';         Text = "$CARRIER_PREFIX tables." }
    @{ Label = 'apples';         Text = "$CARRIER_PREFIX apples." }
    # -le words (same issue: rule vs table)
    @{ Label = 'rule';           Text = "$CARRIER_PREFIX rule." }
    @{ Label = 'mile';           Text = "$CARRIER_PREFIX mile." }
    @{ Label = 'pole';           Text = "$CARRIER_PREFIX pole." }
    @{ Label = 'whole';          Text = "$CARRIER_PREFIX whole." }
    @{ Label = 'table';          Text = "$CARRIER_PREFIX table." }
    @{ Label = 'apple';          Text = "$CARRIER_PREFIX apple." }
    # Compound words with internal silent-e
    @{ Label = 'firewall';       Text = "$CARRIER_PREFIX firewall." }
    @{ Label = 'homeowner';      Text = "$CARRIER_PREFIX homeowner." }
    @{ Label = 'lifetime';       Text = "$CARRIER_PREFIX lifetime." }
    @{ Label = 'timeline';       Text = "$CARRIER_PREFIX timeline." }
    @{ Label = 'wildlife';       Text = "$CARRIER_PREFIX wildlife." }
    @{ Label = 'storefront';     Text = "$CARRIER_PREFIX storefront." }
    # Full passage
    @{ Label = 'THE PASSAGE';    Text = 'The firewall checks network traffic and enforces any rules the security team has set.' }
)

function Measure-Text($word, $text) {
    $doc = $word.Documents.Add()
    try {
        $doc.Content.Text = $text
        $stats = $doc.Content.ReadabilityStatistics
        $byName = @{}
        foreach ($s in $stats) { $byName[$s.Name] = $s.Value }
        $w = [int]$byName['Words']; $se = [int]$byName['Sentences']
        $f = [double]$byName['Flesch Reading Ease']
        $syl = if ($w -gt 0 -and $se -gt 0) {
            $asl = $w / $se
            $asw = (206.835 - 1.015 * $asl - $f) / 84.6
            [math]::Round($w * $asw, 2)
        } else { $null }
        return [PSCustomObject]@{ Words=$w; Sentences=$se; Flesch=$f; Syllables=$syl }
    } finally { $doc.Close([ref]$false) }
}

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    # Calibrate
    $baseline = Measure-Text $word "$CARRIER_PREFIX none."
    $carrierOnlySyl = [math]::Round($baseline.Syllables - 1, 2)
    Write-Host ""
    Write-Host ("Carrier-only syl = {0:F2}" -f $carrierOnlySyl) -ForegroundColor Cyan
    Write-Host ""
    Write-Host ("{0,-16} {1,3} {2,3} {3,7} {4,7} {5,7}" -f 'Token', 'W', 'S', 'Flesch', 'TotSyl', 'TokSyl') -ForegroundColor Yellow
    Write-Host ('-' * 55) -ForegroundColor DarkGray

    foreach ($t in $tests) {
        $r = Measure-Text $word $t.Text
        $tot = '{0:F2}' -f $r.Syllables
        $tokSyl = if ($t.Label -eq 'THE PASSAGE') {
            '{0:F2}' -f $r.Syllables
        } else {
            [math]::Round($r.Syllables - $carrierOnlySyl, 2)
        }
        Write-Host ("{0,-16} {1,3} {2,3} {3,7:F1} {4,7} {5,7}" -f `
            $t.Label, $r.Words, $r.Sentences, $r.Flesch, $tot, $tokSyl)
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
