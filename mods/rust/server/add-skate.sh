#!/usr/bin/env bash
set -Eeuo pipefail
COMMIT=9eeddc7814a28c10cd8dfe1be8c3763144e5afdf
SHA256=bb72d7e4ccbbead77341101196dad4c996d9fb9ebd097f9fc0edd177d242784f
VERSION=0.3.8
DIR=/srv/rust/server/oxide/plugins
CHECK_ONLY=false
DEFER_RELOAD=false
if [[ $# -eq 1 && $1 == --check ]]; then
    CHECK_ONLY=true
elif [[ $# -eq 1 && $1 == --defer-reload ]]; then
    DEFER_RELOAD=true
elif [[ $# -ne 0 ]]; then
    echo "usage: add-skate.sh [--check | --defer-reload]" >&2
    exit 2
fi

if ! $CHECK_ONLY; then
    [[ $EUID -eq 0 ]] || { echo "run as root" >&2; exit 1; }
    command -v python3 >/dev/null || { echo "python3 is required to check Oxide loading" >&2; exit 1; }
    [[ -d $DIR ]] || { echo "no Oxide plugins folder at $DIR" >&2; exit 1; }
    if [[ -e $DIR/ShortcutSkate.cs || -L $DIR/ShortcutSkate.cs ]]; then
        [[ -f $DIR/ShortcutSkate.cs && ! -L $DIR/ShortcutSkate.cs ]] || {
            echo "existing ShortcutSkate.cs must be a regular file" >&2
            exit 1
        }
    fi
fi

skate_download_dir=''
skate_worker_pid=''
stop_worker() {
    if [[ -n $skate_worker_pid ]]; then
        trap '' INT TERM
        # Cover a cancellation before the child's new process group exists.
        kill -TERM -- "-$skate_worker_pid" 2>/dev/null || true
        kill -TERM "$skate_worker_pid" 2>/dev/null || true
        kill -TERM -- "-$skate_worker_pid" 2>/dev/null || true
        wait "$skate_worker_pid" 2>/dev/null || true
        skate_worker_pid=''
    fi
}
cleanup() {
    stop_worker
    if [[ -n $skate_download_dir ]]; then rm -rf -- "$skate_download_dir"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

skate_download_dir=$(mktemp -d /tmp/ShortcutSkate-download.XXXXXX)
skate_tmp=$skate_download_dir/ShortcutSkate.cs
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

# A non-job-control child becomes the tracked session leader without forking.
set +m
skate_cancel_status=0
trap 'skate_cancel_status=130' INT
trap 'skate_cancel_status=143' TERM
skate_worker_code=$(cat <<'BASH'
    set -Eeuo pipefail
    DIR=$1
    VERSION=$2
    SHA256=$3
    DEFER_RELOAD=$4
    [[ $EUID -ne 0 ]] || { echo "rust must be a nonroot account" >&2; exit 1; }
    [[ -d $DIR ]] || { echo "no Oxide plugins folder at $DIR" >&2; exit 1; }
    if [[ -e $DIR/ShortcutSkate.cs || -L $DIR/ShortcutSkate.cs ]]; then
        [[ -f $DIR/ShortcutSkate.cs && ! -L $DIR/ShortcutSkate.cs ]] || {
            echo "existing ShortcutSkate.cs must be a regular file" >&2
            exit 1
        }
    fi
    umask 077
    skate_tmp=""
    skate_backup_tmp=""
    skate_state=""
    read -r -d "" skate_verdict <<'PY' || true
import collections
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import time

action, state_path, directory, version, source_hash, staged_path = sys.argv[1:]
directory = Path(directory)
active = directory / "ShortcutSkate.cs"
backup = directory / "ShortcutSkate.cs.bak"
logs = directory.parent / "logs"

def regular(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    stream = os.fdopen(fd, "rb")
    if not stat.S_ISREG(os.fstat(fd).st_mode):
        stream.close()
        raise OSError("Expected a regular file: " + str(path))
    return stream

def identity(metadata):
    return [metadata.st_dev, metadata.st_ino]

def source(path):
    with regular(path) as stream:
        metadata = os.fstat(stream.fileno())
        data = stream.read()
        return {"identity": identity(metadata), "sha256": hashlib.sha256(data).hexdigest()}, data, metadata

def log_paths():
    if logs.is_symlink() or not logs.is_dir():
        raise OSError("No regular Oxide logs directory at " + str(logs))
    with os.scandir(logs) as entries:
        paths = sorted(Path(entry.path) for entry in entries
            if entry.name.startswith("oxide_") and entry.name.endswith(".txt"))
    if len(paths) > 4096:
        raise OSError("Too many Oxide log files to monitor safely")
    return paths

def save(state):
    with open(state_path + ".new", "w", encoding="utf-8") as stream:
        json.dump(state, stream)
    os.replace(state_path + ".new", state_path)

def cursor(stream, metadata):
    stream.seek(max(0, metadata.st_size - 64))
    suffix = stream.read(metadata.st_size - stream.tell())
    return {"offset": metadata.st_size, "suffix": suffix.hex(),
        "skip": bool(suffix and not suffix.endswith(b"\n"))}

def log_snapshot():
    cursors = {}
    for path in log_paths():
        with regular(path) as stream:
            metadata = os.fstat(stream.fileno())
            cursors[":".join(map(str, identity(metadata)))] = cursor(stream, metadata)
    return cursors

def rollback(state):
    if state.get("settled"):
        return
    if not os.path.lexists(active):
        print("Rollback refused: the active source was removed by another operation.", file=sys.stderr)
    elif source(active)[0] != state["installed"]:
        print("Rollback refused: the active source changed; no file overwritten.", file=sys.stderr)
    elif state["prior"] is None:
        active.unlink()
        print("No prior source existed; the unconfirmed new source was removed.", file=sys.stderr)
    else:
        saved, data, metadata = source(backup)
        if saved != state["prior"]:
            print("Rollback refused: the backup changed; no file overwritten.", file=sys.stderr)
        else:
            fd, temporary = tempfile.mkstemp(prefix=".ShortcutSkate-restore.", dir=directory)
            try:
                with os.fdopen(fd, "wb") as stream:
                    stream.write(data)
                    stream.flush()
                    os.fchmod(stream.fileno(), stat.S_IMODE(metadata.st_mode))
                    os.utime(stream.fileno(), ns=(metadata.st_atime_ns, metadata.st_mtime_ns))
                if source(active)[0] != state["installed"]:
                    raise OSError("Active source changed before rollback; no file overwritten")
                os.replace(temporary, active)
                print("Previous ShortcutSkate.cs restored from " + str(backup) + "; verify its reload in Oxide.", file=sys.stderr)
            finally:
                if os.path.lexists(temporary):
                    os.unlink(temporary)
    state["settled"] = True
    save(state)

try:
    if action in ("capture", "capture-deferred"):
        if action == "capture":
            log_snapshot()
        installed = source(staged_path)[0]
        if installed["sha256"] != source_hash:
            raise OSError("Staged source checksum changed")
        prior = None
        if active.exists():
            prior = source(backup)[0]
            if prior["sha256"] != source(active)[0]["sha256"]:
                raise OSError("Active source changed during backup")
        save({"logs": {}, "installed": installed, "prior": prior, "settled": False, "armed": False})
    else:
        with open(state_path, encoding="utf-8") as stream:
            state = json.load(stream)
        if action == "rollback":
            rollback(state)
        elif action == "commit":
            if source(active)[0] != state["installed"]:
                raise OSError("Installed source changed before deferring reload")
            state["settled"] = True
            save(state)
            print("ShortcutSkate v" + version + " source installed in " + str(directory) + ".", flush=True)
            print("Reload deferred; source-only/native-unverified. No compile or load verdict was observed.", flush=True)
            if state["prior"] is None:
                print("No prior source existed; no rollback backup was created.", flush=True)
            else:
                print("Previous source backup retained at " + str(backup) + ".", flush=True)
        elif action == "arm":
            if source(active)[0] != state["installed"]:
                raise OSError("Installed source changed before monitoring Oxide")
            state["logs"] = log_snapshot()
            state["armed"] = True
            save(state)
        elif action == "wait":
            if not state["armed"] or source(active)[0] != state["installed"]:
                raise OSError("Installed source changed before checking Oxide")
            print("ShortcutSkate v" + version + " source installed in " + str(directory) + ".", flush=True)
            print("Waiting up to 90 seconds for new Oxide verdicts in " + str(logs / "oxide_*.txt") +
                "; if automatic reload is disabled, run oxide.reload ShortcutSkate in the server console.", flush=True)
            record = re.compile(r"^(?:(?:\[Oxide\]\s*)?[^\[\]\r\n]*\[(?:Info|Error|Warning)\]\s*|(?:\[Oxide\]\s*)?)(.*)$")
            loaded = re.compile(r"^Loaded plugin ShortcutSkate v" + re.escape(version) + r"(?:\s|$)")
            failed = re.compile(r"^Error while compiling ShortcutSkate\b|^(?:Plugin\s+)?ShortcutSkate\b.*failed to compile|^failed to compile\s+(?:plugin\s+)?ShortcutSkate\b", re.I)
            recent = collections.deque(maxlen=12)
            partial = {}
            deadline = time.monotonic() + 90
            failure = None
            while time.monotonic() < deadline:
                success = None
                for path in log_paths():
                    if time.monotonic() >= deadline:
                        break
                    with regular(path) as stream:
                        metadata = os.fstat(stream.fileno())
                        key = ":".join(map(str, identity(metadata)))
                        if key not in state["logs"]:
                            state["logs"][key] = cursor(stream, metadata)
                            continue
                        position = state["logs"][key]
                        offset = position["offset"]
                        suffix = bytes.fromhex(position["suffix"])
                        stream.seek(max(0, offset - len(suffix)))
                        if metadata.st_size < offset or stream.read(len(suffix)) != suffix:
                            raise OSError("Oxide log was truncated or rewritten during the wait: " + str(path))
                        stream.seek(offset)
                        remaining = metadata.st_size - offset
                        if remaining > 8388608:
                            raise OSError("New Oxide log tail exceeds 8 MiB; no verdict accepted: " + str(path))
                        chunk = stream.read(remaining)
                        position["offset"] += len(chunk)
                        stream.seek(max(0, position["offset"] - 64))
                        position["suffix"] = stream.read(position["offset"] - stream.tell()).hex()
                    lines = (partial.get(key, b"") + chunk).split(b"\n")
                    partial[key] = lines.pop()
                    if len(partial[key]) > 65536:
                        raise OSError("Oxide log contains an oversized unfinished line: " + str(path))
                    for index, raw in enumerate(lines):
                        if index % 256 == 0 and time.monotonic() >= deadline:
                            break
                        if position["skip"]:
                            position["skip"] = False
                            continue
                        line = raw.decode("utf-8", "replace").rstrip("\r")
                        line = "".join(c for c in line if c == "\t" or ord(c) >= 32)
                        if re.search(r"ShortcutSkate|error CS|\|\s*Line:", line, re.I):
                            recent.append(str(path) + ": " + line)
                        parsed = record.match(line)
                        message = parsed.group(1) if parsed else ""
                        if failed.search(message):
                            failure = str(path) + ": " + line
                        if loaded.search(message):
                            success = str(path) + ": " + line
                if failure:
                    break
                if time.monotonic() >= deadline:
                    break
                if success:
                    if source(active)[0] != state["installed"]:
                        raise OSError("Active source changed before the Oxide verdict")
                    state["settled"] = True
                    save(state)
                    print(success)
                    sys.exit(0)
                time.sleep(min(0.5, max(0, deadline - time.monotonic())))
            print(failure or "No new ShortcutSkate v" + version + " load verdict within 90 seconds.", file=sys.stderr)
            print("Oxide logs: " + str(logs / "oxide_*.txt"), file=sys.stderr)
            for line in recent:
                print(line, file=sys.stderr)
            rollback(state)
            sys.exit(1)
        else:
            raise ValueError("Unknown verdict action")
except (OSError, ValueError) as error:
    print("Oxide load verification failed: " + str(error), file=sys.stderr)
    sys.exit(1)
PY
    cleanup() {
        trap "" INT TERM
        if [[ -n $skate_state && -s $skate_state ]]; then
            python3 -c "$skate_verdict" rollback "$skate_state" "$DIR" "$VERSION" "$SHA256" "" || true
        fi
        if [[ -n $skate_tmp ]]; then rm -f -- "$skate_tmp"; fi
        if [[ -n $skate_backup_tmp ]]; then rm -f -- "$skate_backup_tmp"; fi
        if [[ -n $skate_state ]]; then rm -f -- "$skate_state" "$skate_state.new"; fi
    }
    trap cleanup EXIT
    trap "exit 130" INT
    trap "exit 143" TERM
    skate_tmp=$(mktemp "$DIR/.ShortcutSkate.XXXXXX")
    cat > "$skate_tmp"
    chmod 0644 "$skate_tmp"
    if [[ -f $DIR/ShortcutSkate.cs ]]; then
        skate_backup_tmp=$(mktemp "$DIR/.ShortcutSkate-backup.XXXXXX")
        cp -p -- "$DIR/ShortcutSkate.cs" "$skate_backup_tmp"
        mv -fT -- "$skate_backup_tmp" "$DIR/ShortcutSkate.cs.bak"
    fi
    skate_state=$(mktemp "$DIR/.ShortcutSkate-verdict.XXXXXX")
    skate_capture_action=capture
    if $DEFER_RELOAD; then skate_capture_action=capture-deferred; fi
    python3 -c "$skate_verdict" "$skate_capture_action" "$skate_state" "$DIR" "$VERSION" "$SHA256" "$skate_tmp"
    mv -fT -- "$skate_tmp" "$DIR/ShortcutSkate.cs"
    if $DEFER_RELOAD; then
        python3 -c "$skate_verdict" commit "$skate_state" "$DIR" "$VERSION" "$SHA256" ""
    else
        python3 -c "$skate_verdict" arm "$skate_state" "$DIR" "$VERSION" "$SHA256" ""
        python3 -c "$skate_verdict" wait "$skate_state" "$DIR" "$VERSION" "$SHA256" ""
    fi
BASH
)
setsid --wait runuser -u rust -g rust -- /bin/bash -c "$skate_worker_code" skate-install "$DIR" "$VERSION" "$SHA256" "$DEFER_RELOAD" < "$skate_tmp" &
skate_worker_pid=$!
trap 'exit 130' INT
trap 'exit 143' TERM
if [[ $skate_cancel_status -ne 0 ]]; then exit "$skate_cancel_status"; fi
skate_worker_status=0
if wait "$skate_worker_pid"; then
    skate_worker_pid=''
else
    skate_worker_status=$? skate_worker_pid=''
fi
if [[ $skate_worker_status -ne 0 ]]; then exit "$skate_worker_status"; fi
if $DEFER_RELOAD; then
    echo "Restart or reload the server explicitly, then verify Oxide loading, patch readiness and in-game behavior."
    exit 0
fi
echo "Oxide reported loading ShortcutSkate v$VERSION; check oxide.plugins in the server console."
echo "A source install does not confirm in-game behavior."
