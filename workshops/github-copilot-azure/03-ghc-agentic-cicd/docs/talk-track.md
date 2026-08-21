# Minute-by-minute talk track

| Minute | Presenter action and words |
|---|---|
| 00–02 | “Today we make two things reviewable: an agent's diagnosis and a release's claims.” |
| 02–05 | Show [`architecture.mmd`](./architecture.mmd); state that Azure is optional and pre-existing only. |
| 05–08 | Open the broken workflow. Ask for likely causes without confirming any. |
| 08–10 | Define “established,” “inferred,” and “not established.” |
| 10–12 | Open the sanitized log and identify the command and MSB1009 lines. |
| 12–14 | Run the broken command; confirm the safe local failure. |
| 14–17 | Introduce the Ground → Constrain → Review → Validate loop. |
| 17–21 | Read the worksheet prompt; emphasize exact citations and smallest patch. |
| 21–25 | Run Copilot live if available. Score its variable response, not its fluency. |
| 25–28 | Compare with corrected CI and run the workflow validator. |
| 28–31 | Review OIDC, minimal permissions, variables, timeout, concurrency, and existing-target update. |
| 31–34 | Ask an attendee to explain why no Azure permission increase is justified. |
| 34–35 | Transition: “Green CI is one input to a release claim.” |
| 35–38 | List six common release claims; ask where each evidence item lives. |
| 38–41 | Show sample summary/manifest and the documented hash-mismatch failure. |
| 41–44 | Trace dependency review → CodeQL → test → digest → SBOM/provenance. |
| 44–46 | Explain the protected production approval as evidence, not a YAML-only promise. |
| 46–49 | Review the supply-chain workflow and its permissions. |
| 49–51 | Run the evidence validator; open one citation and recompute mentally. |
| 51–53 | Show generated templates; mark absent runtime health “not established.” |
| 53–55 | Ask attendees to reject one unsupported green statement. |
| 55–57 | Run or show local validation and smoke results. |
| 57–59 | Recap outcomes and optional activation prerequisites. |
| 59–60 | Share reset command and official references; take final question. |

Do not script a fixed model answer. The live response is intentionally variable and
is accepted only when it satisfies the deterministic worksheet.
