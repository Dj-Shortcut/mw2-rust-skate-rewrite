#!/usr/bin/env bash
set -Eeuo pipefail
COMMIT=54a54a3b199fbe5d7d337b40a739b6546195c631
SHA256=de2e6976f6fc9ab2e3099dfe3cf8c076f2a82ff6c026f75bae0c8eda67c2f8ff
VERSION=0.3.4
DIR=/srv/rust/server/oxide/plugins
CHECK_ONLY=false
if [[ $# -eq 1 && $1 == --check ]]; then
    CHECK_ONLY=true
elif [[ $# -ne 0 ]]; then
    echo "usage: add-skate.sh [--check]" >&2
    exit 2
fi

if ! $CHECK_ONLY; then
    [[ $EUID -eq 0 ]] || { echo "run as root" >&2; exit 1; }
    [[ -d $DIR ]] || { echo "no Oxide plugins folder at $DIR" >&2; exit 1; }
    if [[ -e $DIR/ShortcutSkate.cs || -L $DIR/ShortcutSkate.cs ]]; then
        [[ -f $DIR/ShortcutSkate.cs && ! -L $DIR/ShortcutSkate.cs ]] || {
            echo "existing ShortcutSkate.cs must be a regular file" >&2
            exit 1
        }
    fi
fi

skate_tmp=''
skate_backup_tmp=''
cleanup() {
    if [[ -n $skate_tmp ]]; then rm -f -- "$skate_tmp"; fi
    if [[ -n $skate_backup_tmp ]]; then rm -f -- "$skate_backup_tmp"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if $CHECK_ONLY; then
    skate_tmp=$(mktemp)
else
    skate_tmp=$(mktemp "$DIR/.ShortcutSkate.XXXXXX")
fi
curl -fsSL --connect-timeout 15 --max-time 60 \
    "https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite/$COMMIT/mods/rust/plugins/ShortcutSkate.cs" \
    -o "$skate_tmp"
printf '%s  %s\n' "$SHA256" "$skate_tmp" | sha256sum -c - >/dev/null || {
    echo "the downloaded plugin does not match its checksum; nothing installed" >&2
    exit 1
}

if $CHECK_ONLY; then
    echo "ShortcutSkate v$VERSION source verified: $SHA256; nothing installed."
    exit 0
fi

chown rust:rust "$skate_tmp"
chmod 0644 "$skate_tmp"
if [[ -f $DIR/ShortcutSkate.cs ]]; then
    skate_backup_tmp=$(mktemp "$DIR/.ShortcutSkate-backup.XXXXXX")
    cp -p -- "$DIR/ShortcutSkate.cs" "$skate_backup_tmp"
    mv -fT -- "$skate_backup_tmp" "$DIR/ShortcutSkate.cs.bak"
fi
mv -fT -- "$skate_tmp" "$DIR/ShortcutSkate.cs"
echo "ShortcutSkate v$VERSION source installed in $DIR."
echo "Confirm compilation and loading in the server console with: oxide.plugins"
echo "A source install does not confirm in-game behavior."
