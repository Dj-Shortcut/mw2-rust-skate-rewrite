#!/usr/bin/env python3
import grp
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import select
import shutil
import signal
import stat
import subprocess
import sys
import threading
import time


INSTALLER_COMMIT = "41034058afa73c9d6456bf06f4dc94e792eb558d"
INSTALLER_SHA256 = "06604633ab5c65232613b893558e90033ddbbae7e3b712ee648bb04971c7fd41"
SOURCE_COMMIT = "6ba9e3810beb37bb0abc2c83bcfdcadbd9806511"
SOURCE_SHA256 = "01ad2489016da05b60a99bcd18c24a6f8826c33e7eec7e198d37400f0385094d"
PRIOR_COMMIT = "5a9ed6e550a405365c8a18746dfd276a310b6848"
PRIOR_SHA256 = "078508fbdb67683f92d4943480250c7987bcbe051e60565713df06e41cbb465e"
PUBLIC_ROOT = "https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite"
ROOT = Path("/srv/rust")
PLUGINS = ROOT / "server/oxide/plugins"
ACTIVE = PLUGINS / "ShortcutSkate.cs"
BACKUP = PLUGINS / "ShortcutSkate.cs.bak"
LOGS = ROOT / "server/oxide/logs"
LOG = LOGS / "oxide_fixture.txt"
MAX_DOWNLOAD_BYTES = 1024 * 1024
COMMAND_PATH = "/usr/sbin:/usr/bin:/sbin:/bin"
SERVICE_CONTROLLER = r"""
import hashlib
import json
from pathlib import Path
import stat
import sys

directory = Path(__file__).resolve().parent
expected = json.loads((directory / "restart_expectation.json").read_text())
with (directory / "restart_attempts.jsonl").open("a") as stream:
    stream.write(json.dumps(sys.argv[1:]) + "\n")
if sys.argv[1:] != ["restart", "rust-server"]:
    sys.exit("synthetic systemctl refused unexpected arguments")

def snapshot(path):
    if not path.is_file() or path.is_symlink():
        sys.exit("synthetic restart requires a regular source file")
    data = path.read_bytes()
    metadata = path.stat()
    return [data.hex(), metadata.st_uid, metadata.st_gid,
        stat.S_IMODE(metadata.st_mode), metadata.st_mtime_ns]

active = Path(expected["active"])
backup = Path(expected["backup"])
installed = snapshot(active)
if hashlib.sha256(bytes.fromhex(installed[0])).hexdigest() != expected["sha256"]:
    sys.exit("synthetic restart ran before the selected source was installed")
if installed[1:4] != expected["identity"] + [0o644]:
    sys.exit("synthetic restart ran before source ownership and mode were set")
if expected["prior"] is None:
    if backup.exists() or backup.is_symlink():
        sys.exit("synthetic fresh restart found an unexpected backup")
elif snapshot(backup) != expected["prior"]:
    sys.exit("synthetic restart ran without the exact prior backup")
with (directory / "restart_calls.jsonl").open("a") as stream:
    stream.write(json.dumps(sys.argv[1:]) + "\n")
if expected["exit_status"]:
    print("synthetic rust-server restart failed", file=sys.stderr)
    sys.exit(expected["exit_status"])
print("synthetic rust-server restart requested")
"""
LEASE_HOLDER = r"""
import fcntl
import os
import select
import signal
import stat
import sys
import time

descriptor = None
notified = False

def notify(signum, frame):
    global notified
    if not notified:
        notified = True
        os.write(1, ("lease-break " + str(time.monotonic()) + "\n").encode("ascii"))

try:
    signal.signal(signal.SIGIO, notify)
    descriptor = os.open(sys.argv[1], os.O_RDWR | os.O_NOFOLLOW | os.O_CLOEXEC)
    metadata = os.fstat(descriptor)
    if not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != os.geteuid():
        raise RuntimeError("lease target is not an owned regular file")
    fcntl.fcntl(descriptor, fcntl.F_SETOWN, os.getpid())
    fcntl.fcntl(descriptor, fcntl.F_SETLEASE, fcntl.F_WRLCK)
    os.write(1, b"lease-ready\n")
    deadline = time.monotonic() + 150
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise RuntimeError("lease holder handshake timed out")
        readable, _, _ = select.select([0], [], [], remaining)
        if not readable:
            raise RuntimeError("lease holder handshake timed out")
        message = os.read(0, 128)
        if message != b"release\n":
            raise RuntimeError("lease holder received no release handshake")
        fcntl.fcntl(descriptor, fcntl.F_SETLEASE, fcntl.F_UNLCK)
        os.close(descriptor)
        descriptor = None
        os.write(1, b"lease-released\n")
        break
except Exception as error:
    os.write(1, ("lease-error: " + str(error) + "\n").encode("utf-8", "replace"))
    sys.exit(1)
finally:
    if descriptor is not None:
        os.close(descriptor)
"""


class ScenarioFailure(Exception):
    pass


def require(condition, message):
    if not condition:
        raise ScenarioFailure(message)


def lookup_user(name):
    try:
        return pwd.getpwnam(name)
    except KeyError:
        return None


def lookup_group(name):
    try:
        return grp.getgrnam(name)
    except KeyError:
        return None


def mount_path(value):
    for escaped, literal in (("\\040", " "), ("\\011", "\t"), ("\\012", "\n"), ("\\134", "\\")):
        value = value.replace(escaped, literal)
    return Path(value)


def require_private_target():
    for line in Path("/proc/self/mountinfo").read_text().splitlines():
        fields = line.split()
        require(len(fields) >= 6, "unreadable Linux mount inventory")
        mounted = mount_path(fields[4])
        if mounted != Path("/") and (mounted == ROOT or mounted in ROOT.parents or ROOT in mounted.parents):
            raise ScenarioFailure("refusing a mounted target path: " + str(mounted))


def require_environment():
    require(sys.platform == "linux", "this scenario requires Linux")
    require(os.geteuid() == 0, "this scenario requires container root")
    marker = Path("/.dockerenv")
    require(marker.is_file() and not marker.is_symlink(), "this scenario requires /.dockerenv")
    require(not os.path.lexists(ROOT), "refusing an existing /srv/rust")
    require(lookup_user("rust") is None, "refusing an existing rust user")
    require(lookup_group("rust") is None, "refusing an existing rust group")
    require(Path("/srv").is_dir() and not Path("/srv").is_symlink(), "requires a regular /srv directory")
    require_private_target()
    require(hasattr(os, "pidfd_open") and hasattr(signal, "pidfd_send_signal"), "requires Linux pidfd process cleanup")
    for name in ("sh", "bash", "curl", "sha256sum", "mktemp", "cat", "chmod", "cp", "mv", "rm", "runuser", "setsid", "useradd", "userdel", "groupdel", "python3"):
        require(shutil.which(name, path=COMMAND_PATH) is not None, "missing prerequisite: " + name)
    nobody = lookup_user("nobody")
    require(nobody is not None and nobody.pw_uid != 0, "requires a nonroot nobody identity")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def snapshot(path):
    data = path.read_bytes()
    metadata = path.stat()
    return data, metadata.st_uid, metadata.st_gid, stat.S_IMODE(metadata.st_mode), metadata.st_mtime_ns


def root_download_snapshot():
    entries = {}
    for path in (entry for prefix in ("ShortcutSkate-download.", "ShortcutSkate-update.") for entry in Path("/tmp").glob(prefix + "*")):
        metadata = path.lstat()
        entries[path.name] = (
            metadata.st_dev, metadata.st_ino, metadata.st_mode, metadata.st_uid,
            metadata.st_gid, metadata.st_size, metadata.st_mtime_ns, metadata.st_ctime_ns,
            str(path.readlink()) if path.is_symlink() else None,
        )
    return entries


def process_inventory():
    records = {}
    directories = [path for path in Path("/proc").iterdir() if path.name.isdecimal()]
    require(len(directories) <= 8192, "process inventory exceeds the scenario limit")
    for path in directories:
        try:
            raw = (path / "stat").read_text()
            head, tail = raw.rsplit(")", 1)
            fields = tail.split()
            uid_lines = [line for line in (path / "status").read_text().splitlines() if line.startswith("Uid:")]
            if not uid_lines:
                continue
            records[int(path.name)] = {
                "start": int(fields[19]), "parent": int(fields[1]),
                "group": int(fields[2]), "session": int(fields[3]),
                "state": fields[0], "name": head.split("(", 1)[1],
                "uids": tuple(int(value) for value in uid_lines[0].split()[1:]),
            }
        except (FileNotFoundError, ProcessLookupError):
            continue
    return records


def lease_event(holder, pending, timeout):
    deadline = time.monotonic() + timeout
    while b"\n" not in pending:
        remaining = deadline - time.monotonic()
        require(remaining > 0, "kernel lease milestone timed out")
        readable, _, _ = select.select([holder.stdout], [], [], remaining)
        require(readable, "kernel lease milestone timed out")
        chunk = os.read(holder.stdout.fileno(), 512)
        require(chunk, "lease holder exited before its milestone")
        pending.extend(chunk)
        require(len(pending) <= 4096, "lease holder output exceeds the scenario limit")
    line, _, rest = pending.partition(b"\n")
    pending[:] = rest
    return line.decode("utf-8", "replace")


def signal_owned_process(pid, start, signum):
    try:
        descriptor = os.pidfd_open(pid, 0)
    except ProcessLookupError:
        return
    try:
        fields = Path("/proc", str(pid), "stat").read_text().rsplit(")", 1)[1].split()
        if int(fields[19]) == start:
            signal.pidfd_send_signal(descriptor, signum, None, 0)
    except (FileNotFoundError, ProcessLookupError):
        pass
    finally:
        os.close(descriptor)


class Scenario:
    def __init__(self, installer, guide):
        self.installer = installer
        self.installer_bytes = installer.read_bytes()
        require(digest(self.installer_bytes) == INSTALLER_SHA256, "checkout installer is not the exact reviewed source")
        self.guide = guide
        self.guide_bytes = guide.read_bytes()
        entries = [block for block in re.findall(r"```sh\n(.*?)\n```", self.guide_bytes.decode("utf-8"), re.DOTALL) if "skate_update_dir" in block]
        require(len(entries) == 1, "requires one documented skate_update_dir shell entrypoint")
        self.entrypoint = entries[0]
        require(INSTALLER_COMMIT in self.entrypoint and INSTALLER_SHA256 in self.entrypoint, "documented entrypoint does not pin the reviewed installer")
        require(re.search(r'bash\s+"\$skate_update_dir/add-skate\.sh"\s+--defer-reload\s*(?:&&|;)\s*systemctl\s+restart\s+rust-server\s*\)?\s*$', self.entrypoint), "documented entrypoint must defer reload before restarting rust-server")
        self.root_identity = None
        self.user_identity = None
        self.group_identity = None
        self.root_download_baseline = root_download_snapshot()
        self.total = 0
        self.group_checks = 0
        self.work = ROOT / ".skate-install-scenario"
        self.delivered = self.work / "add-skate.sh"
        self.prior = self.work / "prior.cs"
        self.environment = os.environ.copy()
        self.environment.update(PATH=COMMAND_PATH, LC_ALL="C", CURL_HOME=str(self.work), TMPDIR=str(self.work / "tmp"))
        self.environment.pop("BASH_ENV", None)
        self.environment.pop("ENV", None)

    def check(self, condition, message):
        self.group_checks += 1
        self.total += 1
        require(condition, message)

    def group(self, name, action):
        self.group_checks = 0
        action()
        print(name + ": PASS " + str(self.group_checks) + " checks", flush=True)

    def run(self, arguments, timeout=90, input_bytes=None, nonroot=False, environment=None):
        options = {}
        if nonroot:
            nobody = lookup_user("nobody")
            options.update(user=nobody.pw_uid, group=nobody.pw_gid, extra_groups=[])
        process = subprocess.Popen(
            arguments, stdin=subprocess.PIPE if input_bytes is not None else subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd="/", env=self.environment if environment is None else environment,
            start_new_session=True, **options,
        )
        try:
            stdout, stderr = process.communicate(input_bytes, timeout=timeout)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                process.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.communicate(timeout=5)
            raise
        return subprocess.CompletedProcess(arguments, process.returncode, stdout.decode("utf-8", "replace"), stderr.decode("utf-8", "replace"))

    def command(self, arguments):
        result = self.run(arguments, timeout=15)
        require(result.returncode == 0, "prerequisite command failed: " + result.stderr.strip())
        return result

    def download(self, url, destination):
        result = self.run(["curl", "-fsSL", "--connect-timeout", "15", "--max-time", "60", "--max-filesize", str(MAX_DOWNLOAD_BYTES), url, "-o", str(destination)], timeout=70)
        require(result.returncode == 0, "public source download failed: " + result.stderr.strip())
        require(destination.stat().st_size <= MAX_DOWNLOAD_BYTES, "public source exceeds size limit")
        return destination.read_bytes()

    def install(self, arguments=(), delivered=False, timeout=170, environment=None):
        return self.run(["bash", str(self.delivered if delivered else self.installer), *arguments], timeout=timeout, environment=environment)

    def clean_plugins(self):
        for path in PLUGINS.iterdir():
            if path.is_symlink() or not path.is_dir():
                path.unlink()
            else:
                shutil.rmtree(path)
        for path in LOGS.iterdir():
            path.unlink()
        LOG.touch()
        if self.user_identity:
            os.chown(LOG, *self.user_identity)
        LOG.chmod(0o640)

    def log(self, data, path=LOG):
        with path.open("ab") as stream:
            stream.write(data)
        os.chown(path, *self.user_identity)
        path.chmod(0o640)

    def with_verdict(self, invoke, emit=None):
        previous = ACTIVE.stat().st_ino if ACTIVE.exists() else None
        finished = threading.Event()
        errors = []

        def writer():
            deadline = time.monotonic() + 80
            try:
                while not finished.is_set() and time.monotonic() < deadline:
                    states = list(PLUGINS.glob(".ShortcutSkate-verdict.*"))
                    armed = False
                    for path in states:
                        if path.name.endswith(".new"):
                            continue
                        try:
                            armed = armed or json.loads(path.read_text()).get("armed", False)
                        except (OSError, ValueError):
                            pass
                    if armed and ACTIVE.exists() and ACTIVE.stat().st_ino != previous and digest(ACTIVE.read_bytes()) == SOURCE_SHA256:
                        if emit is None:
                            self.log(b"04:20 [Info] Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")
                        else:
                            emit(finished)
                        return
                    time.sleep(0.05)
                raise ScenarioFailure("installer did not publish source before the synthetic verdict")
            except BaseException as error:
                errors.append(error)

        worker = threading.Thread(target=writer, daemon=True)
        worker.start()
        try:
            result = invoke()
        finally:
            finished.set()
            worker.join(timeout=5)
        require(not worker.is_alive(), "owned verdict fixture thread survived")
        require(not errors,
            f"synthetic verdict fixture failed: {errors}; "
            f"installer exit status: {result.returncode}; stderr tail: {result.stderr[-2048:]!r}")
        return result

    def no_staging(self):
        self.check(not any(path.name.startswith(".ShortcutSkate") for path in PLUGINS.iterdir()), "hidden installer staging remains")
        self.check(not any((self.work / "tmp").iterdir()), "private root download staging remains")
        self.check(root_download_snapshot() == self.root_download_baseline, "root download staging leaked or changed preexisting entries")

    def seed_prior(self):
        self.clean_plugins()
        ACTIVE.write_bytes(self.prior.read_bytes())
        os.chown(ACTIVE, *(self.user_identity or (0, 0)))
        ACTIVE.chmod(0o640)
        os.utime(ACTIVE, ns=(1660000000123456789, 1660000000123456789))
        return snapshot(ACTIVE)

    def delivery(self):
        delivered = self.download(PUBLIC_ROOT + "/" + INSTALLER_COMMIT + "/mods/rust/server/add-skate.sh", self.delivered)
        self.check(digest(delivered) == INSTALLER_SHA256, "delivered installer checksum mismatch")
        self.check(delivered == self.installer_bytes, "delivered installer differs from checkout")
        prior = self.download(PUBLIC_ROOT + "/" + PRIOR_COMMIT + "/mods/rust/plugins/ShortcutSkate.cs", self.prior)
        self.check(digest(prior) == PRIOR_SHA256, "original 0.2.0 source checksum mismatch")
        print("installer: " + INSTALLER_COMMIT + " sha256=" + digest(delivered), flush=True)
        print("source: " + SOURCE_COMMIT + " sha256=" + SOURCE_SHA256, flush=True)
        print("prior: " + PRIOR_COMMIT + " sha256=" + digest(prior), flush=True)
        print("documented entrypoint: sha256=" + digest(self.entrypoint.encode("utf-8")), flush=True)

    def missing_owner(self, arguments=()):
        before = self.seed_prior()
        result = self.install(arguments)
        self.check(result.returncode == 1, "missing rust identity did not fail installation")
        self.check("runuser:" in result.stderr and "rust" in result.stderr, "missing owner did not reach real runuser failure")
        self.check(snapshot(ACTIVE) == before, "missing owner changed the active source or metadata")
        self.check(not os.path.lexists(BACKUP), "missing owner created a backup")
        self.no_staging()

    def guards(self, arguments=()):
        before = self.seed_prior()
        result = self.install(("--unknown",))
        self.check(result.returncode == 2 and "usage:" in result.stderr, "unknown argument guard failed")
        self.check(snapshot(ACTIVE) == before, "unknown argument changed active source")
        result = self.run(["bash", "-s", "--", *arguments], input_bytes=self.installer_bytes, nonroot=True)
        self.check(result.returncode == 1 and "run as root" in result.stderr, "nonroot guard failed")
        self.check(snapshot(ACTIVE) == before, "nonroot call changed active source")
        self.no_staging()

        moved = self.work / "missing-plugins"
        PLUGINS.rename(moved)
        try:
            result = self.install(arguments)
            self.check(result.returncode == 1 and "no Oxide plugins folder" in result.stderr, "missing-folder guard failed")
            self.check(not os.path.lexists(PLUGINS), "missing-folder guard recreated the folder")
            self.check(snapshot(moved / ACTIVE.name) == before, "missing-folder guard changed active source")
        finally:
            moved.rename(PLUGINS)

        ACTIVE.unlink()
        ACTIVE.mkdir()
        result = self.install(arguments)
        self.check(result.returncode == 1 and "must be a regular file" in result.stderr, "directory-source guard failed")
        self.check(ACTIVE.is_dir() and not any(ACTIVE.iterdir()), "directory-source guard changed its target")
        self.no_staging()
        ACTIVE.rmdir()
        ACTIVE.symlink_to(self.prior)
        prior_before = snapshot(self.prior)
        result = self.install(arguments)
        self.check(result.returncode == 1 and "must be a regular file" in result.stderr, "symlink-source guard failed")
        self.check(ACTIVE.is_symlink() and ACTIVE.readlink() == self.prior, "symlink-source guard changed the link")
        self.check(snapshot(self.prior) == prior_before, "symlink-source guard changed the linked source")
        self.no_staging()
        ACTIVE.unlink()
        ACTIVE.symlink_to(self.work / "absent.cs")
        result = self.install(arguments)
        self.check(result.returncode == 1 and "must be a regular file" in result.stderr, "dangling-symlink guard failed")
        self.check(ACTIVE.is_symlink() and not ACTIVE.exists(), "dangling-symlink guard changed the link")
        self.no_staging()

    def create_identity(self):
        try:
            self.command(["useradd", "--system", "--user-group", "--no-create-home", "--shell", "/usr/sbin/nologin", "rust"])
        finally:
            user = lookup_user("rust")
            group = lookup_group("rust")
            if user is not None:
                self.user_identity = (user.pw_uid, user.pw_gid)
            if group is not None:
                self.group_identity = group.gr_gid
        require(self.user_identity is not None and self.group_identity == self.user_identity[1] and self.user_identity[0] != 0, "rust identity creation failed")
        os.chown(PLUGINS, *self.user_identity)
        PLUGINS.chmod(0o755)

    def protected_active_refusal(self, arguments=()):
        self.seed_prior()
        os.chown(ACTIVE, 0, 0)
        ACTIVE.chmod(0o600)
        before = snapshot(ACTIVE)
        result = self.install(arguments)
        self.check(result.returncode == 1, "worker overwrote a root-only active source")
        self.check("cp:" in result.stderr and "Permission denied" in result.stderr and "ShortcutSkate.cs" in result.stderr, "root-only source did not reach real worker copy refusal")
        self.check(snapshot(ACTIVE) == before, "worker changed root-only source bytes or metadata")
        self.check(not os.path.lexists(BACKUP), "root-only source refusal published a backup")
        self.no_staging()

    def active_installed(self):
        self.check(ACTIVE.is_file() and not ACTIVE.is_symlink(), "installed source is not a regular file")
        data, uid, gid, mode, _ = snapshot(ACTIVE)
        self.check(digest(data) == SOURCE_SHA256, "installed source checksum mismatch")
        self.check((uid, gid) == self.user_identity, "installed source ownership is not rust:rust")
        self.check(mode == 0o644, "installed source mode is not 0644")
        self.no_staging()

    def fresh(self):
        self.clean_plugins()
        result = self.with_verdict(lambda: self.install(delivered=True))
        self.check(result.returncode == 0, "delivered installer failed fresh installation: " + result.stderr.strip())
        self.check("ShortcutSkate v0.3.11 source installed" in result.stdout, "fresh installation lacked source-install acknowledgement")
        self.check("oxide_fixture.txt: 04:20 [Info] Loaded plugin ShortcutSkate v0.3.11" in result.stdout, "fresh install did not print the observed load verdict and path")
        self.active_installed()
        self.check(not os.path.lexists(BACKUP), "fresh installation unexpectedly created a backup")

    def replacement(self):
        before = self.seed_prior()
        result = self.with_verdict(self.install)
        self.check(result.returncode == 0, "previous-source replacement failed: " + result.stderr.strip())
        self.active_installed()
        self.check(BACKUP.is_file() and not BACKUP.is_symlink(), "previous source was not backed up as a regular file")
        self.check(snapshot(BACKUP) == before, "backup did not retain exact prior bytes, owner, group, mode and mtime")

    def deferred_acknowledgement(self, result):
        self.check("ShortcutSkate v0.3.11 source installed" in result.stdout, "deferred installation lacked source-install acknowledgement")
        self.check("Reload deferred; source-only/native-unverified. No compile or load verdict was observed." in result.stdout, "deferred installation did not state its source-only scope")
        self.check("Restart or reload the server explicitly" in result.stdout, "deferred installation omitted the explicit reload instruction")
        self.check("Waiting up to 90 seconds" not in result.stdout, "deferred installation entered the native-verdict wait")
        self.check("Oxide reported loading" not in result.stdout and "Loaded plugin ShortcutSkate" not in result.stdout, "deferred installation claimed an Oxide load verdict")
        self.check("Previous ShortcutSkate.cs restored" not in result.stderr and "unconfirmed new source was removed" not in result.stderr, "settled deferred installation triggered rollback")

    def deferred_install(self, fresh=False, missing_logs=False):
        before = None if fresh else self.seed_prior()
        if fresh:
            self.clean_plugins()
        self.log(b"Error while compiling ShortcutSkate: deferred historical fixture failure\n")
        log_before = snapshot(LOG)
        moved = self.work / "deferred-logs"
        if missing_logs:
            LOGS.rename(moved)
        try:
            result = self.install(("--defer-reload",), delivered=fresh, timeout=75)
            self.check(result.returncode == 0, "deferred source installation failed: " + result.stderr.strip())
            self.deferred_acknowledgement(result)
            self.active_installed()
            if fresh:
                self.check(not os.path.lexists(BACKUP), "deferred fresh installation unexpectedly created a backup")
                self.check("No prior source existed; no rollback backup was created." in result.stdout, "deferred fresh installation misstated its backup")
            else:
                self.check(snapshot(BACKUP) == before, "deferred installation did not retain exact previous source bytes and metadata")
                self.check("Previous source backup retained at " + str(BACKUP) in result.stdout, "deferred installation did not identify its retained backup")
            if missing_logs:
                self.check(not os.path.lexists(LOGS), "deferred installation created or required an Oxide logs directory")
                self.check(snapshot(moved / LOG.name) == log_before, "deferred installation changed the displaced log")
            else:
                self.check(snapshot(LOG) == log_before, "deferred installation changed the existing log")
                self.log(b"Error while compiling ShortcutSkate: later deferred fixture failure\n")
                self.check(digest(ACTIVE.read_bytes()) == SOURCE_SHA256, "later synthetic failure rolled back settled deferred source")
                if before is not None:
                    self.check(snapshot(BACKUP) == before, "later synthetic failure changed the retained previous source")
            self.check(not any(self.user_identity[0] in record["uids"] and record["state"] not in ("Z", "X") for record in process_inventory().values()), "deferred installer left a worker running after settlement")
        finally:
            if missing_logs:
                moved.rename(LOGS)

    def incompatible_options(self):
        before = self.seed_prior()
        for arguments in (("--check", "--defer-reload"), ("--defer-reload", "--check"), ("--check", "--check"), ("--defer-reload", "--defer-reload")):
            result = self.install(arguments)
            self.check(result.returncode == 2 and "usage:" in result.stderr, "incompatible installer options were accepted: " + str(arguments))
            self.check(snapshot(ACTIVE) == before, "incompatible options changed active source or metadata")
            self.check(not os.path.lexists(BACKUP), "incompatible options published a backup")
            self.no_staging()

    def check_only(self):
        before = self.seed_prior()
        result = self.install(("--check",), timeout=75)
        self.check(result.returncode == 0, "check-only source verification failed: " + result.stderr.strip())
        self.check("ShortcutSkate v0.3.11 source verified: " + SOURCE_SHA256 + "; nothing installed." in result.stdout, "check-only acknowledgement did not name the exact verified source")
        self.check(snapshot(ACTIVE) == before, "check-only changed active source or metadata")
        self.check(not os.path.lexists(BACKUP), "check-only published a backup")
        self.check("source installed" not in result.stdout and "Oxide reported loading" not in result.stdout, "check-only claimed installation or loading")
        self.no_staging()

    def deferred_download_failure(self):
        before = self.seed_prior()
        failed_environment = self.environment.copy()
        missing_ca = self.work / "absent-ca.pem"
        require(not os.path.lexists(missing_ca), "TLS failure fixture must not exist")
        failed_environment["CURL_CA_BUNDLE"] = str(missing_ca)
        result = self.install(("--defer-reload",), timeout=75, environment=failed_environment)
        self.check(result.returncode != 0 and "curl:" in result.stderr and str(missing_ca) in result.stderr, "deferred source download did not fail on the owned nonexistent CA bundle")
        self.check(snapshot(ACTIVE) == before, "deferred download failure changed active source or metadata")
        self.check(not os.path.lexists(BACKUP), "deferred download failure created a backup")
        self.check("source installed" not in result.stdout and "Reload deferred" not in result.stdout, "deferred download failure claimed settlement")
        self.no_staging()

    def restart_environment(self, prior, exit_status=0):
        directory = self.work / "service-controller"
        directory.mkdir(mode=0o700, exist_ok=True)
        expectation = {
            "active": str(ACTIVE), "backup": str(BACKUP), "sha256": SOURCE_SHA256,
            "identity": list(self.user_identity), "exit_status": exit_status,
            "prior": None if prior is None else [prior[0].hex(), *prior[1:]],
        }
        (directory / "restart_expectation.json").write_text(json.dumps(expectation))
        for name in ("restart_attempts.jsonl", "restart_calls.jsonl"):
            (directory / name).unlink(missing_ok=True)
        controller = directory / "systemctl"
        controller.write_text("#!" + sys.executable + "\n" + SERVICE_CONTROLLER)
        controller.chmod(0o700)
        environment = self.environment.copy()
        environment["PATH"] = str(directory) + ":" + COMMAND_PATH
        return environment

    def restart_records(self, name):
        path = self.work / "service-controller" / name
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def documented_entrypoint(self):
        self.clean_plugins()
        scratch = self.work / "tmp"
        self.check(not any(scratch.iterdir()), "documented entrypoint scratch was not empty")
        environment = self.restart_environment(None)
        result = self.run(["sh", "-c", self.entrypoint], timeout=150, environment=environment)
        self.check(result.returncode == 0, "documented entrypoint failed: " + result.stderr.strip())
        self.check("ShortcutSkate v0.3.11 source installed" in result.stdout, "documented entrypoint did not execute the installer")
        self.deferred_acknowledgement(result)
        self.check(self.restart_records("restart_attempts.jsonl") == [["restart", "rust-server"]], "documented entrypoint did not invoke exactly the synthetic rust-server restart")
        self.check(self.restart_records("restart_calls.jsonl") == [["restart", "rust-server"]], "documented restart ran before selected source ownership, mode or backup were ready")
        self.check("synthetic rust-server restart requested" in result.stdout, "documented restart stand-in did not acknowledge its synthetic request")
        self.active_installed()
        self.check(not os.path.lexists(BACKUP), "documented fresh installation unexpectedly created a backup")
        self.check(not any(scratch.iterdir()), "documented entrypoint retained private download staging")

        before = snapshot(ACTIVE)
        failed_environment = self.restart_environment(None)
        missing_ca = self.work / "absent-ca.pem"
        require(not os.path.lexists(missing_ca), "TLS failure fixture must not exist")
        failed_environment["CURL_CA_BUNDLE"] = str(missing_ca)
        result = self.run(["sh", "-c", self.entrypoint], timeout=75, environment=failed_environment)
        self.check(result.returncode != 0, "documented entrypoint ignored outer curl failure")
        self.check("curl:" in result.stderr and str(missing_ca) in result.stderr, "outer download did not fail on the owned nonexistent CA bundle")
        self.check(snapshot(ACTIVE) == before, "outer download failure changed active source or metadata")
        self.check(not os.path.lexists(BACKUP), "outer download failure executed the installer and created a backup")
        self.check(not any(scratch.iterdir()), "outer download failure retained private staging")
        self.check(not self.restart_records("restart_attempts.jsonl"), "outer download failure attempted a service restart")
        self.no_staging()

    def documented_replacement(self, restart_failure=False):
        before = self.seed_prior()
        environment = self.restart_environment(before, 23 if restart_failure else 0)
        result = self.run(["sh", "-c", self.entrypoint], timeout=150, environment=environment)
        self.check(result.returncode == (23 if restart_failure else 0), "documented entrypoint lost the synthetic restart exit status")
        self.deferred_acknowledgement(result)
        self.check(self.restart_records("restart_attempts.jsonl") == [["restart", "rust-server"]], "documented replacement did not invoke exactly one synthetic restart")
        self.check(self.restart_records("restart_calls.jsonl") == [["restart", "rust-server"]], "documented replacement restarted before exact previous-source backup and selected metadata were ready")
        self.active_installed()
        self.check(snapshot(BACKUP) == before, "documented replacement or restart result changed the exact prior backup")
        if restart_failure:
            self.check("synthetic rust-server restart failed" in result.stderr, "synthetic restart failure was not visible")
            self.check("synthetic rust-server restart requested" not in result.stdout, "failed synthetic restart claimed success")
        else:
            self.check("synthetic rust-server restart requested" in result.stdout, "successful synthetic restart was not visible")
        self.check(not any((self.work / "tmp").iterdir()), "documented replacement retained private staging")

    def documented_install_failure(self):
        before = self.seed_prior()
        BACKUP.mkdir()
        sentinel = BACKUP / "retained"
        sentinel.write_bytes(b"backup directory remains\n")
        environment = self.restart_environment(before)
        result = self.run(["sh", "-c", self.entrypoint], timeout=150, environment=environment)
        self.check(result.returncode == 1 and "mv:" in result.stderr, "documented backup failure did not preserve the real installer refusal")
        self.check(snapshot(ACTIVE) == before, "documented backup failure changed active source or metadata")
        self.check(BACKUP.is_dir() and sentinel.read_bytes() == b"backup directory remains\n", "documented backup failure changed the existing backup directory")
        self.check(not self.restart_records("restart_attempts.jsonl"), "documented failed source installation attempted a service restart")
        self.check("source installed" not in result.stdout and "Oxide reported loading" not in result.stdout, "documented installation failure claimed success")
        self.no_staging()

    def backup_failure(self, arguments=()):
        before = self.seed_prior()
        BACKUP.mkdir()
        sentinel = BACKUP / "retained"
        sentinel.write_bytes(b"backup directory remains\n")
        result = self.install(arguments)
        self.check(result.returncode == 1, "backup-directory replacement did not fail")
        self.check("mv:" in result.stderr and "ShortcutSkate.cs.bak" in result.stderr, "backup failure did not reach real GNU mv")
        self.check(snapshot(ACTIVE) == before, "backup failure changed active source or metadata")
        self.check(BACKUP.is_dir() and sentinel.read_bytes() == b"backup directory remains\n", "backup failure changed existing backup directory")
        self.no_staging()

    def compile_failure(self, fresh=False):
        before = None if fresh else self.seed_prior()
        if fresh:
            self.clean_plugins()
        result = self.with_verdict(self.install, lambda _: self.log(
            b"[Oxide] Error while compiling ShortcutSkate: fixture error CS0001 | Line: 17\n"))
        self.check(result.returncode == 1, "compile failure did not fail the installer")
        self.check("oxide_fixture.txt" in result.stderr and "fixture error CS0001 | Line: 17" in result.stderr, "compiler verdict/path was not printed")
        self.check("Oxide reported loading" not in result.stdout, "compile failure claimed successful loading")
        if fresh:
            self.check(not os.path.lexists(ACTIVE) and not os.path.lexists(BACKUP), "failed fresh source or backup survived")
            self.check("No prior source existed" in result.stderr, "fresh rollback outcome was not stated")
        else:
            self.check(snapshot(ACTIVE) == before and snapshot(BACKUP) == before, "compile failure did not restore exact previous bytes and metadata")
            self.check("Previous ShortcutSkate.cs restored" in result.stderr, "backup restoration was not stated")
        self.no_staging()

    def stale_and_partial(self):
        self.seed_prior()
        self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\nError while compiling ShortcutSkate: old failure\nLoaded plugin ShortcutSkate v")

        def emit(finished):
            self.log(b"0.3.11 by Dj-Shortcut\nLoaded plugin ShortcutSkate v0.3.10 by Dj-Shortcut\nLoaded plugin ShortcutSkate v0.3.110 by Dj-Shortcut\nLoaded plugin ShortcutSkate v0.3.11")
            time.sleep(1.5)
            self.check(not finished.is_set(), "installer accepted stale, old partial or wrong-version verdict")
            self.log(b" by Dj-Shortcut\n")

        result = self.with_verdict(self.install, emit)
        self.check(result.returncode == 0 and "Oxide reported loading ShortcutSkate v0.3.11" in result.stdout, "complete new selected-version verdict was not accepted")
        self.active_installed()

    def rotation(self):
        self.seed_prior()
        self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")

        def emit(finished):
            LOG.rename(LOGS / "oxide_rotated.txt")
            self.log(b"other plugin compiled\n")
            time.sleep(1)
            self.check(not finished.is_set(), "rotated stale load verdict was accepted")
            self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")

        result = self.with_verdict(self.install, emit)
        self.check(result.returncode == 0, "new log after rotation did not supply the load verdict")
        self.active_installed()

    def copied_log_and_other_plugin(self):
        self.seed_prior()

        def emit(finished):
            self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n", LOGS / "oxide_copied.txt")
            self.log(b"ShortcutSkateHelper failed to compile: unrelated fixture\n")
            self.log(b"04:20 [Info] [OtherPlugin] diagnostic: Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")
            time.sleep(1.5)
            self.check(not finished.is_set(), "copied historical log or unrelated plugin supplied a verdict")
            self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")

        result = self.with_verdict(self.install, emit)
        self.check(result.returncode == 0, "correct verdict was not accepted after ignoring other records")
        self.active_installed()

    def long_tail_failure(self):
        before = self.seed_prior()
        tail = b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n" + (b"unrelated log line\n" * 65000) + b"Error while compiling ShortcutSkate: final fixture failure\n"
        self.check(len(tail) > 1048576, "long-tail fixture did not cross the previous first-chunk boundary")
        result = self.with_verdict(self.install, lambda _: self.log(tail))
        self.check(result.returncode == 1 and "final fixture failure" in result.stderr, "success before a later already-appended failure was accepted")
        self.check(snapshot(ACTIVE) == before, "later compiler failure did not restore the prior source")
        self.no_staging()

    def changed_files(self, change_backup):
        self.seed_prior()
        changed = BACKUP if change_backup else ACTIVE
        sentinel = b"another updater owns these bytes\n"

        def emit(_):
            changed.write_bytes(sentinel)
            self.log(b"ShortcutSkate failed to compile: fixture failure\n")

        result = self.with_verdict(self.install, emit)
        self.check(result.returncode == 1 and "Rollback refused" in result.stderr, "changed source or backup was not protected")
        self.check(changed.read_bytes() == sentinel, "rollback overwrote another updater's changed file")
        self.check("Previous ShortcutSkate.cs restored" not in result.stderr, "refused rollback claimed restoration")
        self.no_staging()

    def log_truncation(self):
        before = self.seed_prior()
        self.log(b"old log prefix that is longer than the replacement\n")
        result = self.with_verdict(self.install, lambda _: LOG.write_bytes(b"new\n"))
        self.check(result.returncode == 1 and "truncated or rewritten" in result.stderr, "log truncation was accepted as fresh evidence")
        self.check(snapshot(ACTIVE) == before, "unreliable log did not restore the previous source")
        self.no_staging()

    def verdict_timeout(self):
        before = self.seed_prior()
        self.log(b"Loaded plugin ShortcutSkate v0.3.11 by Dj-Shortcut\n")
        started = time.monotonic()
        result = self.run(["bash", str(self.installer)], timeout=170)
        self.check(result.returncode == 1 and "within 90 seconds" in result.stderr, "missing fresh verdict did not time out")
        self.check(90 <= time.monotonic() - started < 165, "load-verdict wait was not bounded around 90 seconds plus download")
        self.check(snapshot(ACTIVE) == before, "timeout did not restore the prior source")
        self.check("Previous ShortcutSkate.cs restored" in result.stderr, "timeout did not report restoration")
        self.no_staging()

    def interrupted_verdict(self, signum, expected_status):
        before = self.seed_prior()
        caller = subprocess.Popen(["bash", str(self.installer)], stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd="/", env=self.environment, start_new_session=True)
        owned = set()
        try:
            deadline = time.monotonic() + 75
            while time.monotonic() < deadline:
                self.collect_owned_processes(caller, None, owned)
                if ACTIVE.exists() and digest(ACTIVE.read_bytes()) == SOURCE_SHA256:
                    break
                require(caller.poll() is None, "installer exited before cancellation in the verdict wait")
                time.sleep(0.05)
            require(ACTIVE.exists() and digest(ACTIVE.read_bytes()) == SOURCE_SHA256, "source was not installed before verdict-wait cancellation")
            caller.send_signal(signum)
            stdout, stderr = caller.communicate(timeout=10)
            self.check(caller.returncode == expected_status, "verdict-wait cancellation returned the wrong status")
            self.check(snapshot(ACTIVE) == before, "cancellation after publication did not restore prior source")
            self.check(b"Previous ShortcutSkate.cs restored" in stderr and b"Oxide reported loading" not in stdout, "cancellation did not report guarded restoration")
            inventory = self.collect_owned_processes(caller, None, owned)
            self.check(not self.live_owned_processes(inventory, owned), "caller returned before owned verdict worker exited")
            self.no_staging()
        finally:
            self.collect_owned_processes(caller, None, owned)
            if caller.poll() is None:
                caller.send_signal(signal.SIGTERM)
            self.stop_owned_processes(owned)
            caller.communicate(timeout=5)

    def collect_owned_processes(self, caller, holder, owned):
        inventory = process_inventory()
        descendants = {pid for pid, start in owned if pid in inventory and inventory[pid]["start"] == start}
        for process in (caller, holder):
            if process is not None and process.returncode is None and process.pid in inventory:
                descendants.add(process.pid)
        while True:
            additional = {pid for pid, record in inventory.items() if record["parent"] in descendants}
            if additional <= descendants:
                break
            descendants.update(additional)
        for pid, record in inventory.items():
            if pid in descendants or self.user_identity[0] in record["uids"]:
                owned.add((pid, record["start"]))
        return inventory

    def live_owned_processes(self, inventory, owned, excluded=()):
        return [
            pid for pid, start in owned
            if pid not in excluded and pid in inventory and inventory[pid]["start"] == start
            and inventory[pid]["state"] not in ("Z", "X")
        ]

    def stop_owned_processes(self, owned):
        for signum, timeout in ((signal.SIGTERM, 3), (signal.SIGKILL, 3)):
            inventory = process_inventory()
            live = self.live_owned_processes(inventory, owned)
            for pid in live:
                signal_owned_process(pid, inventory[pid]["start"], signum)
            deadline = time.monotonic() + timeout
            while live and time.monotonic() < deadline:
                time.sleep(0.05)
                live = self.live_owned_processes(process_inventory(), owned)
            if not live:
                return
        require(not live, "owned interruption processes survived cleanup")

    def interrupted_replacement(self, signum, expected_status):
        before = self.seed_prior()
        lease_break_time = int(Path("/proc/sys/fs/lease-break-time").read_text().strip())
        require(lease_break_time >= 20, "kernel lease break interval is too short for the interruption scenario")
        initial_inventory = process_inventory()
        self.check(not any(self.user_identity[0] in record["uids"] and record["state"] not in ("Z", "X") for record in initial_inventory.values()), "owned rust identity already has live processes")
        holder = None
        caller = None
        holder_release_started = False
        owned = set()
        pending = bytearray()
        try:
            holder = subprocess.Popen(
                [sys.executable, "-u", "-c", LEASE_HOLDER, str(ACTIVE)],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                cwd="/", env=self.environment, start_new_session=True, bufsize=0,
                user=self.user_identity[0], group=self.user_identity[1], extra_groups=[],
            )
            self.collect_owned_processes(caller, holder, owned)
            milestone = lease_event(holder, pending, 10)
            self.check(milestone == "lease-ready", "required real write lease could not be acquired: " + milestone)
            caller = subprocess.Popen(
                ["bash", str(self.installer)], stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd="/", env=self.environment,
                start_new_session=True,
            )
            self.collect_owned_processes(caller, holder, owned)
            milestone = lease_event(holder, pending, 75)
            self.check(milestone.startswith("lease-break "), "installer did not reach the real lease-blocked backup: " + milestone)
            break_started = float(milestone.split(" ", 1)[1])
            inventory = self.collect_owned_processes(caller, holder, owned)
            blocked_copies = [
                pid for pid in self.live_owned_processes(inventory, owned, (holder.pid, caller.pid))
                if inventory[pid]["name"] == "cp" and self.user_identity[0] in inventory[pid]["uids"]
            ]
            self.check(len(blocked_copies) == 1, "kernel notification did not identify one real rust backup copy")
            self.check(caller.poll() is None and holder.poll() is None, "installer or lease holder exited before cancellation")
            self.check(0 <= time.monotonic() - break_started < lease_break_time - 13, "lease notification left too little time for a valid interruption check")
            caller.send_signal(signum)
            status = caller.wait(timeout=8)
            inventory = self.collect_owned_processes(caller, holder, owned)
            alive_at_return = self.live_owned_processes(inventory, owned, (holder.pid,))
            held_at_return = holder.poll() is None
            # Opening ACTIVE here would itself break the lease and invalidate the milestone.
            holder_release_started = True
            released_stdout, released_stderr = holder.communicate(b"release\n", timeout=5)
            self.check(status == expected_status, "cancelled installer returned " + str(status) + " instead of " + str(expected_status))
            self.check(held_at_return, "installer did not return while the lease remained held")
            self.check(time.monotonic() - break_started < lease_break_time, "kernel lease break timeout invalidated the cancellation check")
            self.check(holder.returncode == 0 and b"lease-released\n" in released_stdout and not released_stderr, "lease holder did not release cleanly")
            self.check(not alive_at_return, "installer returned while owned worker processes were still alive")
            stdout, stderr = caller.communicate(timeout=5)
            self.check(b"source installed" not in stdout, "cancelled installer reported a successful source install")
            inventory = self.collect_owned_processes(caller, holder, owned)
            self.check(not self.live_owned_processes(inventory, owned), "owned worker survived lease release")
            self.check(not any(self.user_identity[0] in record["uids"] and record["state"] not in ("Z", "X") for record in inventory.values()), "a rust process survived the interrupted operation")
            self.check(snapshot(ACTIVE) == before, "interruption or lease release changed active source bytes or metadata")
            self.check(not os.path.lexists(BACKUP), "interrupted backup was published")
            self.no_staging()
        finally:
            self.collect_owned_processes(caller, holder, owned)
            if caller is not None and caller.poll() is None:
                caller.send_signal(signal.SIGTERM)
            if holder is not None and holder.poll() is None:
                try:
                    if holder_release_started:
                        holder.communicate(timeout=3)
                    else:
                        holder_release_started = True
                        holder.communicate(b"release\n", timeout=3)
                except (BrokenPipeError, subprocess.TimeoutExpired):
                    pass
            self.collect_owned_processes(caller, holder, owned)
            self.stop_owned_processes(owned)
            for process in (caller, holder):
                if process is not None:
                    process.communicate(timeout=3)

    def unchanged_inputs(self):
        self.check(self.installer.read_bytes() == self.installer_bytes, "checkout installer changed")
        self.check(self.delivered.read_bytes() == self.installer_bytes, "delivered installer changed")
        self.check(digest(self.prior.read_bytes()) == PRIOR_SHA256, "original prior source changed")
        self.check(self.guide.read_bytes() == self.guide_bytes, "Linux guide changed")

    def cleanup(self):
        errors = []
        if self.user_identity is not None:
            try:
                user = lookup_user("rust")
                require(user is not None and (user.pw_uid, user.pw_gid) == self.user_identity, "rust identity changed; refusing identity cleanup")
                self.command(["userdel", "rust"])
            except Exception as error:
                errors.append(str(error))
        if self.group_identity is not None:
            try:
                group = lookup_group("rust")
                if group is not None:
                    require(group.gr_gid == self.group_identity, "rust group changed; refusing group cleanup")
                    self.command(["groupdel", "rust"])
            except Exception as error:
                errors.append(str(error))
        if self.root_identity is not None:
            try:
                current = ROOT.lstat()
                require(stat.S_ISDIR(current.st_mode) and (current.st_dev, current.st_ino) == self.root_identity, "owned target identity changed; refusing tree cleanup")
                require_private_target()
                shutil.rmtree(ROOT)
            except Exception as error:
                errors.append(str(error))
        require(not errors, "cleanup failed: " + "; ".join(errors))
        require(not os.path.lexists(ROOT), "owned target survived cleanup")
        require(lookup_user("rust") is None and lookup_group("rust") is None, "owned rust identity survived cleanup")
        require(root_download_snapshot() == self.root_download_baseline, "root download staging changed after cleanup")
        print("cleanup: PASS owned tree and identity removed", flush=True)

    def execute(self):
        ROOT.mkdir(mode=0o755)
        created = ROOT.stat()
        self.root_identity = (created.st_dev, created.st_ino)
        try:
            self.work.mkdir(mode=0o700)
            (self.work / "tmp").mkdir(mode=0o700)
            PLUGINS.mkdir(parents=True)
            LOGS.mkdir()
            self.group("reviewed_delivery", self.delivery)
            self.group("missing_owner", self.missing_owner)
            self.group("deferred_missing_owner", lambda: self.missing_owner(("--defer-reload",)))
            self.group("admission_guards", self.guards)
            self.group("deferred_admission_guards", lambda: self.guards(("--defer-reload",)))
            self.create_identity()
            self.group("incompatible_options", self.incompatible_options)
            self.group("check_only", self.check_only)
            self.group("protected_active_refusal", self.protected_active_refusal)
            self.group("deferred_protected_active_refusal", lambda: self.protected_active_refusal(("--defer-reload",)))
            self.group("fresh_install", self.fresh)
            self.group("prior_source_replacement", self.replacement)
            self.group("deferred_fresh_without_logs", lambda: self.deferred_install(fresh=True, missing_logs=True))
            self.group("deferred_prior_source_replacement", self.deferred_install)
            self.group("deferred_replacement_without_logs", lambda: self.deferred_install(missing_logs=True))
            self.group("deferred_download_failure", self.deferred_download_failure)
            self.group("backup_rename_failure", self.backup_failure)
            self.group("deferred_backup_rename_failure", lambda: self.backup_failure(("--defer-reload",)))
            self.group("compile_failure_restore", self.compile_failure)
            self.group("fresh_compile_failure", lambda: self.compile_failure(fresh=True))
            self.group("stale_partial_wrong_version", self.stale_and_partial)
            self.group("log_rotation", self.rotation)
            self.group("copied_log_and_other_plugin", self.copied_log_and_other_plugin)
            self.group("long_tail_failure", self.long_tail_failure)
            self.group("changed_source_guard", lambda: self.changed_files(False))
            self.group("changed_backup_guard", lambda: self.changed_files(True))
            self.group("log_truncation", self.log_truncation)
            self.group("verdict_timeout", self.verdict_timeout)
            self.group("sigterm_during_verdict", lambda: self.interrupted_verdict(signal.SIGTERM, 143))
            self.group("sigint_during_verdict", lambda: self.interrupted_verdict(signal.SIGINT, 130))
            self.group("sigterm_during_backup", lambda: self.interrupted_replacement(signal.SIGTERM, 143))
            self.group("sigint_during_backup", lambda: self.interrupted_replacement(signal.SIGINT, 130))
            self.group("documented_delivery_and_failure", self.documented_entrypoint)
            self.group("documented_replacement_and_restart", self.documented_replacement)
            self.group("documented_restart_failure", lambda: self.documented_replacement(restart_failure=True))
            self.group("documented_install_failure", self.documented_install_failure)
            self.group("unchanged_inputs", self.unchanged_inputs)
        finally:
            self.cleanup()
        print("skate_server_plugin_install: PASS " + str(self.total) + " checks; no Rust runtime started", flush=True)


def main():
    require(len(sys.argv) == 1, "usage: SkateServerPluginInstallScenario.py")
    require_environment()
    installer = Path(__file__).resolve().parents[3] / "mods/rust/server/add-skate.sh"
    guide = Path(__file__).resolve().parents[3] / "mods/rust/server/LINUX.md"
    Scenario(installer, guide).execute()


if __name__ == "__main__":
    try:
        main()
    except (ScenarioFailure, OSError, subprocess.TimeoutExpired) as error:
        print("skate_server_plugin_install: FAIL " + str(error), file=sys.stderr)
        sys.exit(1)
