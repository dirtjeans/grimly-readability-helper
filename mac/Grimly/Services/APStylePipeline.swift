import Foundation

/// AP Style pipeline for Grimly. Deterministic pass first, then a narrow
/// LLM pass for judgment calls — the code pass nails the mechanical rules
/// 100% of the time and hands the LLM cleaner input so its judgment work
/// is easier.
///
/// The LLM pass is limited to three categories only (attribution verbs,
/// passive-voice attribution, editorial framing) so a small on-device model
/// doesn't drift into rewrites that overlap with the deterministic pass.
///
/// Prompt-echo guard: small on-device models sometimes echo the system
/// prompt verbatim instead of returning the rewritten text. If the LLM's
/// response contains any of the sentinel strings, fall back to the
/// code-pass output rather than leaking prompt instructions into the diff.
///
/// 1:1 port of `Grimly.Core/Services/ApStylePipeline.cs`.
final class APStylePipeline {
    private let codePass: APStyleCodePass
    private let client: FoundryLocalClient

    init(codePass: APStyleCodePass, client: FoundryLocalClient) {
        self.codePass = codePass
        self.client = client
    }

    func run(_ text: String) async -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return text }

        // Pass 1: mechanical AP rules.
        let afterCodePass = codePass.apply(text)

        // Pass 2: LLM judgment pass. Narrow scope — three categories only,
        // with explicit "no other changes" language to reduce drift on
        // small models.
        let prompt = """
            Revise this text to comply with the Associated Press Stylebook. Apply ONLY these changes:
              1. Attribution verbs: prefer 'said' over 'claimed', 'stated', 'noted', 'commented', 'remarked', 'expressed', 'declared'. Use 'said' unless the sentence specifically calls for a different verb.
              2. Passive-voice attribution: rewrite 'was said by X' as 'X said'.
              3. Editorial framing: strip alarmist, promotional, or opinion-loaded adjectives ('groundbreaking', 'shocking', 'unprecedented' — unless the source is quoted saying so).

            Do NOT change: sentence order, sentence structure beyond active/passive swaps, factual content, punctuation, capitalization, numbers, times, dates, or word choice outside the categories above. If none of the three categories apply, return the text unchanged.

            Return ONLY the revised text — no preamble, no explanation, no quotes.
            """

        let refined: String
        do {
            refined = try await client.getEditedText(
                originalText: afterCodePass,
                mode: .customPrompt,
                customPrompt: prompt,
                temperature: 0.0
            )
        } catch {
            return afterCodePass
        }

        let refinedTrim = refined.trimmingCharacters(in: .whitespacesAndNewlines)
        if refinedTrim.isEmpty { return afterCodePass }

        if Self.looksLikePromptEcho(refined) { return afterCodePass }

        // Strip any wrapping quotes the model may have added.
        return refinedTrim.trimmingCharacters(in: CharacterSet(charactersIn: "\""))
    }

    // Signature phrases seen in prompt-echo failures on Phi and Qwen.
    // Kept in sync with the Windows list.
    private static let echoSignatures: [String] = [
        // Direct prompt echoes.
        "Apply ONLY these changes",
        "Attribution verbs: prefer 'said'",
        "Passive-voice attribution",
        "Editorial framing",
        "Return ONLY the revised text",
        "Associated Press Stylebook",

        // Meta-commentary preambles small models tack on.
        "You're about to",
        "Here's the revised",
        "Here is the revised",
        "Here's the rewritten",
        "I've made the following",
        "I have made the following",
        "The goal is to",
        "Remember,",
        "Sure, here",
        "Let me revise",
        "In this revision",
        "Note that",
        "As requested",
    ]

    private static func looksLikePromptEcho(_ raw: String) -> Bool {
        let lower = raw.lowercased()
        for sig in echoSignatures {
            if lower.contains(sig.lowercased()) { return true }
        }
        return false
    }
}
