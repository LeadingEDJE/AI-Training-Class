# Project 3 - Facilitator Notes

Run-of-show, what to watch for at each step, and talking points for the long waits. The slide deck is one cue card per step; these notes are what you say while it's up.

Slides: open `slides/Project3-Slides.html` in a browser. Arrow keys advance, F toggles fullscreen. `slides/Project3-Slides.pdf` is the same deck for projectors that only take a PDF.

## The one idea

**The AI is only as good as the context you give it, and you control the context.** Say it before Part 1, say it when they stash the wrong-way attempt, say it at the end. Every step in Part 2 is a kind of context: a map, working commands, specialized agents, a retrospective, a plan.

## Run-of-show

Times assume 90 minutes. Cuts for 60 are at the bottom.

| Clock | Slide | Step | Attendees | You |
|---|---|---|---|---|
| 0:00 | 1 | Intro | Listen | 2 min. The one idea. "Tonight you'll do this on your own repo, today you learn the moves." |
| 0:02 | 2 | Before you start | Pick repo, cut `brownfield-lab` branch, push if it's theirs | 5 min. Confirm everyone's agent is started inside the repo folder, not the workshop root |
| 0:07 | 3 | Part 1 - wrong way | Prompt 1, default model, no plan mode. Hands off for 10 min | Walk the room. Don't help. Note who gets a build error, who gets a confident "done" that isn't |
| 0:17 | 4 | Stash and reset | `git stash push` | 2 min. Ask 3 people: did it build? did you see it? The answer is almost always no. Then the one idea |
| 0:19 | 5 | Step 1 - Map | Prompt 2 → `ARCHITECTURE.md` | Mention `cartographer`. Ask someone to read one paragraph of their overview aloud and say whether it's right |
| 0:29 | 6 | Step 2 - Build and run | Prompt 3, real output | Own-repo people who stall here stay here. That's the lab for them. Move between them |
| 0:44 | 7 | Step 3 - Agents | Prompt 4 → `.claude/agents/`, `CLAUDE.md` | Have someone read their front-end agent aloud. Ask which sentence could describe any project. Delete it live |
| 0:54 | 8 | Step 4 - Retro | Prompt 5, then `/clear` | 4 min. "This is the cheapest context you'll ever collect." |
| 0:58 | 9 | Step 5 - Plan | Opus, plan mode, Prompt 6 | Check three things over shoulders: did they state their background, did it ask questions, is there a handoff prompt at the top of the plan file |
| 1:12 | 10 | Step 6 - Execute | Prompt 7 | The long wait. Talking points below. Watch the orchestrator delegate |
| 1:37 | 11 | Step 7 - Show and tell | Open the feature, compare with the stash | Pick 3 or 4 people. Same request, what was different? |
| 1:48 | 12 | Close | | The one idea. HumanLayer reading. Cartographer tonight |

## What to watch for

You don't need to know their codebase. Every check is visible over a shoulder.

- **Part 1.** How fast does it start writing code? Does it ask anything? Does it claim to have tested? The best teaching moment is a confident summary sitting next to a red build.
- **Step 1.** Does `ARCHITECTURE.md` name real folders and files? Does it contain "I could not determine"? An overview with zero uncertainty is guessing somewhere. Ask them to find the guess.
- **Step 2.** Did commands actually run with output on screen, or did it write instructions without executing them? Did it record what worked back into the file?
- **Step 3.** Generic versus specific. "Follow best practices" is nothing. "Handlers live in `src/Web/Pages`, use the `IRepository<T>` pattern, run `dotnet test tests/UnitTests`" is context. Count how many agents it made. More than four is usually padding.
- **Step 4.** Did it find anything real? If it says "nothing to add," the session was too shallow or the agent is being polite. Push once.
- **Step 5.** Did it ask questions before planning? Are they pitched to the background the attendee gave? Does the plan name the agents from Step 3? Does the handoff prompt include "not done until running locally"?
- **Step 6.** Does the orchestrator's summary match what the subagents actually reported? Watch for a subagent saying "tests fail" and the orchestrator saying "done." When it declares done, make them open it before they believe it.

## Talking points for the long wait (Step 6)

You have 20 to 30 minutes. Pick from these.

1. **What just happened in Part 1.** No map, no commands, no conventions. It did the only thing it could: pattern-match from training data onto a codebase it hadn't read. That's not a model failure. That's a context failure, and it was ours.
2. **Context is layered.** `ARCHITECTURE.md` is what the code is. `CLAUDE.md` is how we work in it. Agents are who does what. The plan is what we're doing right now. The retro is what we learned. Each one is cheap alone and compounding together.
3. **Why fresh sessions.** Long conversations accumulate wrong turns and cost. A session that starts from written artifacts knows everything the last one learned and none of its mistakes.
4. **Model selection is a budget decision.** Opus to think, Sonnet to do. The orchestrator pattern makes that explicit: one expensive brain, many cheap hands. Show your own `/cost` if you're comfortable.
5. **State your background.** The agent calibrates questions to who it thinks you are. Left unsaid, it guesses, and it usually guesses "expert," which means fewer questions and more assumptions.
6. **"Not done until I can see it."** The most expensive failure in real use is a claim of verification that never happened. The sentence costs nothing and changes behavior.
7. **The retrospective habit.** Two minutes at the end of every session. Ask what it learned. Put it where it belongs. Six weeks later your repo has a context layer no one had to schedule time to write.
8. **Cartographer and friends.** What they did by hand in Step 1 exists as plugins and skills. The point of doing it by hand once is knowing what the tool is producing and whether it's right.

## Handing out features

One feature per attendee at Part 1, reused in Step 5. The list with hand-out text, why each is hard, and what done looks like is in `leader/FEATURE-CHEATSHEET.md`. That folder is gitignored so attendees never see it. Copy it to any machine you present from.

## Fallback repo hooks

Real problems in the vendored copies, if anyone asks "what should I pick?" or finishes early.

### eShopOnWeb

- Build output prints a high-severity `System.Text.Json` advisory and two moderate `Azure.Identity` CVEs.
- Packages are two major versions behind across the board.
- `EmailSender` is a stub with a TODO. Never sends.
- `AuthorizationConstants` hardcodes the default password with its own "don't use in production" TODO.
- Distributed cache TODOs in logout and token revocation. `IMemoryCache` breaks with more than one host.
- Three `xUnit2013` analyzer warnings. Five-minute fix.
- Web and PublicApi each hold their own in-memory database. An admin edit through the API never shows on the storefront. Excellent "why doesn't my change appear?" trap, and Prompt 2 should surface it.
- `/admin` returns 200 for everyone because the Blazor shell checks authorization client-side. `demouser` is not an admin. `admin@microsoft.com` with the same password is.
- Two solution files, `eShopOnWeb.sln` and `Everything.sln`. A bare `dotnet build` fails until you name one.
- It builds under the .NET 10 SDK but the PublicApi returns 500s when rolled forward to the .NET 10 runtime, and 12 integration tests fail. Verified. Good "builds fine, fails at runtime, why?" investigation for someone strong.

### hexo

- 19 npm audit findings including one critical.
- Five failing tests. Four are order-dependent in one file.
- `bin/hexo init` scaffolds a site that defaults to pnpm while the repo itself uses npm.
- The CLI lives in a separate `hexo-cli` package. See whether Prompt 2 catches that boundary.
- Timezone warning on every run.

## Common failure modes and the intervention

| You see | Say |
|---|---|
| Part 1: attendee starts fixing the AI's mistakes | "Hands off. Let it be wrong. That's the point of this ten minutes." |
| Own repo won't build at Step 2, attendee getting frustrated | "This is the real work. Let's make the agent earn it." Sit with them |
| Accepted `ARCHITECTURE.md` or agents unchanged | "Read me the third paragraph. Is that true? Would you put that in a PR description?" |
| Plan looks perfect, no pushback | "Ask it what breaks if it's wrong about the first file it named." |
| Skipped stating their background in Prompt 6 | "Go back and tell it who you are. Watch the questions change." |
| Orchestrator says done, nothing running | "Ask it to paste the actual test output. Then ask it what URL to open." |
| One long conversation across every step | "New session. It has the files. It doesn't need the memory." |

## Cuts for 60 minutes

Do these in order until it fits.

1. Step 4 becomes one sentence at the end of Step 3: "Before you clear, ask it what it learned." Saves 4 min.
2. Step 3 drops to front-end and back-end agents only, 7 min. Saves 3.
3. Step 5 caps at 10 min. Tell them to accept the plan after one round of questions. Saves 5.
4. Step 6 caps at 15 min. Some won't finish. Show-and-tell becomes "show me the plan and how far it got." Saves 10.
5. Never cut Part 1 or the stash comparison. That contrast is the lesson.

## Where this material came from

The mapping prompt condenses the `create-overview` command from the earlier Cursor workshop. The good-versus-bad context-file contrast from that workshop is worth two minutes if you have them: a tailored `AGENTS.md` that maps the repo versus a generic "you are a senior React developer" persona pasted into a vanilla JS project. The reading to recommend at the end is HumanLayer's [Writing a good CLAUDE.md](https://www.humanlayer.dev/blog/writing-a-good-claude-md).
