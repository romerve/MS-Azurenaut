# Month 2 minute-by-minute talk track

| Minute | Facilitator action | Evidence on screen |
|---:|---|---|
| 00–02 | State that the workshop is standalone and local. | README prerequisites |
| 02–05 | Run health baseline and frame evidence-over-assertion. | `dotnet test ... --no-build` |
| 05–08 | Explain why a one-line issue forces policy guesses. | Vague issue |
| 08–10 | Ask the room to identify missing behavior. | Blank acceptance criteria |
| 10–12 | Run challenge context demo. | `Readiness: insufficient` |
| 12–14 | Distinguish deterministic input analysis from model output. | Challenge fixture |
| 14–17 | Introduce four persisted context layers. | Context-flow diagram |
| 17–20 | Read the five Given/When/Then scenarios. | Improved issue |
| 20–22 | Run solution context demo. | `Readiness: scoped` |
| 22–25 | Trace validation before permit consumption. | `Program.cs` |
| 25–27 | Trace the fixed window and clock seam. | `ClientRateLimiter.cs` |
| 27–30 | Run focused acceptance tests. | Passing acceptance tests |
| 30–33 | Map tests to acceptance and review checklist. | PR template |
| 33–35 | Close primary topic: persist context, demand evidence. | Take-home assets |
| 35–38 | Introduce the large-cart integer-overflow symptom. | Buggy fixture |
| 38–41 | Run the red stage; pause on the failed assertion. | `RED confirmed` |
| 41–43 | Explain smallest-fix-before-refactor discipline. | Green fixture |
| 43–45 | Explain why decimal is the money boundary. | Calculator diff |
| 45–48 | Run all three isolated stages. | Red/green/refactor markers |
| 48–50 | Compare green and refactor fixtures. | Helper extraction |
| 50–52 | Run production regression filter. | Passing test |
| 52–54 | Connect actual commands to reviewer trust. | Expected outputs |
| 54–55 | Close secondary topic: reproduce, fix, refactor. | Bug-lab command |
| 55–58 | Review checklist and reset path. | Facilitator checklist |
| 58–60 | State outcomes and final take-home command. | `./scripts/validate.zsh` |
