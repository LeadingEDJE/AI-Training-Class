#!/usr/bin/env python3
"""Report every Helm values file that turns the Phase 51 migration hook OFF.

Prints one path per line; prints nothing when the tree is clean. Consumed by
scripts/check-migrate-hook.sh.

WHY THIS IS A SEPARATE FILE AND NOT A grep
    `migrations.enabled` is a KEY, and it must be resolved as one. The first draft of the calling
    script grepped each values file for any `enabled: false` and then for the word `migrations:`,
    which flagged the chart's own values.yaml on the strength of an unrelated
    `dataProtectionKeys.enabled: false` seventeen lines away. A gate that is red on a correct tree
    gets suppressed, and a suppressed gate is strictly worse than no gate.

WHY IT MATTERS AT ALL
    `migrations.enabled: false` means NOTHING MIGRATES and the application runs against whatever
    schema happens to already exist -- exactly the behaviour Phase 51 exists to remove. The switch is
    a one-off emergency `--set` for a first-ever install into a brand-new environment whose
    credentials Secret does not exist yet. Committing it to a values file would silently disable the
    whole guarantee while every deploy stayed green.

An unparseable values file is REPORTED, not skipped: a file this gate cannot read is a file whose
setting it cannot vouch for, and failing closed is the correct direction here.
"""

import glob
import sys

import yaml


def main() -> int:
    offenders = []
    for path in sorted(glob.glob("deploy/helm/**/values*.yaml", recursive=True)):
        try:
            with open(path, encoding="utf-8") as handle:
                data = yaml.safe_load(handle) or {}
        except (OSError, yaml.YAMLError) as exc:
            offenders.append(f"{path} (unreadable: {exc})")
            continue

        if not isinstance(data, dict):
            continue

        migrations = data.get("migrations")
        if isinstance(migrations, dict) and migrations.get("enabled") is False:
            offenders.append(path)

    for offender in offenders:
        print(offender)
    return 0


if __name__ == "__main__":
    sys.exit(main())
