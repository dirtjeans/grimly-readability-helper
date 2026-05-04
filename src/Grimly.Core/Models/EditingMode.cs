namespace Grimly.Models;

public enum EditingMode
{
    // Existing values — DO NOT reorder. Settings JSON serializes enum values
    // as integers, so reordering would silently reassign saved DefaultMode.
    FixGrammar,
    ShorterSentences,
    Plainify,
    ActiveVoice,
    ActiveVerbs,
    Tighten,
    CutGerunds,
    DropJargon,
    BulletPoints,
    Conversational,
    CustomPrompt,

    // --- Appended 2026-04-15 for readability-checklist coverage ---
    // Keep these at the end for the same serialization-stability reason.
    CutFiller,
    LeadWithPoint,
    ConcreteNouns,
    CutHedges,
    UnpackJargon
}

public static class EditingModeExtensions
{
    public static string GetDisplayName(this EditingMode mode) => mode switch
    {
        EditingMode.FixGrammar => "Fix Grammar",
        EditingMode.ShorterSentences => "Shorter Sentences",
        EditingMode.Plainify => "Shorter Words",
        EditingMode.ActiveVoice => "Active Voice",
        EditingMode.ActiveVerbs => "Active Verbs",
        EditingMode.Tighten => "Revise Nominalizations",
        EditingMode.CutGerunds => "Cut Gerunds",
        EditingMode.DropJargon => "Drop Jargon",
        EditingMode.BulletPoints => "Bullet Points",
        EditingMode.Conversational => "Conversational",
        EditingMode.CustomPrompt => "Custom",
        EditingMode.CutFiller => "Cut Filler",
        EditingMode.LeadWithPoint => "Lead With Point",
        EditingMode.ConcreteNouns => "Concrete Nouns",
        EditingMode.CutHedges => "Cut Hedges",
        EditingMode.UnpackJargon => "Unpack Terms",
        _ => mode.ToString()
    };

    // Output-format guardrail appended to every mode's prompt.
    // Written as plain prose (no "OUTPUT:" label or headings) because small
    // models will literally reproduce template-style formatting in their
    // responses. Bullet lists also get echoed, so the guardrail is a single
    // run-on sentence.
    private const string Suffix =
        " Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.";

    public static string GetSystemPrompt(this EditingMode mode) => mode switch
    {
        EditingMode.FixGrammar =>
            """
            You are a strict proofreader. Make ONLY mechanical grammar, spelling, and punctuation fixes to the text below. Do NOT improve, rewrite, simplify, or restyle anything that's already grammatically correct. If a sentence is clunky but technically correct, leave it exactly as written.

            FIX ONLY THESE:
            1. Spelling errors and obvious typos.
            2. Subject-verb agreement ("he have" → "he has").
            3. Pronoun agreement and case ("between you and I" → "between you and me").
            4. Verb tense errors and inconsistent tense within a single thought.
            5. Article errors ("a apple" → "an apple"; missing articles where required).
            6. Confused homophones when clearly wrong: its/it's, your/you're, their/there/they're, affect/effect, then/than, lose/loose, who's/whose.
            7. Capitalization (sentence starts, proper nouns, "I").
            8. Punctuation: missing periods, comma splices, missing/misplaced apostrophes, run-on sentences with no punctuation between independent clauses.
            9. Misplaced or dangling modifiers ONLY when the meaning is ambiguous as a result.
            10. Word boundaries that create real errors ("alot" → "a lot", "everyday" used as adverb → "every day").

            DO NOT:
            - Change word choice for style (no "utilize" → "use", no synonym swaps).
            - Shorten or split sentences for readability.
            - Convert passive to active or vice versa.
            - Reorder sentences or paragraphs.
            - Remove or add filler words, transitions, hedges, or qualifiers.
            - Change tone, voice, or formality.
            - Replace correct-but-awkward phrasing with smoother phrasing.
            - Change facts, names, numbers, or quoted text.
            - Add or remove the Oxford comma (it's a style choice, not a grammar rule).

            If the text has no grammar, spelling, or punctuation errors, return it unchanged.
            """ + Suffix,
        EditingMode.ShorterSentences =>
            "You are a writing assistant. Rewrite the following text to improve readability. Break up long sentences into shorter ones. Preserve the original meaning. " + Suffix,
        EditingMode.Plainify =>
            "You are a writing assistant. Rewrite the following text by replacing Latinate or abstract words with plain Anglo-Saxon equivalents. Examples: 'utilize' → 'use', 'commence' → 'start', 'facilitate' → 'help', 'demonstrate' → 'show', 'approximately' → 'about', 'sufficient' → 'enough', 'endeavor' → 'try', 'terminate' → 'end', 'obtain' → 'get', 'purchase' → 'buy', 'require' → 'need', 'assist' → 'help', 'prior to' → 'before', 'subsequent to' → 'after'. Only replace when the plain word is fully equivalent in meaning. Preserve technical terms, proper nouns, product names, and legal or contractual language exactly as written. Preserve the original meaning. " + Suffix,
        EditingMode.ActiveVoice =>
            "You are a writing assistant. Rewrite the following text by converting passive voice constructions to active voice (e.g., 'The report was written by the team' becomes 'The team wrote the report', 'Mistakes were made' becomes 'We made mistakes'). Preserve the original meaning. " + Suffix,
        EditingMode.ActiveVerbs =>
            "You are a writing assistant. Rewrite the following text using active voice wherever possible. Convert passive constructions to active ones. Preserve the original meaning. " + Suffix,
        EditingMode.Tighten =>
            "You are a writing assistant. Rewrite the following text by removing nominalizations — verbs hidden inside nouns that flatten prose. Apply these swaps wherever they appear: " +
            "'the implementation of X' → 'implementing X', " +
            "'the reduction of X' → 'reducing X', " +
            "'the identification of X' → 'identifying X', " +
            "'the prevention of X' → 'preventing X', " +
            "'make a decision' → 'decide', " +
            "'perform an analysis' → 'analyze', " +
            "'conduct a review' → 'review', " +
            "'give consideration to' → 'consider', " +
            "'make a determination' → 'determine'. " +
            "Only change parts of the text that match these patterns. Do NOT add new sentences or content. Do NOT touch gerund stacks (e.g., 'the tracking of the monitoring of') — a separate mode handles those. Preserve meaning, facts, numbers, names, and proper nouns. " + Suffix,
        EditingMode.CutGerunds =>
            "You are a writing assistant. Rewrite the following text to eliminate gerund stacks — chains of two or more -ing verbs in the same clause, usually joined by 'of', 'the', or 'and'. Gerund stacks make prose abstract and hard to parse. Replace each stack with a concrete finite verb or a cleaner noun phrase.\n\nExamples:\n- 'the implementing of the monitoring of endpoints' → 'monitoring endpoints'\n- 'We are improving our tracking of the reporting of security events.' → 'We are improving how we report security events.'\n- 'Focusing on implementing Zero Trust.' → 'We are implementing Zero Trust.'\n- 'Reducing the impact of attacking sophisticated threats.' → 'Reducing the impact of sophisticated attacks.'\n\nLeave isolated gerunds alone — only fix clusters where two or more -ing words appear together in the same clause. Leave gerunds that are the subject of a sentence alone ('Running tests is important' stays). Do not touch nominalizations ('made a decision') — a separate pass handles those. Preserve the original meaning, facts, numbers, names, and proper nouns. " + Suffix,
        EditingMode.DropJargon =>
            "You are a writing assistant. Rewrite the following text by removing industry buzzwords and vague corporate jargon. Replace them with plain language.\n\n" +
            "Examples:\n" +
            "- 'leverage synergies' → 'work together'\n" +
            "- 'operationalize' → 'put to work'\n" +
            "- 'value add' → 'benefit'\n" +
            "- 'best-of-breed' → (usually cuttable)\n" +
            "- 'mission-critical' → 'essential'\n" +
            "- 'next-generation' → (usually cuttable)\n" +
            "- 'cutting-edge' → 'new' or cut it\n" +
            "- 'robust' / 'seamless' → usually cuttable\n\n" +
            "Keep technical terms that are genuinely precise (MITRE ATT&CK, segmentation, firewall, API). Remove vague buzzwords that could apply to anything. Preserve the original meaning, facts, numbers, and proper nouns. " + Suffix,
        EditingMode.BulletPoints =>
            "You are a writing assistant. Return the ENTIRE text, keeping all sentences exactly as they are EXCEPT for sentences that contain a sequence of three or more items joined by commas or 'and'. For those sentences only, extract the items into a bullet point list with the introductory part ending in a colon. For example, 'If you catch on fire, you should stop what you're doing, drop to the ground, and roll around to put it out.' becomes:\n\nIf you catch on fire, you should:\n* Stop what you're doing\n* Drop to the ground\n* Roll around to put it out\n\nIMPORTANT: You must include ALL of the original text in your response. Only modify sentences that contain item sequences. Every other sentence must appear unchanged. " + Suffix,
        EditingMode.Conversational =>
            "You are a writing assistant. Rewrite the following text in a conversational but professional tone, as if explaining it to a colleague.\n\n" +
            "What conversational means here:\n" +
            "- Use contractions (it's, don't, you're)\n" +
            "- Use shorter, more direct sentences\n" +
            "- Use 'you' and 'your' when it fits\n" +
            "- Prefer common words over formal ones\n\n" +
            "What conversational does NOT mean:\n" +
            "- Do NOT add new ideas, examples, explanations, or commentary\n" +
            "- Do NOT add color phrases ('interestingly', 'believe it or not', 'let's dive in')\n" +
            "- Do NOT expand acronyms or define terms the original doesn't define\n" +
            "- Do NOT add transitions or context that isn't already there\n\n" +
            "Every idea in your output must come from the original text. If a claim isn't in the original, don't add it. If a fact isn't in the original, don't include it. Your job is to change the register, not the content. Preserve meaning, facts, numbers, names, and proper nouns exactly. " + Suffix,
        EditingMode.CustomPrompt => "",
        EditingMode.CutFiller =>
            "You are a writing assistant. Rewrite the following text by replacing multi-word filler phrases with shorter equivalents. Apply these common swaps wherever they appear:\n\n" +
            "- 'in order to' → 'to'\n" +
            "- 'due to the fact that' → 'because'\n" +
            "- 'at this point in time' → 'now'\n" +
            "- 'in the event that' → 'if'\n" +
            "- 'for the purpose of' → 'to'\n" +
            "- 'with respect to' / 'with regard to' → 'about' or 'for'\n" +
            "- 'take into consideration' → 'consider'\n" +
            "- 'make reference to' → 'reference' or 'mention'\n" +
            "- 'in close proximity to' → 'near'\n" +
            "- 'a large number of' → 'many'\n" +
            "- 'despite the fact that' → 'although'\n" +
            "- 'a majority of' → 'most'\n" +
            "- 'in the near future' → 'soon'\n" +
            "- 'at the present time' → 'now'\n" +
            "- 'in spite of the fact that' → 'although'\n" +
            "- 'has the ability to' → 'can'\n" +
            "- 'on a regular basis' → 'regularly'\n\n" +
            "Only swap where the short form keeps the same meaning. Do not rewrite voice, invert sentences, or change anything else. Preserve meaning, facts, numbers, and proper nouns exactly. " + Suffix,
        EditingMode.LeadWithPoint =>
            "You are a writing assistant. Rewrite the following paragraph so the main point comes first. The paragraph already contains a takeaway — find it and move it to the front. Setup, context, and qualifications move after the takeaway, or get cut if they add nothing once the point is made.\n\n" +
            "What you CAN do:\n" +
            "- Rearrange sentences and clauses\n" +
            "- Cut setup that adds nothing\n" +
            "- Paraphrase for flow, especially when promoting a subordinate clause to a top-level sentence\n" +
            "- Adjust grammar and transitions so the rearrangement reads naturally\n\n" +
            "What you CAN'T do:\n" +
            "- Do NOT add claims, examples, or facts that aren't in the original\n" +
            "- Do NOT add interpretation, commentary, or editorializing\n" +
            "- Do NOT invent metaphors or color phrases (e.g., 'biggest lever', 'game changer')\n" +
            "- Do NOT define acronyms or terms the original doesn't define\n\n" +
            "Examples:\n\n" +
            "Before: 'While many factors contribute to breach impact, including time to detection, response maturity, and awareness, segmentation has emerged as one of the most effective controls for limiting blast radius.'\n" +
            "After: 'Segmentation is one of the most effective controls for limiting blast radius. Many other factors contribute to breach impact, including time to detection, response maturity, and awareness.'\n\n" +
            "Before: 'To mitigate risk, especially given that breaches are often inevitable, security teams must prioritize containment over prevention.'\n" +
            "After: 'Security teams must prioritize containment over prevention. Breaches are often inevitable, so containment is the realistic goal.'\n\n" +
            "The takeaway is usually a claim, recommendation, or result — not a condition or caveat. Preserve meaning, facts, numbers, and proper nouns. " + Suffix,
        EditingMode.ConcreteNouns =>
            "You are a writing assistant. Rewrite the following text by replacing abstract nouns with concrete ones. Abstract nouns — 'solution', 'approach', 'framework', 'capability', 'ecosystem', 'landscape', 'environment', 'infrastructure' — could mean almost anything and leave nothing for the reader to picture.\n\n" +
            "Examples:\n" +
            "- 'the solution' → the actual product name, or 'the product'\n" +
            "- 'the approach' → describe the method briefly\n" +
            "- 'the environment' → 'your data center and cloud workloads'\n" +
            "- 'visibility capabilities' → 'the ability to see traffic between workloads'\n" +
            "- 'communication dependencies' → 'which apps talk to which'\n" +
            "- 'the ecosystem' → the actual systems involved\n\n" +
            "Only replace abstract nouns where you can infer the concrete meaning from context. If a noun is already specific (a proper noun, a product name, a named protocol), leave it alone. Preserve the original meaning, facts, numbers, and proper nouns. " + Suffix,
        EditingMode.CutHedges =>
            "You are a writing assistant. Rewrite the following text by removing hedges and intensifiers. Both weaken prose.\n\n" +
            "Hedges to remove (softeners): 'perhaps', 'arguably', 'somewhat', 'in some cases', 'may', 'possibly', 'could potentially', 'seemingly', 'sort of', 'kind of', 'tends to', 'it appears that'.\n\n" +
            "Intensifiers to remove (weight-adders): 'very', 'extremely', 'significantly', 'quite', 'highly', 'fundamentally', 'truly', 'incredibly', 'essentially', 'really', 'absolutely', 'literally'.\n\n" +
            "Method: remove the word and check whether the sentence still carries the same claim. If yes, the hedge or intensifier was dead weight.\n\n" +
            "Example:\n" +
            "Before: 'This is arguably one of the most significant shifts in the cybersecurity landscape, and it is very likely to fundamentally change how security teams think about defense in depth.'\n" +
            "After: 'This shift is changing how security teams think about defense in depth.'\n\n" +
            "Preserve meaning, facts, numbers, and proper nouns. Do not change voice or tone beyond removing the flagged words. Do not add new claims. " + Suffix,
        EditingMode.UnpackJargon =>
            "You are a writing assistant. The ONLY edit you are allowed to make is adding short parenthetical glosses after the first use of an acronym or an opaque technical term. " +
            "A gloss is a brief explanation in parentheses — 1 to 7 words maximum. Examples: 'SOC (security operations center)', 'MITRE ATT&CK (a framework mapping attacker behavior)', 'TTP (tactic, technique, or procedure)'. " +
            "If the text contains NO acronyms or genuinely opaque terms, return the text UNCHANGED. " +
            "Do NOT write explanatory sentences about any term. Do NOT add context paragraphs. Do NOT define terms a general reader would know (email, HTTP, firewall, internet). Do NOT redefine a term after its first use. Do NOT define terms the original already explains. " +
            "Preserve meaning, facts, numbers, names, proper nouns, voice, and tone. " + Suffix,
        _ => ""
    };

    public static double GetBaseTemperature(this EditingMode mode) => mode switch
    {
        // Grammar fixes are mechanical; creativity here just invites the
        // model to "improve" things outside its scope.
        EditingMode.FixGrammar => 0.0,
        EditingMode.Conversational => 0.5,
        EditingMode.CustomPrompt => 0.3, // uses slider value directly
        _ => 0.3 // all specific technique modes
    };

    public static string GetVariantPrompt(this EditingMode mode, int variant) => mode.GetSystemPrompt();
    public static bool IsRevisionMode(this EditingMode mode) => false;

    /// <summary>
    /// Order the modes should appear in the UI. Decoupled from the enum
    /// declaration order so we can reorder buttons without breaking saved
    /// settings (which serialize EditingMode as its integer rawValue).
    /// `Custom` goes last because it's the escape hatch — users scan
    /// task-specific modes first, fall back to Custom only when nothing fits.
    /// </summary>
    public static readonly IReadOnlyList<EditingMode> UiOrder = new[]
    {
        EditingMode.FixGrammar,
        EditingMode.ShorterSentences,
        EditingMode.LeadWithPoint,
        EditingMode.Plainify,
        EditingMode.CutFiller,
        EditingMode.ActiveVoice,
        EditingMode.ActiveVerbs,
        EditingMode.Tighten,           // "Revise Nominalizations"
        EditingMode.CutGerunds,
        EditingMode.ConcreteNouns,
        EditingMode.CutHedges,
        EditingMode.UnpackJargon,
        EditingMode.DropJargon,
        EditingMode.BulletPoints,
        EditingMode.Conversational,
        EditingMode.CustomPrompt,      // always last — escape hatch
    };
}
