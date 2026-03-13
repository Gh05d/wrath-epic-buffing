#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

# 1. Read version from csproj
VERSION=$(grep -oP '<Version>\K[^<]+' BuffIt2TheLimit/BuffIt2TheLimit.csproj)
TAG="v${VERSION}"
ZIP="BuffIt2TheLimit/bin/BuffIt2TheLimit-${VERSION}.zip"

echo "Preparing release: Buff It 2 The Limit ${TAG}"

# 2. Check if tag already exists
if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "ERROR: Tag ${TAG} already exists. Bump the version first."
    exit 1
fi

# 3. Check for uncommitted changes
if ! git diff --quiet || ! git diff --cached --quiet; then
    echo "ERROR: Working tree is dirty. Commit or stash changes first."
    exit 1
fi

# 4. Push current branch to fork
echo "Pushing to fork..."
git push fork master

# 5. Build Release configuration
echo "Building Release..."
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj \
    -c Release \
    -p:SolutionDir="$(pwd)/" \
    --nologo

# 6. Verify zip exists
if [ ! -f "$ZIP" ]; then
    echo "ERROR: Expected zip not found at ${ZIP}"
    exit 1
fi

echo "Release artifact: ${ZIP} ($(du -h "$ZIP" | cut -f1))"

# 7. Create and push tag
git tag -a "$TAG" -m "Release ${TAG}"
git push fork "$TAG"

# 8. Create GitHub release
echo "Creating GitHub release..."
gh release create "$TAG" "$ZIP" \
    --repo Gh05d/wrath-epic-buffing \
    --title "Buff It 2 The Limit ${TAG}" \
    --notes "$(cat <<NOTES
## Buff It 2 The Limit ${TAG}

### Installation
1. Download \`BuffIt2TheLimit-${VERSION}.zip\`
2. Extract into \`{GameDir}/Mods/BuffIt2TheLimit/\`
3. Enable in Unity Mod Manager

### Requirements
- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 0.23.0+
- Pathfinder: Wrath of the Righteous 1.4+
NOTES
)"

# 9. Update Repository.json with DownloadUrl for UMM auto-download
DOWNLOAD_URL="https://github.com/Gh05d/wrath-epic-buffing/releases/download/${TAG}/BuffIt2TheLimit-${VERSION}.zip"
cat > Repository.json <<REPO
{
    "Releases": [
        {
            "Id": "BuffIt2TheLimit",
            "Version": "${VERSION}",
            "DownloadUrl": "${DOWNLOAD_URL}"
        }
    ]
}
REPO

git add Repository.json
git commit -m "chore: update Repository.json for ${TAG}"
git push fork master

echo ""
echo "Release ${TAG} published!"
echo "  GitHub: https://github.com/Gh05d/wrath-epic-buffing/releases/tag/${TAG}"
echo "  Repository.json updated with DownloadUrl"
echo ""
echo "Next step: Upload ${ZIP} to Nexus Mods"
