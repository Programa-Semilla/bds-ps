SPEX_ROOT=~/.claude/plugins/cache/cc-rhuss-marketplace/spex/4.0.0

mkdir -p "$SPEX_ROOT/commands"
for skill_dir in "$SPEX_ROOT/spex/skills"/*/; do
    skill_name=$(basename "$skill_dir")
    cp "$skill_dir/SKILL.md" "$SPEX_ROOT/commands/${skill_name}.md"
done
