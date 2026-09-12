# Project 3 - Facilitator Notes

Not for attendees. Run-of-show, what to watch for at each step, and talking points to use while people are heads-down.

## The one idea

Every step exists to land one sentence: **the AI is only as good as the context you gave it, and you control the context.** Say it at the start, prove it at Step 5, say it again at the end.

## Run-of-show

| Clock | Step | You are doing |
|---|---|---|
| 0:00 | Intro | 3 minutes. The one idea, the 10-minute rule, "work on a branch." Point at the fallback cards |
| 0:03 | 1 Pick and run | Walk the room. Anyone still fighting a build at 0:10 gets moved to a fallback, no debate |
| 0:13 | 2 Orient | Ask 2 or 3 people what Prompt 1 got wrong. There is always something |
| 0:23 | 3 Add context | Ask someone to read their raw `/init` output aloud. Then ask what they cut |
| 0:33 | 4 Plan | Quietest stretch. Talking points below |
| 0:48 | 5 Execute and critique | Ask: did anyone's Prompt 4 claim tests passed without showing output? |
| 1:00 | Debrief or continue | 60-minute version ends here with the worksheet round |
| 1:00 | 6 Compare and refine | Own-repo people with a real merge commit get the best payoff here |
| 1:15 | 7 Build a skill | Finish-early work. Fine if only a third of the room gets here |
| 1:27 | Debrief | Worksheet round, then the one idea |

If you are running behind, cut Step 3's critique prompt to "read it, delete half" and fold the reflection into the debrief. Never cut Step 5's fresh-session review. That is where the lesson lands.

## What to watch for, per step

You don't need to know their codebase. Every one of these is visible over a shoulder.

- **Step 2.** Did it read files or answer from the README and folder names? Did it say "I could not verify" anywhere? If the answer has no uncertainty in it, it is guessing somewhere. Ask them to find the guess.
- **Step 3.** Length. Anything over 100 lines is filler. Does it contain the actual test command? Does it contain a convention the attendee recognizes as real? Did the attendee delete anything, or accept it whole?
- **Step 4.** Does the plan name real files? Ask the attendee to open one it named and confirm the function exists. Watch the pushback: when the attendee questions the plan, does the AI cite evidence or just capitulate? Capitulation is the failure mode to name out loud.
- **Step 5.** Did tests actually run, with output on screen? Did the fresh-session review find something the attendee also spotted? Did it find something they missed? Either way, ask "what would have prevented that?"
- **Step 6.** Behavior differences versus style differences. Most differences are style, which means the missing context was convention, not logic.

## Talking points while they work

Use these during Steps 4 and 5 when the room is quiet. Two or three minutes each.

1. **Why `/init` output is too long.** It optimizes for coverage, not for what a new contributor gets corrected on. A good context file is the code review comments you are tired of writing.
2. **Fresh sessions beat long ones.** The implementing session believes its own work. A reviewer with no memory of writing the code finds different things. This is why Step 5 uses two conversations.
3. **The two kinds of wrong.** Wrong facts about the codebase are fixed with context. Wrong judgment about the change is fixed with a better plan and pushback. Attendees should classify each worksheet row.
4. **"Show me the output."** The most expensive failure in real use is a claim of verification that never happened. Make it a habit to demand the actual terminal output, every time.
5. **Skills are just prompts you were tired of retyping.** Step 7 is not advanced. It is the natural end of noticing repetition.

## Fallback repo hooks

Real problems that exist in the vendored copies, useful when someone asks "what should I pick?"

### eShopOnWeb

- Build output prints a high-severity `System.Text.Json` advisory and two moderate `Azure.Identity` CVEs. Good "upgrade without breaking tests" ticket.
- Packages are two major versions behind across the board. Good scoped-upgrade conversation.
- `EmailSender` is a stub with a TODO. Never sends. Good feature ticket with a clear test seam.
- `AuthorizationConstants` hardcodes the default password with its own "don't use in production" TODO.
- Distributed cache TODOs in the logout and token revocation paths. `IMemoryCache` breaks with more than one host. Good architecture discussion.
- Three `xUnit2013` analyzer warnings. Five-minute ticket for someone who finishes early.
- The obsolete `SerializationInfo` constructor on the basket exception. Framework-drift ticket.
- It builds under the .NET 10 SDK but the PublicApi returns 500s when rolled forward to the .NET 10 runtime, and 12 integration tests fail. Verified. Good "builds fine, fails at runtime, why?" investigation for someone strong.

### hexo

- 19 npm audit findings including one critical. Triage exercise: which ones matter for a CLI tool?
- Five failing tests. Four are order-dependent in one file. Good "make tests hermetic" ticket.
- `bin/hexo init` scaffolds a site that defaults to pnpm while the repo itself uses npm. Tooling inconsistency ticket.
- The CLI lives in a separate `hexo-cli` package. Good "map the real module boundary" question for Step 2. See whether Prompt 1 catches it.
- The `husky` prepare script fails silently outside a git checkout. Packaging hygiene.
- Timezone warning on every run. UX cleanup.

## Common failure modes and the intervention

| You see | Say |
|---|---|
| Still fighting their own build at 0:10 | "Switch to a fallback. You can redo this on your repo tomorrow with what you learned." |
| Accepted `/init` output unchanged | "Read me the third paragraph. Is that true? Would you write that in a PR description?" |
| Plan looks perfect, attendee never pushed back | "Ask it what breaks if it's wrong about the file it named first." |
| AI says tests pass, no output on screen | "Ask it to paste the actual test output. Then check the count." |
| Attendee reads the whole codebase themselves during Step 2 | "You're doing its job. Let it be wrong first, then correct it. That's the exercise." |
| Long single conversation across steps | "New session. Point it at the CLAUDE.md and the plan file you already have." |

## Where this material came from

The prompt sequence adapts the brownfield block from the earlier Cursor workshop: explore, generate rules, re-implement a historical feature, compare against the real merge commit, extract missing rules. The good-versus-bad context-file contrast is worth showing if you have a spare minute: a tailored `AGENTS.md` that maps the repo versus a generic "you are a senior React developer" persona pasted into a vanilla JS project. The HumanLayer post [Writing a good CLAUDE.md](https://www.humanlayer.dev/blog/writing-a-good-claude-md) is the reading to recommend at the end.
