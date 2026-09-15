# Project 3 - Maintaining a Brownfield Repository

> **The AI is only as good as the context you give it, and you control the context.**

Projects 1 and 2 were greenfield. The AI knew everything about the code because it wrote it. Real work is not like that. In this lab you point an agent at a codebase it has never seen, watch it fail, then do the work that makes it succeed.

Use **your own repository** if you brought one. Otherwise use one of the two provided fallback repos in the [Optional](#optional-provided-fallback-repositories) section at the bottom. Work on a throwaway branch either way. Never push AI-generated changes to a shared branch during this lab.

Every prompt below is a starting point. Edit the bracketed parts. Paste the rest.

## Before you start (5 minutes)

1. Pick your repo: the one you brought, or a fallback from the bottom of this page. Open a terminal **inside that folder**. Your agent should start there, not at the workshop root.
2. Cut a branch:

   ```
   git checkout -b brownfield-lab
   ```

3. If it's your own repo, push the branch so your work survives the day:

   ```
   git push -u origin brownfield-lab
   ```

   If you're on a fallback repo, keep the branch local. You don't have push rights to the workshop repo. Fork it first if you want to keep your work.

4. Fallback prerequisites: the .NET 8 SDK for eShopOnWeb, or Node.js 22 for hexo. Check with `dotnet --list-sdks` or `node --version`. Per-platform install commands are in each repo's `RUNNING.md`.

## Part 1 - The wrong way (10 minutes)

*NOTE:  We may skip this depending on time.*

Open a terminal **inside the repo folder**, start your agent, and ask for a feature. No planning, no context, default model. No plugins, extra skills, or MCP servers either. If you have any installed, turn them off for this part. They are context too, and the point is to see what happens without it.

Before you paste the prompt, switch to **auto mode** so it doesn't stop to ask permission for every edit and command. In Claude Code, press `Shift+Tab` until the mode indicator shows auto, or start with `claude --permission-mode auto`. We want to see what it does when nobody is steering.

### Prompt 1 - Just build it

> Add [the feature your instructor gave you, in one or two sentences]. Make assumptions and just get it done.

Set a 10-minute timer and let it run. Answer its questions if it asks any. When time is up, don't fix anything. Lets talk through the results and why some will be better than other.

- Did it build? Did it run? Did you see the feature work?
- What did it assume about the codebase that you know is wrong?
- What questions should it have asked you first?

Then put the attempt aside so we can compare later:

```
git add -A
git stash push -m "wrong-way attempt"
```

## Part 2 - The right way

### Step 1 - Map the codebase (10 minutes)

The thorough version of this is a tool like the `cartographer` plugin, which fans out subagents and produces a full codebase map. It's worth running on your real repo tonight. It takes too long for this room, so use the short version. Start a **fresh session** in the repo folder.

Use **auto mode** again. Manual or Accept Edits mode also works here if you want to step through what it reads and writes, but we have limited time, so auto is recommended.

### Prompt 2 - Overview

> Give me a high-level overview of this repository, starting at the top level. Identify what it is meant to do and how it does it. Identify the primary components, services, and layers, what each is responsible for, and the common patterns used across the repo. Then dig into each component you identified and describe its key files, its entry points, and how it talks to the others. Skip build output and dependency folders. Save the result as ARCHITECTURE.md in the repo root, with a consistent structure for every component and a simple ASCII tree of the folder layout with a one-line purpose for each folder. Tell me anything you could not determine or are guessing about.  Use Sonnet sub-agents to scout the codebase after you get the high level so you can avoid bloating your own context.  If appropriate, add additional documentation to the codebase calling out inconsistencies with patterns that are otherwise standard.  Make this easy for future agents to understand where to look.

Skim the file while it's writing. Correct anything you know is wrong before moving on. That correction is context.

### Step 2 - Get it building and running (10 to 20 minutes)

Same session.

### Prompt 3 - Build, run, test

> Using ARCHITECTURE.md, figure out how to build this project, run it locally, and execute its test suite. Actually run each command and show me the real output. If something fails because of my environment, diagnose it and tell me what to install or change, then retry. When everything works, add a "Running locally" section to ARCHITECTURE.md with the exact commands that succeeded, the URLs or entry points to open, and any gotchas you hit.

**If you are working on your own project and don't have a way to run it locally, this may be your whole lab, and it'll be worth it.** We will help you find a solution.

Having a way for the agents to actually run the code pays for itself immediately. Agents love to tell you they're done when they don't have the ability to prove it to you.

### Step 3 - Create specialized agents (and skills) (10 minutes)

Start a **fresh session** in the repo folder (`/clear`). We've documented what is necessary for this task already.  This is a good habit for not bloating context.

### Prompt 4 - Build the team

> Read ARCHITECTURE.md, including the Running locally section. Then create a set of specialized subagents for this codebase in .claude/agents/, one file each. At minimum create a front-end agent and a back-end agent. Add others only where this codebase genuinely has a distinct area, such as data access and migrations, tests, build and tooling, or infrastructure. For each agent write: a one-paragraph description of when to use it, the folders and layers it owns, the conventions and patterns it must follow with real examples from this repo, the exact commands it must run to verify its work, and the things it must not touch. Then create or update CLAUDE.md so it points at ARCHITECTURE.md and the agents, states the build, run, and test commands, and lists the two or three things about this repo that are most likely to trip up a new contributor. If this repo has a repetitive workflow worth packaging, such as running the test suite and interpreting failures, regenerating a client, or seeding data, also create a skill for it in .claude/skills/. Keep CLAUDE.md under 60 lines. Show me the list of files you created.  Also create any skills that may be of value to these agents or you as the orchestrator of the codebase.  Don't add things for the sake of adding them, but if things will genuinely add value, add them.  Specify higher level models (Opus) for "thinking" tasks like research and planning, lower level (sonnet) for "doing" things like actually writing code.  Ask me if you're not sure what model to specify.

Open the agents. Delete anything generic that could describe any project. What's left is the context.

### Step 4 - What did we learn? Ask your robot (5 minutes)

Before you close a session, ask what it learned. Do it now in the session from Step 3.

### Prompt 5 - What did we learn

> Have we learned anything this session worth preserving in the codebase in some way?

Make this a habit. It is the cheapest context you will ever collect.

Then clear your context. `/clear` or start a new session.

### Step 5 - Plan a larger feature (15 minutes)

Switch to a powerful model for this. `/model` and pick Opus or the strongest model your tool offers. Enter **plan mode**. Then paste, after filling in the brackets:

### Prompt 6 - Plan with me

> I am a [your role and experience level, such as "senior .NET developer who has never touched this codebase" or "junior developer, comfortable with JavaScript, new to TypeScript"]. Ask me questions at the level that fits that background. I want to add this feature: [the same feature you used in Part 1, described in two to four sentences].
>
> Before planning, read CLAUDE.md, ARCHITECTURE.md, and the agents in .claude/agents/. Ask me any questions you need answered before you can plan well, then produce a plan. The plan must name the specific files to change or create, the order of work, the tests to add or update, and how we'll verify the feature works end to end, including how I will see it running locally.
>
> When the plan is executed, you will act as the orchestrator and delegate the work to the subagents in .claude/agents/. Use Sonnet for implementation and test-writing tasks. Use Opus only for research or design decisions. Structure the plan so the work can be delegated that way.
>
> Write the finished plan to docs/plans/<feature>.md. Give me a handoff prompt when you are done to hand to a new agent with fresh context (it should point at the mardown file).  MAKE SURE the prompt is clear that the next agent is not done until it is able to validate the feature is working end to end.  If it needs additional tools or plugins to do that it should ask me to install them.

Answer its questions honestly. Push back on the plan at least once. Read the handoff prompt it wrote. If it doesn't say "you are not done until it runs locally," add that yourself.

### Step 6 - Execute the plan (20 to 30 minutes)

Clear your context. `/clear` or start a new session. Stay on the powerful model as orchestrator. Paste the handoff prompt from the top of your plan file. If you wrote your own, make sure it covers everything Prompt 7 below does.

### Prompt 7 - Execute

> Read docs/plans/[feature-name].md and execute it. You are the orchestrator. Delegate implementation to the subagents in .claude/agents/ per the plan, using Sonnet for implementation and Opus for research. You are not done until the project builds, the tests pass, the application is running locally, and you have told me exactly what to open or run so I can see the feature working with my own eyes. Show me the real build and test output, not a summary. If you get stuck on my environment, stop and ask.

This is the long wait. Watch how it delegates. Notice when a subagent's report and the orchestrator's summary disagree. When it says done, go look.  

*NOTE:  I usually ask the orchestrator to show me and/or prove it here.  For example: If you've got the playwright or browser tools available (and it's a web app), It's a good habit to ask the agent to drive the running feature end to end walk through it together one step at a time.  You'll learn a lot this way and eventually can make it a rule never to tell you its done until it proves it end to end itself.* The original prompt should have made it ask you to do this, but agents often conveniently forget to actually validate their work.

### Step 7 - Show and tell

Need some volunteers here.  As your stuff finishes - raise your hand and lets talk through your results

```
git stash show -p stash@{0} | head -100
```

Same request. What was different?

## Worksheet

| | Part 1 (wrong way) | Part 2 (right way) |
|---|---|---|
| Did it build and run? | | |
| Did I see the feature work? | | |
| Wrong assumptions it made | | |
| Questions it should have asked | | |
| What context would have prevented the miss | | |

## Tips

- Fresh session per step. The steps produce artifacts (`ARCHITECTURE.md`, agents, `CLAUDE.md`, the plan) so each session can start cold and still know everything.
- Heavy model for mapping and planning, cheaper models for doing. See [resources/model-selection.md](../../resources/model-selection.md).
- "Show me the actual output" is not optional. The most common failure is a confident claim that tests pass when they were never run.
- If it didn't ask you a question and should have, that's a context gap. Write it down.
- Reading for later: [Writing a good CLAUDE.md](https://www.humanlayer.dev/blog/writing-a-good-claude-md). Their root file is under 60 lines.

## Optional: provided fallback repositories

eShopOnWeb and hexo were copied into this folder from public open-source projects with their CI, deploy, and AI-helper files removed; licenses are preserved in each folder. LEAP was trimmed from an internal-style app rather than an open-source one, so it never had those files to strip, but the same rule applies: no `CLAUDE.md`, on purpose.

Start your agent **inside the repo folder**, not the workshop root, so `/init`, `ARCHITECTURE.md`, and `.claude/agents/` scope to the project.

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

Full setup and a click-through tour of the app: [eShopOnWeb/RUNNING.md](eShopOnWeb/RUNNING.md).

Your instructor will hand you a feature at Part 1. Use the same feature in Step 5 so you can compare the two attempts.

### hexo (Node)

A static-site generator written in TypeScript, about 22k lines, with a real open issue backlog. hexo is a library and a command-line tool, not a website, so `hexo/example-site` is a small starter blog wired to the local hexo source. Your edits to `hexo/lib` show up when you regenerate the site. Treat `hexo` as the one repo for this lab. Start your agent there.

**Prerequisite:** Node.js 22 or newer. Tested on 22, 24, and 25. Install commands per platform are in `hexo/RUNNING.md`.

```
cd projects/3-Brownfield/hexo
npm ci
npm run build
npm test
```

Five tests fail out of about 1,300. That is the upstream state of the project, not your mistake. Four are order-dependent in one file.

The example site needs the `npm run build` above to have finished first. It loads hexo from the compiled `dist` folder.

```
cd example-site
npm install
npx hexo generate
npx hexo server -p 4111
```

Open `http://localhost:4111`. Port 4000 is hexo's default but is often taken by something else on developer laptops.

Full setup and a tour: [hexo/RUNNING.md](hexo/RUNNING.md).

Your instructor will hand you a feature at Part 1. Use the same feature in Step 5 so you can compare the two attempts.

### LEAP (Compass) (.NET + React)

A trimmed copy of an internal-style employee/SOW directory app: .NET 10 minimal API, React 19 SPA, PostgreSQL 16. About 900 files — roughly twice either other fallback — and it needs Docker for Postgres.

**Prerequisite:** the .NET 10 SDK, Node 22 and npm 10 (`.npmrc` rejects npm 11 on purpose), and Docker.

```
cd projects/3-Brownfield/leap
dotnet build leap.slnx
dotnet test --project tests/unit/LeadingEDJE.Leap.Api.Tests.csproj
npm ci
npm run build -w web/compass
npm run test -w web/compass
```

Two unit tests fail out of the box, both in `CompassReportEndpointsTests` (and four more in the integration project). That's a real bug already in the app — a bad `DateOnly` query parameter is never validated, so the report answers 200 with a silently wrong date range instead of a clean 400 — and it's a good first thing to hunt down.

```
cp .env.example .env
make setup
make dev-all
```

Open `http://localhost:5176/compass/`. You're auto-signed-in (DevBypass); `SAML=1 make dev-all` runs a real SAML handshake against a mock identity provider instead, though the browser callback is a known 500 in this local setup.

Full setup and a tour of the app: [leap/RUNNING.md](leap/RUNNING.md).

Your instructor will hand you a feature at Part 1. Use the same feature in Step 5 so you can compare the two attempts.
