# Project 3 - Maintaining a Brownfield Repository

Projects 1 and 2 were greenfield: the AI knew everything about the code because it wrote it. Real work is not like that. In this project you point Claude Code at a codebase it has never seen, watch where it is right and where it is wrong, and learn that the lever you control is **context**.

Use **your own repository** if you can run it and its tests on your laptop within the first 10 minutes. If you can't, switch to one of the two provided fallback repos in the [Optional](#optional-provided-fallback-repositories) section at the bottom. Don't burn the hour fighting a build.

Work on a throwaway branch. Never push AI-generated changes to a shared branch during this lab.

## Core lab (60 minutes)

### Step 1 - Pick and run (10 min)

1. Open a terminal in the repo you're using and start `claude`.
2. Confirm you can build, run, and execute the test suite yourself. Write down the exact commands.
3. If you're not building and testing by minute 10, switch to a fallback repo.

### Step 2 - Orient (10 min)

Do not read the code yourself yet. Start a **fresh conversation** and paste:

#### Prompt 1 - Orientation

> Explore this repository. Tell me: what it does, how to build, run, and test it, where the main entry points are, and how the code is organized. List anything you are unsure about or could not verify. Cite the files you actually read.

Check its answer against what you learned in Step 1. Note anything it got wrong or guessed.

### Step 3 - Add context (10 min)

1. Run `/init` to generate a `CLAUDE.md`.
2. Read it. It's usually too long and half of it is filler. Then paste:

#### Prompt 2 - Critique the context file

> Review CLAUDE.md against the actual code and configuration in this repo. Remove anything that is generic, wrong, or restates what a developer can see in the code. Add the exact build, run, and test commands, the conventions a new contributor would be corrected on in code review, and the two or three places in the codebase that are surprising or easy to get wrong. Keep it under 60 lines.

Read the result again. Delete anything you don't agree with. This file is yours, not the AI's.

### Step 4 - Plan a change (15 min)

Pick a small real change: a bug from your tracker, a feature request, a recent ticket, or one of the suggested tickets in the fallback repo cards below. Aim for something a colleague would finish in an hour or two.

Enter **plan mode** (`Shift+Tab` twice, or start the prompt with the request to plan only) and paste:

#### Prompt 3 - Plan only

> Here is a ticket: [paste the ticket text or describe the change]. Investigate the codebase and propose an implementation plan. Do not write code yet. The plan must name the specific files and functions you would change, list the existing tests that cover this area, describe how you would verify the change, and call out any assumptions or open questions you have for me.

Push back on the plan at least once. Ask "why that file and not this one?" or "what breaks if you're wrong about X?" Watch whether it defends the plan with evidence or just agrees with you.

### Step 5 - Execute and critique (15 min)

Accept the plan and let it implement.

#### Prompt 4 - Implement and verify

> Implement the plan. Run the build and the relevant tests, and show me the actual output. If anything fails, fix it and re-run. Then summarize what you changed and what you could not verify.

Now open a **new conversation** (fresh context) and paste:

#### Prompt 5 - Skeptical review

> Review the uncommitted changes in this repository as a skeptical senior engineer who knows this codebase well. Look for logic errors, missed edge cases, changes that don't match the existing conventions, and anything that was claimed as tested but isn't. Be specific and cite lines. Don't fix anything.

Before moving on, write two lines in the worksheet below: one thing the AI got right that you would not have expected, and one thing it got wrong that better context would have prevented.

## Stretch (if we have 90 minutes)

### Step 6 - Compare and refine (15 min)

**If you're on your own repo** and picked a ticket that has already been fixed, compare the AI's version to the real one:

#### Prompt 6 - Compare against the real fix

> This change was already implemented in commit [hash or PR number]. Compare my uncommitted implementation to that commit. Classify each difference as cosmetic, a style or convention mismatch, or a behavior difference. For each behavior difference, say which version is correct and why.

**If you're on a fallback repo** there is no known answer. Instead, compare the Step 5 review to your own reading of the diff. Which findings were real, which were noise?

Then, in either case:

#### Prompt 7 - Extract the missing context

> Based on the differences and review findings above, what information about this codebase would have gotten you closer to the correct implementation on the first try? Focus on implicit conventions, architectural boundaries, and examples to follow. Don't include the answer itself. Propose additions to CLAUDE.md.

Apply the additions you agree with. Re-run Steps 4 and 5 on a second ticket if time allows, and see whether the plan improves.

### Step 7 - Build a skill (15 min)

Take the review prompt from Step 5, or whatever prompt you found yourself repeating, and turn it into a reusable skill.

#### Prompt 8 - Create a skill

> Create a skill in .claude/skills/ called review-change that reviews the uncommitted diff the way a senior engineer on this project would. Include the specific conventions and pitfalls we captured in CLAUDE.md, require that it cite file and line for every finding, and forbid it from making edits. Then run it on the current diff.

## Worksheet

Fill this in as you go. We'll compare notes as a group.

| | What happened | What context would have changed it |
|---|---|---|
| Got right, unexpectedly | | |
| Got wrong | | |
| Claimed but didn't verify | | |
| Question it should have asked me | | |

## Tips

- Fresh conversation per step. Long conversations drift and cost more. Make each step produce an artifact (`CLAUDE.md`, a plan file, a diff) and start the next step from that.
- Use a heavier model and higher effort for Steps 2 to 4, a cheaper one for Step 5's implementation. See [resources/model-selection.md](../../resources/model-selection.md).
- "Show me the actual output" is not optional. The most common failure is a confident claim that tests pass when they were never run.
- When it asks you a question, answer it. When it doesn't ask and should have, note that in the worksheet. That's a context gap.
- Recommended reading after the lab: [Writing a good CLAUDE.md](https://www.humanlayer.dev/blog/writing-a-good-claude-md). Their root file is under 60 lines.

## Optional: provided fallback repositories

Both were copied into this folder from public open-source projects with their CI, deploy, and AI-helper files removed. Licenses are preserved in each folder. Neither has a `CLAUDE.md`, on purpose.

### eShopOnWeb (.NET)

Microsoft's reference e-commerce sample. ASP.NET Core 8, Razor Pages web app plus a public API, clean-architecture layout. Upstream was archived in January 2025, so its dependencies are already stale. Configured here to use an in-memory database, so no SQL Server is needed.

**Prerequisite:** the .NET 8 SDK. A newer SDK can sit alongside it, but the app targets .NET 8 and returns errors on the .NET 10 runtime, so .NET 8 itself must be installed. If your browser complains about the HTTPS certificate, run `dotnet dev-certs https --trust` once.

```
cd projects/3-Brownfield/eShopOnWeb
dotnet build eShopOnWeb.sln
dotnet test eShopOnWeb.sln
```

Terminal 1:

```
cd projects/3-Brownfield/eShopOnWeb/src/PublicApi
dotnet run --launch-profile PublicApi
```

Terminal 2:

```
cd projects/3-Brownfield/eShopOnWeb/src/Web
dotnet run --launch-profile Web
```

Open `https://localhost:5001`. Admin area is at `/admin`. Log in with `demouser@microsoft.com` / `Pass@word1`.

Suggested tickets (these are real issues in the code, not invented):

- The build prints a high-severity security advisory for `System.Text.Json`. Upgrade the affected packages without breaking the tests.
- `EmailSender` is a stub that never sends anything. Implement it behind an interface with a fake for tests, and wire it into the order confirmation flow.
- The default password is hardcoded in `AuthorizationConstants` with a TODO. Move it to configuration and make the seeder read it.
- Three xUnit analyzer warnings complain about `Assert.Equal` being used to check collection sizes. Fix them properly.

### hexo (Node)

A static-site generator written in TypeScript, about 22k lines, with a real open issue backlog. `hexo-site` is a small pre-scaffolded blog that points at the local `hexo` source, so your edits to `hexo/lib` show up when you regenerate the site.

**Prerequisite:** Node.js 20.19 or newer.

```
cd projects/3-Brownfield/hexo
npm ci --ignore-scripts
npm run build
npm test
```

Use `--ignore-scripts` here. The project's install hook tries to register git hooks, and this folder isn't its own git repo. Five tests fail out of about 1,300. That is the upstream state of the project, not your mistake. Four are order-dependent in one file. That's a ticket if you want it.

The site needs the `npm run build` above to have finished first. It loads hexo from the compiled `dist` folder.

```
cd ../hexo-site
npm install
npx hexo generate
npx hexo server -p 4111
```

Open `http://localhost:4111`. Port 4000 is hexo's default but is often taken by something else on developer laptops.

Suggested tickets from the upstream tracker:

- [hexojs/hexo#5747](https://github.com/hexojs/hexo/issues/5747) - watch mode warns about recreating files it created during the previous run.
- [hexojs/hexo#5479](https://github.com/hexojs/hexo/issues/5479) - a `.j2` file in `code_dir` causes a rendering error.
- [hexojs/hexo#5635](https://github.com/hexojs/hexo/issues/5635) - Markdown gets escaped when two tag plugins share a prefix.
- The four order-dependent failures in `test/scripts/processors/asset.ts`. Make them pass in any order.
