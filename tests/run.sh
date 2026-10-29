#!/usr/bin/env bash
# Runs the scripted tests: each tests/<name>.txt is replayed at a fixed 60 Hz with the environment and flags
# its opening comment lines name (ZC_...=..., --menu, --new, --nosave), and a screenshot of the last frame goes to
# shots/tests/<name>.png. Usage: tests/run.sh [name ...]   (no names: every test)
# A test passes when the game runs it to the end without an exception; the screenshots are for looking at.
set -u
cd "$(dirname "$0")/.."
exe=src/ZombieCafe/bin/Debug/net8.0/ZombieCafePlus.exe
mkdir -p shots/tests
names=("$@")
if [ ${#names[@]} -eq 0 ]; then
    for f in tests/*.txt; do names+=("$(basename "$f" .txt)"); done
fi
failed=0
for name in "${names[@]}"; do
    script="tests/$name.txt"
    header=$(awk '/^#/ {print; next} {exit}' "$script")
    env_vars=$(grep -o 'ZC_[A-Z_]*=[^ )]*' <<<"$header" | tr '\n' ' ')
    flags=$(grep -o -- '--[a-z]*' <<<"$header" | tr '\n' ' ')
    last=$(grep -v '^#' "$script" | awk 'NF {print $1}' | sort -n | tail -1)
    frames=$(( ${last:-0} + ${TEST_TAIL:-120} ))
    out=$(env ZC_MUTE=1 ZC_SEED=3 $env_vars ZC_INPUT="$script" timeout 300 "$exe" $flags --shot "shots/tests/$name.png" --frames "$frames" 2>&1)
    code=$?
    if [ $code -ne 0 ] || grep -q "Unhandled exception\|   at " <<<"$out"; then
        echo "FAIL $name (exit $code)"
        grep -m5 "exception\|   at \|\[input\]" <<<"$out"
        failed=$((failed + 1))
    elif grep -q "\[input\]" <<<"$out"; then
        echo "WARN $name: $(grep -m1 '\[input\]' <<<"$out")"
    else
        echo "ok   $name"
    fi
done
echo "$failed failed"
exit $failed
