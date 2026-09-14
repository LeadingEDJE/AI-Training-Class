#!/usr/bin/env python3
"""Assert that every changed TypeScript file differs from a base ref in COMMENTS ONLY.

    python3 scripts/verify-comments-only.py [base-ref]        # default: origin/main
    python3 scripts/verify-comments-only.py --self-test       # prove the tokenizer

WHY THIS IS A SCRIPT AND NOT A PARAGRAPH IN THE PR.
A comment pass can only break things by accident, and the accident is silent: a
regex that eats a contiguous run of comment lines will happily eat the closing
`*/` of a docblock, or an `import` that followed it. Both happened during the
phase-4 judged pass, and BOTH PASSED eslint AND prettier -- a docblock that
swallows the line after it is still valid TypeScript. No linter can see it,
because nothing is malformed; the file simply means something else.

So the check is not "does it parse" but "is the code identical". Comments are
stripped from both sides and the remainder compared token-for-token. The
stripper is string-aware, so a `//` inside a string literal is not mistaken for
a comment.

TEST-STRATEGY.md "Before deleting a test" step 2: print the count and assert it
is non-zero BEFORE the comparison. A loop over zero changed files otherwise
reports every deletion as safe -- this repo has lost eight gates to that shape.

Self-test: delete a line of real code from any changed file and re-run. It must
FAIL. A guard that has never failed has never run. Delete the WHOLE file and
re-run too -- that is the case this script got wrong until 2026-08-28, because a
deleted file cannot be opened and the exception was swallowed as a skip.
"""

import subprocess
import sys


def strip_comments(src: str) -> str:
    """Remove block and line comments, preserving string, template and REGEX literals.

    Regex literals have to be tracked, not just strings. `/[\'"]/g` contains a quote, and
    treating that quote as opening a string desyncs everything after it -- the next real
    `//` comment is then never stripped, and a legitimate comment-only edit false-FAILs.
    A desync can also run a later `/*` past its true end and remove real code, which is the
    fail-OPEN direction, so this is not merely noisy.

    Telling a regex from a division needs the previous significant token, because `/` is
    ambiguous in JavaScript. The standard heuristic: a regex may start where a VALUE may
    start -- after an operator, an opening bracket, a comma, a semicolon, or one of the
    keywords below -- and a division may not. `a / b` is division; `= /re/` is a regex.
    """
    # A `/` after one of these starts a regex, not a division.
    value_position_chars = set('(,=:[!&|?{};+-*%^~<>')
    value_position_words = {
        'return', 'typeof', 'instanceof', 'in', 'of', 'new', 'delete',
        'void', 'case', 'do', 'else', 'yield', 'await',
    }

    out: list[str] = []
    i, n = 0, len(src)
    prev_char = ''   # last significant character emitted
    prev_word = ''   # last identifier emitted, for the keyword cases

    def regex_may_start() -> bool:
        if not prev_char:
            return True
        if prev_char in value_position_chars:
            return True
        return prev_word in value_position_words

    while i < n:
        c = src[i]

        if c in '"\'`':
            quote = c
            out.append(c)
            i += 1
            while i < n:
                if src[i] == '\\':
                    out.append(src[i : i + 2])
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == quote:
                    i += 1
                    break
                i += 1
            prev_char, prev_word = quote, ''
            continue

        if src.startswith('/*', i):
            end = src.find('*/', i + 2)
            i = n if end < 0 else end + 2
            continue

        if src.startswith('//', i):
            end = src.find('\n', i)
            i = n if end < 0 else end
            continue

        # A regex literal. Copied verbatim like a string, so its contents can never be
        # mistaken for a comment or a quote. `/` inside a [...] character class is literal.
        if c == '/' and regex_may_start():
            j, in_class = i + 1, False
            closed = False
            while j < n:
                ch = src[j]
                if ch == '\\':
                    j += 2
                    continue
                if ch == '\n':
                    break          # unterminated: not a regex after all
                if ch == '[':
                    in_class = True
                elif ch == ']':
                    in_class = False
                elif ch == '/' and not in_class:
                    closed = True
                    j += 1
                    break
                j += 1
            if closed:
                while j < n and src[j].isalpha():   # flags
                    j += 1
                out.append(src[i:j])
                i = j
                prev_char, prev_word = '/', ''
                continue

        out.append(c)
        i += 1
        if not c.isspace():
            prev_char = c
            prev_word = prev_word + c if (c.isalnum() or c == '_') else ''

    text = ''.join(out)
    return '\n'.join(line.rstrip() for line in text.split('\n') if line.strip())


def self_test() -> int:
    """Prove the tokenizer on the cases that actually break naive strippers.

    Run with `--self-test`. The stripper is the whole trust basis of this tool: if it
    mis-parses, the comparison is meaningless in one direction or the other. Every case
    below is a real shape from this codebase or a documented review finding.
    """
    cases = [
        # (name, source, text that MUST be stripped, text that MUST survive)
        ("regex containing a quote, then a comment",
         "const q = /['\"]/g;\n// gone\nconst K = 1;", "// gone", "/['\"]/g"),
        ("division is not a regex",
         "const r = a / b; // gone\nconst s = c / d;", "// gone", "a / b"),
        ("a URL inside a string is not a comment",
         "const u = 'http://x/y'; // gone", "// gone", "http://x/y"),
        ("regex containing //",
         "const p = /a\\/\\/b/; // gone", "// gone", "/a\\/\\/b/"),
        ("regex containing a backtick",
         "const t = /[`]/; // gone\nconst z = 2;", "// gone", "/[`]/"),
        ("/ inside a character class does not end the regex",
         "const c = /[/]/g; // gone\nconst y = 3;", "// gone", "/[/]/g"),
        ("block comment removed",
         "const a = 1; /* gone */ const b = 2;", "gone", "const b = 2"),
        ("template literal holding both quote kinds",
         "const t = `it's ${x} \"q\"`; // gone", "// gone", "it's ${x}"),
        ("apostrophe inside a comment does not open a string",
         "const a = 1;\n// don't break\nconst b = 2;", "don't break", "const b = 2"),
        ("regex in value position after `return`",
         "function f(){ return /['\"]/.test(s); } // gone", "// gone", "/['\"]/"),
    ]

    failures = 0
    for name, src, must_go, must_stay in cases:
        out = strip_comments(src)
        removed, kept = must_go not in out, must_stay in out
        if removed and kept:
            print(f'  ok    {name}')
            continue
        failures += 1
        print(f'  FAIL  {name}')
        print(f'        stripped {must_go!r}? {removed}   kept {must_stay!r}? {kept}')
        print(f'        got: {out!r}')

    print(f'\n{len(cases) - failures}/{len(cases)} tokenizer cases pass')
    return 1 if failures else 0


def main() -> int:
    if '--self-test' in sys.argv[1:]:
        return self_test()

    ref = sys.argv[1] if len(sys.argv) > 1 else 'origin/main'

    # Compare against the MERGE BASE, not the ref's tip. A long-lived branch falls behind
    # main, and diffing against the moving tip reports every file main changed since the
    # fork as though this branch had touched it -- inverted, so unrelated code edits look
    # like this branch broke them. Fails loud rather than silent, but still wrong.
    base = subprocess.run(
        ['git', 'merge-base', 'HEAD', ref], capture_output=True, text=True, check=True
    ).stdout.strip()  # ASCII sha; encoding= not needed here
    if base != ref:
        print(f'Base: merge-base of HEAD and {ref} = {base[:9]}')

    # --name-status, not --name-only. The status letter is the whole point: a DELETED file
    # cannot be read from the working tree, and the previous version caught the resulting
    # FileNotFoundError and `continue`d -- so deleting an entire tracked file was reported as
    # comment-only, and counted toward the PASS total it was never compared for. That is the
    # same fail-open shape the docstring above warns about, one level down: not a loop over
    # zero files, but a loop that silently drops the files it cannot handle.
    raw = subprocess.run(
        ['git', 'diff', '--name-status', base, '--', '*.ts', '*.tsx'],
        capture_output=True,
        text=True,
        encoding='utf-8',
        check=True,
    ).stdout.splitlines()

    # A rename is `R<score>\told\tnew`; everything else is `<letter>\tpath`. Keep BOTH paths:
    # the new one to read from the working tree, the OLD one to read from the base.
    #
    # Taking only the last field -- which this did -- asks the base for a path that exists
    # only at HEAD. `git show base:<new-path>` then fails, its stdout is '', and a
    # byte-identical rename is compared against EMPTY and reported as CODE CHANGED.
    entries = []
    for line in raw:
        fields = line.split('\t')
        if len(fields) < 2:
            continue
        status = fields[0][0]
        if status == 'R' and len(fields) >= 3:
            entries.append((status, fields[2], fields[1]))  # (status, worktree path, base path)
        else:
            entries.append((status, fields[-1], fields[-1]))

    print(f'Comparing {len(entries)} changed TypeScript file(s)...')
    if not entries:
        print('ERROR: no changed TypeScript files -- this check measured nothing.')
        return 1

    changed_code = []
    for status, path, base_path in entries:
        # Deleting a file removes its code, so it is never "a comment change" -- no matter
        # that the removed lines happened to be comments.
        if status == 'D':
            changed_code.append((path, 'FILE DELETED'))
            continue

        # An ADDED file has no base content; `git show` fails and yields ''. Comparing
        # against empty is right: a new file carrying code IS a code change.
        # encoding='utf-8' is NOT decoration. `text=True` alone decodes with the LOCALE
        # codec, which on a Windows checkout is cp1252 -- and these files contain em dashes.
        # The decode then raises inside subprocess's reader THREAD, which does not propagate:
        # `.stdout` comes back None and the crash surfaces far away as
        # `TypeError: object of type 'NoneType' has no len()` inside strip_comments. CI never
        # saw it because Linux runners are UTF-8, so this guard could not be run at all on the
        # platform its own docstring tells developers to run it on.
        shown = subprocess.run(
            ['git', 'show', f'{base}:{base_path}'],
            capture_output=True,
            text=True,
            encoding='utf-8',
        )
        # Only an ADDED file legitimately has no base content. For anything else a failing
        # `git show` means this check could not read what it is comparing against, and
        # swallowing that turns "I could not look" into "it changed". Say which, and where.
        if shown.returncode != 0 and status != 'A':
            changed_code.append((path, f'BASE UNREADABLE at {base_path}'))
            continue
        old = shown.stdout
        try:
            with open(path, encoding='utf-8') as handle:
                new = handle.read()
        except FileNotFoundError:
            # git says this path exists at HEAD but the working tree disagrees. Never
            # skip it: an unreadable file is an unverified file.
            changed_code.append((path, f'UNREADABLE (git status {status})'))
            continue

        if strip_comments(old) != strip_comments(new):
            changed_code.append((path, 'CODE CHANGED'))

    for path, reason in changed_code:
        print(f'  {reason}: {path}')

    ok = len(entries) - len(changed_code)
    if changed_code:
        print(f'\nFAIL: {len(changed_code)} of {len(entries)} file(s) changed more than comments.')
        return 1

    print(f'\nPASS: {ok}/{len(entries)} changed file(s) differ in comments only.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
