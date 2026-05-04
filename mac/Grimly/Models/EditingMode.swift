import Foundation

enum EditingMode: Int, Codable, CaseIterable, Identifiable {
    // Existing cases — do not reorder. Saved settings serialize by rawValue.
    case fixGrammar
    case shorterSentences
    case shorterWords
    case activeVoice
    case activeVerbs
    case reviseNominalizations
    case cutGerunds
    case dropJargon
    case bulletPoints
    case plainEnglish
    case conversational
    case customPrompt
    // --- Appended 2026-04-15 for readability-checklist coverage ---
    case cutFiller
    case leadWithPoint
    case concreteNouns
    case cutHedges
    case unpackJargon

    var id: Int { rawValue }

    var displayName: String {
        switch self {
        case .fixGrammar: return "Fix Grammar"
        case .shorterSentences: return "Shorter Sentences"
        case .shorterWords: return "Shorter Words"
        case .activeVoice: return "Active Voice"
        case .activeVerbs: return "Active Verbs"
        case .reviseNominalizations: return "Revise Nominalizations"
        case .cutGerunds: return "Cut Gerunds"
        case .dropJargon: return "Drop Jargon"
        case .bulletPoints: return "Bullet Points"
        case .plainEnglish: return "Plain English"
        case .conversational: return "Conversational"
        case .customPrompt: return "Custom"
        case .cutFiller: return "Cut Filler"
        case .leadWithPoint: return "Lead With Point"
        case .concreteNouns: return "Concrete Nouns"
        case .cutHedges: return "Cut Hedges"
        case .unpackJargon: return "Unpack Terms"
        }
    }

    var systemPrompt: String {
        switch self {
        case .fixGrammar:
            return """
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

                Return ONLY the corrected text with no explanations, preamble, or commentary.
                """
        case .shorterSentences:
            return "You are a writing assistant. Rewrite the following text to improve readability. Break up long sentences into shorter ones. Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .shorterWords:
            return "You are a writing assistant. Rewrite the following text by replacing long or complex words with shorter, simpler alternatives that have fewer syllables. For example, 'utilize' becomes 'use', 'approximately' becomes 'about', 'demonstrate' becomes 'show'. Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .activeVoice:
            return "You are a writing assistant. Rewrite the following text by converting passive voice constructions to active voice (e.g., 'The report was written by the team' becomes 'The team wrote the report', 'Mistakes were made' becomes 'We made mistakes'). Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .activeVerbs:
            return "You are a writing assistant. Rewrite the following text using active voice wherever possible. Convert passive constructions to active ones. Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .reviseNominalizations:
            return """
                You are a writing assistant. Rewrite the following text by pulling verbs back out of noun phrases. Nominalizations — verbs trapped inside nouns — flatten prose. Two patterns to fix:

                (1) Nominalizations with 'of': -tion, -ment, -ance, -ence, or -ity nouns dragging 'of' behind them. Convert back to verbs.
                  - 'the implementation of segmentation' → 'implementing segmentation' or 'segmenting'
                  - 'the reduction of risk' → 'reducing risk'
                  - 'the identification of vulnerabilities' → 'identifying vulnerabilities'
                  - 'the prevention of lateral movement' → 'preventing lateral movement'

                (2) Weak verb + nominalized object: 'make', 'give', 'take', 'perform', 'conduct', or 'provide' paired with a noun that's really a verb. Collapse to the real verb.
                  - 'make a decision' → 'decide'
                  - 'perform an analysis' → 'analyze'
                  - 'conduct a review' → 'review'
                  - 'give consideration to' → 'consider'
                  - 'make a determination' → 'determine'

                Do NOT touch gerund stacks ('the implementing of the monitoring of endpoints') — a separate mode (Cut Gerunds) handles those. Preserve the original meaning, facts, numbers, and proper nouns. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .cutGerunds:
            return """
                You are a writing assistant. Rewrite the following text to eliminate gerund stacks — chains of two or more -ing verbs in the same clause, usually joined by 'of', 'the', or 'and'. Gerund stacks make prose abstract and hard to parse. Replace each stack with a concrete finite verb or a cleaner noun phrase.

                Examples:
                - 'the implementing of the monitoring of endpoints' → 'monitoring endpoints'
                - 'We are improving our tracking of the reporting of security events.' → 'We are improving how we report security events.'
                - 'Focusing on implementing Zero Trust.' → 'We are implementing Zero Trust.'
                - 'Reducing the impact of attacking sophisticated threats.' → 'Reducing the impact of sophisticated attacks.'

                Leave isolated gerunds alone — only fix clusters where two or more -ing words appear together in the same clause. Leave gerunds that are the subject of a sentence alone ('Running tests is important' stays). Do not touch nominalizations ('made a decision') — the Revise Nominalizations mode handles those. Preserve the original meaning, facts, numbers, names, and proper nouns. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .dropJargon:
            return "You are a writing assistant. Rewrite the following text to improve readability. Remove jargon and replace it with plain language. Remove or simplify adjectives and adverbs that don't add meaning — if a modifier doesn't change the reader's understanding, cut it. Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .bulletPoints:
            return """
                You are a writing assistant. Return the ENTIRE text, keeping all sentences exactly as they are EXCEPT for sentences that contain a sequence of three or more items joined by commas or 'and'. For those sentences only, extract the items into a bullet point list with the introductory part ending in a colon. For example, 'If you catch on fire, you should stop what you're doing, drop to the ground, and roll around to put it out.' becomes:

                If you catch on fire, you should:
                * Stop what you're doing
                * Drop to the ground
                * Roll around to put it out

                IMPORTANT: You must include ALL of the original text in your response. Only modify sentences that contain item sequences. Every other sentence must appear unchanged. Return ONLY the full rewritten text with no explanations, preamble, or commentary.
                """
        case .plainEnglish:
            return "You are a writing assistant. Rewrite the following text in plain English. Use straightforward, everyday language while keeping it professional. Avoid jargon, buzzwords, and overly formal phrasing. Preserve the original meaning. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary."
        case .conversational:
            return """
                You are a writing assistant. Rewrite the following text in a conversational but professional tone, as if explaining it to a colleague.

                What conversational means here:
                - Use contractions (it's, don't, you're)
                - Use shorter, more direct sentences
                - Use 'you' and 'your' when it fits
                - Prefer common words over formal ones

                What conversational does NOT mean:
                - Do NOT add new ideas, examples, explanations, or commentary
                - Do NOT add color phrases ('interestingly', 'believe it or not', 'let's dive in')
                - Do NOT expand acronyms or define terms the original doesn't define
                - Do NOT add transitions or context that isn't already there

                Every idea in your output must come from the original text. If a claim isn't in the original, don't add it. If a fact isn't in the original, don't include it. Your job is to change the register, not the content. Preserve meaning, facts, numbers, names, and proper nouns exactly. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .customPrompt:
            return ""
        case .cutFiller:
            return """
                You are a writing assistant. Rewrite the following text by replacing multi-word filler phrases with shorter equivalents. Apply these common swaps wherever they appear:

                - 'in order to' → 'to'
                - 'due to the fact that' → 'because'
                - 'at this point in time' → 'now'
                - 'in the event that' → 'if'
                - 'for the purpose of' → 'to'
                - 'with respect to' / 'with regard to' → 'about' or 'for'
                - 'take into consideration' → 'consider'
                - 'make reference to' → 'reference' or 'mention'
                - 'in close proximity to' → 'near'
                - 'a large number of' → 'many'
                - 'despite the fact that' → 'although'
                - 'a majority of' → 'most'
                - 'in the near future' → 'soon'
                - 'at the present time' → 'now'
                - 'in spite of the fact that' → 'although'
                - 'has the ability to' → 'can'
                - 'on a regular basis' → 'regularly'

                Only swap where the short form keeps the same meaning. Do not rewrite voice, invert sentences, or change anything else. Preserve meaning, facts, numbers, and proper nouns exactly. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .leadWithPoint:
            return """
                You are a writing assistant. Rewrite the following paragraph so the main point comes first. The paragraph already contains a takeaway — find it and move it to the front. Setup, context, and qualifications move after the takeaway, or get cut if they add nothing once the point is made.

                What you CAN do:
                - Rearrange sentences and clauses
                - Cut setup that adds nothing
                - Paraphrase for flow, especially when promoting a subordinate clause to a top-level sentence
                - Adjust grammar and transitions so the rearrangement reads naturally

                What you CAN'T do:
                - Do NOT add claims, examples, or facts that aren't in the original
                - Do NOT add interpretation, commentary, or editorializing
                - Do NOT invent metaphors or color phrases (e.g., 'biggest lever', 'game changer')
                - Do NOT define acronyms or terms the original doesn't define

                Examples:

                Before: 'While many factors contribute to breach impact, including time to detection, response maturity, and awareness, segmentation has emerged as one of the most effective controls for limiting blast radius.'
                After: 'Segmentation is one of the most effective controls for limiting blast radius. Many other factors contribute to breach impact, including time to detection, response maturity, and awareness.'

                Before: 'To mitigate risk, especially given that breaches are often inevitable, security teams must prioritize containment over prevention.'
                After: 'Security teams must prioritize containment over prevention. Breaches are often inevitable, so containment is the realistic goal.'

                The takeaway is usually a claim, recommendation, or result — not a condition or caveat. Preserve meaning, facts, numbers, and proper nouns. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .concreteNouns:
            return """
                You are a writing assistant. Rewrite the following text by replacing abstract nouns with concrete ones. Abstract nouns — 'solution', 'approach', 'framework', 'capability', 'ecosystem', 'landscape', 'environment', 'infrastructure' — could mean almost anything and leave nothing for the reader to picture.

                Examples:
                - 'the solution' → the actual product name, or 'the product'
                - 'the approach' → describe the method briefly
                - 'the environment' → 'your data center and cloud workloads'
                - 'visibility capabilities' → 'the ability to see traffic between workloads'
                - 'communication dependencies' → 'which apps talk to which'
                - 'the ecosystem' → the actual systems involved

                Only replace abstract nouns where you can infer the concrete meaning from context. If a noun is already specific (a proper noun, a product name, a named protocol), leave it alone. Preserve the original meaning, facts, numbers, and proper nouns. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .cutHedges:
            return """
                You are a writing assistant. Rewrite the following text by removing hedges and intensifiers. Both weaken prose.

                Hedges to remove (softeners): 'perhaps', 'arguably', 'somewhat', 'in some cases', 'may', 'possibly', 'could potentially', 'seemingly', 'sort of', 'kind of', 'tends to', 'it appears that'.

                Intensifiers to remove (weight-adders): 'very', 'extremely', 'significantly', 'quite', 'highly', 'fundamentally', 'truly', 'incredibly', 'essentially', 'really', 'absolutely', 'literally'.

                Method: remove the word and check whether the sentence still carries the same claim. If yes, the hedge or intensifier was dead weight.

                Example:
                Before: 'This is arguably one of the most significant shifts in the cybersecurity landscape, and it is very likely to fundamentally change how security teams think about defense in depth.'
                After: 'This shift is changing how security teams think about defense in depth.'

                Preserve meaning, facts, numbers, and proper nouns. Do not change voice or tone beyond removing the flagged words. Do not add new claims. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        case .unpackJargon:
            return """
                You are a writing assistant. The ONLY edit you are allowed to make is adding short parenthetical glosses after the first use of an acronym or an opaque technical term. A gloss is a brief explanation in parentheses — 1 to 7 words maximum. Examples: 'SOC (security operations center)', 'MITRE ATT&CK (a framework mapping attacker behavior)', 'TTP (tactic, technique, or procedure)'.

                If the text contains NO acronyms or genuinely opaque terms, return the text UNCHANGED.

                Do NOT write explanatory sentences about any term. Do NOT add context paragraphs. Do NOT define terms a general reader would know (email, HTTP, firewall, internet). Do NOT redefine a term after its first use. Do NOT define terms the original already explains.

                Preserve meaning, facts, numbers, names, proper nouns, voice, and tone. Your entire response must be only the rewritten text, matching the input's paragraph structure — do not include the original text alongside it, do not include labels like 'Rewritten:' or 'After:' or 'Version 1:', do not list the changes you made, do not give multiple versions or alternatives, do not add any explanation, preamble, or commentary.
                """
        }
    }

    var baseTemperature: Double {
        switch self {
        case .plainEnglish, .conversational: return 0.5
        // Grammar fixes are mechanical; creativity here just invites the
        // model to "improve" things outside its scope.
        case .fixGrammar: return 0.0
        default: return 0.3
        }
    }

    /// UI ordering for the mode pills. Decoupled from the enum's declaration
    /// order so we can reshuffle buttons without breaking saved settings
    /// (which serialize by `rawValue`). `.customPrompt` stays last — it's the
    /// escape hatch, reached only after scanning task-specific modes.
    static let uiOrder: [EditingMode] = [
        .fixGrammar,
        .shorterSentences,
        .leadWithPoint,
        .shorterWords,
        .plainEnglish,
        .cutFiller,
        .activeVoice,
        .activeVerbs,
        .reviseNominalizations,
        .cutGerunds,
        .concreteNouns,
        .cutHedges,
        .unpackJargon,
        .dropJargon,
        .bulletPoints,
        .conversational,
        .customPrompt,  // always last — escape hatch
    ]
}
