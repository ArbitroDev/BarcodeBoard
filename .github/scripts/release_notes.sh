#!/usr/bin/env bash
# Writes the release notes of a version, in Markdown, from the Conventional
# Commits since the previous version tag (or since the start when there is
# none), grouped by type.
#
# Usage: release_notes.sh [<version>] [<revision>]
#   version:  the version being released (1.2.3); empty for a preview of the
#             changes not released yet.
#   revision: HEAD by default.
set -euo pipefail

version="${1:-}"
head="${2:-HEAD}"
repo_url="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-ArbitroDev/BarcodeBoard}"

# The previous version: the latest tag before this revision (the tag of the
# version itself, when it already points to the revision, is left out).
previous=$(git describe --tags --abbrev=0 --match 'v*' "${head}^" 2>/dev/null || true)
range="${previous:+${previous}..}${head}"

declare -A sections=()
breaking=""
pattern='^([a-z]+)(\(([a-z0-9._/-]+)\))?(!)?: (.+)$'

while IFS=$'\t' read -r sha subject body_flag; do
  [[ "$subject" =~ $pattern ]] || continue
  type="${BASH_REMATCH[1]}"
  scope="${BASH_REMATCH[3]}"
  bang="${BASH_REMATCH[4]}"
  description="${BASH_REMATCH[5]}"

  line="- ${scope:+**${scope}:** }${description} ([${sha:0:7}](${repo_url}/commit/${sha}))"
  sections[$type]+="${line}"$'\n'
  if [[ -n "$bang" || "$body_flag" == breaking ]]; then
    breaking+="${line}"$'\n'
  fi
done < <(git log --no-merges --reverse \
  --format='%H%x09%s%x09%(trailers:key=BREAKING CHANGE,valueonly,separator=)' "$range" |
  awk -F'\t' '{ print $1 "\t" $2 "\t" ($3 != "" ? "breaking" : "") }')

if [[ -n "$version" ]]; then
  echo "## Barcode Board ${version}"
else
  echo "## Changes not released yet${previous:+ (since ${previous})}"
fi
echo

if [[ -n "$breaking" ]]; then
  echo "### Breaking changes"
  echo
  printf '%s\n' "$breaking"
fi

titles=(
  "feat:New features"
  "fix:Bug fixes"
  "perf:Performance"
  "refactor:Refactoring"
  "docs:Documentation"
  "test:Tests"
  "build:Build"
  "ci:Continuous integration"
  "style:Style"
  "chore:Maintenance"
  "revert:Reverts"
)
for entry in "${titles[@]}"; do
  type="${entry%%:*}"
  [[ -n "${sections[$type]:-}" ]] || continue
  echo "### ${entry#*:}"
  echo
  printf '%s\n' "${sections[$type]}"
done

if [[ -n "$previous" && -n "$version" ]]; then
  echo "**Full changes:** ${repo_url}/compare/${previous}...v${version}"
fi
