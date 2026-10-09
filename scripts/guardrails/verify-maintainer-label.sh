#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited.
#
# Verify that a governance override label on the current PR was applied by the
# maintainer named in .agents/OWNERS (ADR-0031). The label's presence alone is
# not consent: anyone with triage rights can add a label.
#
# Usage: verify-maintainer-label.sh LABEL
# Env:   GH_TOKEN, GITHUB_REPOSITORY, PR_NUMBER
# Exit 0 only when the most recent "labeled" event for LABEL was by the maintainer.
set -euo pipefail

label="${1:?label name required}"
: "${GITHUB_REPOSITORY:?}" "${PR_NUMBER:?}"

maintainer=$(grep -oE 'GitHub `@[A-Za-z0-9-]+`' .agents/OWNERS | head -n1 | sed -E 's/.*@([A-Za-z0-9-]+).*/\1/')
if [[ -z "$maintainer" ]]; then
  echo "::error title=No maintainer::Could not read the maintainer's GitHub handle from .agents/OWNERS."
  exit 1
fi

actor=$(gh api --paginate "repos/${GITHUB_REPOSITORY}/issues/${PR_NUMBER}/events" \
  --jq ".[] | select(.event == \"labeled\" and .label.name == \"${label}\") | .actor.login" | tail -n1)

if [[ -n "$actor" && "${actor,,}" == "${maintainer,,}" ]]; then
  echo "'${label}' applied by maintainer @${actor}."
  exit 0
fi

echo "::error title=Unverified override::'${label}' was last applied by '${actor:-unknown}', not the maintainer @${maintainer} (.agents/OWNERS). Only the maintainer may apply it."
exit 1
