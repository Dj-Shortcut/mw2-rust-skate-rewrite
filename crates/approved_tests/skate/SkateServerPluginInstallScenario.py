#!/usr/bin/env python3
import grp
import hashlib
import os
from pathlib import Path
import pwd
import re
import shutil
import signal
import stat
import subprocess
import sys


INSTALLER_COMMIT = "44c5c1fcdd73928d285fd5c7f7ca859709a78c3c"
INSTALLER_SHA256 = "68e2e5220354e0df4fa282a32faedfc2d42b4df1c223d92c686b197b14501e5b"
SOURCE_COMMIT = "54a54a3b199fbe5d7d337b40a739b6546195c631"
SOURCE_SHA256 = "de2e6976f6fc9ab2e3099dfe3cf8c076f2a82ff6c026f75bae0c8eda67c2f8ff"
PRIOR_COMMIT = "5a9ed6e550a405365c8a18746dfd276a310b6848"
PRIOR_SHA256 = "078508fbdb67683f92d4943480250c7987bcbe051e60565713df06e41cbb465e"
PUBLIC_ROOT = "https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite"
ROOT = Path("/srv/rust")
PLUGINS = ROOT / "server/oxide/plugins"
ACTIVE = PLUGINS / "ShortcutSkate.cs"
BACKUP = PLUGINS / "ShortcutSkate.cs.bak"
MAX_DOWNLOAD_BYTES = 1024 * 1024
COMMAND_PATH = "/usr/sbin:/usr/bin:/sbin:/bin"


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
    for name in ("sh", "bash", "curl", "sha256sum", "mktemp", "cat", "chmod", "cp", "mv", "rm", "runuser", "setsid", "useradd", "userdel", "groupdel"):
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

    def install(self, arguments=(), delivered=False):
        return self.run(["bash", str(self.delivered if delivered else self.installer), *arguments])

    def clean_plugins(self):
        for path in PLUGINS.iterdir():
            if path.is_symlink() or not path.is_dir():
                path.unlink()
            else:
                shutil.rmtree(path)

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

    def missing_owner(self):
        before = self.seed_prior()
        result = self.install()
        self.check(result.returncode == 1, "missing rust identity did not fail installation")
        self.check("runuser:" in result.stderr and "rust" in result.stderr, "missing owner did not reach real runuser failure")
        self.check(snapshot(ACTIVE) == before, "missing owner changed the active source or metadata")
        self.check(not os.path.lexists(BACKUP), "missing owner created a backup")
        self.no_staging()

    def guards(self):
        before = self.seed_prior()
        result = self.install(("--unknown",))
        self.check(result.returncode == 2 and "usage:" in result.stderr, "unknown argument guard failed")
        self.check(snapshot(ACTIVE) == before, "unknown argument changed active source")
        result = self.run(["bash", "-s"], input_bytes=self.installer_bytes, nonroot=True)
        self.check(result.returncode == 1 and "run as root" in result.stderr, "nonroot guard failed")
        self.check(snapshot(ACTIVE) == before, "nonroot call changed active source")
        self.no_staging()

        moved = self.work / "missing-plugins"
        PLUGINS.rename(moved)
        try:
            result = self.install()
            self.check(result.returncode == 1 and "no Oxide plugins folder" in result.stderr, "missing-folder guard failed")
            self.check(not os.path.lexists(PLUGINS), "missing-folder guard recreated the folder")
            self.check(snapshot(moved / ACTIVE.name) == before, "missing-folder guard changed active source")
        finally:
            moved.rename(PLUGINS)

        ACTIVE.unlink()
        ACTIVE.mkdir()
        result = self.install()
        self.check(result.returncode == 1 and "must be a regular file" in result.stderr, "directory-source guard failed")
        self.check(ACTIVE.is_dir() and not any(ACTIVE.iterdir()), "directory-source guard changed its target")
        self.no_staging()
        ACTIVE.rmdir()
        ACTIVE.symlink_to(self.prior)
        prior_before = snapshot(self.prior)
        result = self.install()
        self.check(result.returncode == 1 and "must be a regular file" in result.stderr, "symlink-source guard failed")
        self.check(ACTIVE.is_symlink() and ACTIVE.readlink() == self.prior, "symlink-source guard changed the link")
        self.check(snapshot(self.prior) == prior_before, "symlink-source guard changed the linked source")
        self.no_staging()
        ACTIVE.unlink()
        ACTIVE.symlink_to(self.work / "absent.cs")
        result = self.install()
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

    def protected_active_refusal(self):
        self.seed_prior()
        os.chown(ACTIVE, 0, 0)
        ACTIVE.chmod(0o600)
        before = snapshot(ACTIVE)
        result = self.install()
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
        result = self.install(delivered=True)
        self.check(result.returncode == 0, "delivered installer failed fresh installation: " + result.stderr.strip())
        self.check("ShortcutSkate v0.3.4 source installed" in result.stdout, "fresh installation lacked source-install acknowledgement")
        self.active_installed()
        self.check(not os.path.lexists(BACKUP), "fresh installation unexpectedly created a backup")

    def replacement(self):
        before = self.seed_prior()
        result = self.install()
        self.check(result.returncode == 0, "previous-source replacement failed: " + result.stderr.strip())
        self.active_installed()
        self.check(BACKUP.is_file() and not BACKUP.is_symlink(), "previous source was not backed up as a regular file")
        self.check(snapshot(BACKUP) == before, "backup did not retain exact prior bytes, owner, group, mode and mtime")

    def documented_entrypoint(self):
        self.clean_plugins()
        scratch = self.work / "tmp"
        self.check(not any(scratch.iterdir()), "documented entrypoint scratch was not empty")
        result = self.run(["sh", "-c", self.entrypoint], timeout=150)
        self.check(result.returncode == 0, "documented entrypoint failed: " + result.stderr.strip())
        self.check("ShortcutSkate v0.3.4 source installed" in result.stdout, "documented entrypoint did not execute the installer")
        self.active_installed()
        self.check(not os.path.lexists(BACKUP), "documented fresh installation unexpectedly created a backup")
        self.check(not any(scratch.iterdir()), "documented entrypoint retained private download staging")

        before = snapshot(ACTIVE)
        failed_environment = self.environment.copy()
        missing_ca = self.work / "absent-ca.pem"
        require(not os.path.lexists(missing_ca), "TLS failure fixture must not exist")
        failed_environment["CURL_CA_BUNDLE"] = str(missing_ca)
        result = self.run(["sh", "-c", self.entrypoint], timeout=75, environment=failed_environment)
        self.check(result.returncode != 0, "documented entrypoint ignored outer curl failure")
        self.check("curl:" in result.stderr and str(missing_ca) in result.stderr, "outer download did not fail on the owned nonexistent CA bundle")
        self.check(snapshot(ACTIVE) == before, "outer download failure changed active source or metadata")
        self.check(not os.path.lexists(BACKUP), "outer download failure executed the installer and created a backup")
        self.check(not any(scratch.iterdir()), "outer download failure retained private staging")
        self.no_staging()

    def backup_failure(self):
        before = self.seed_prior()
        BACKUP.mkdir()
        sentinel = BACKUP / "retained"
        sentinel.write_bytes(b"backup directory remains\n")
        result = self.install()
        self.check(result.returncode == 1, "backup-directory replacement did not fail")
        self.check("mv:" in result.stderr and "ShortcutSkate.cs.bak" in result.stderr, "backup failure did not reach real GNU mv")
        self.check(snapshot(ACTIVE) == before, "backup failure changed active source or metadata")
        self.check(BACKUP.is_dir() and sentinel.read_bytes() == b"backup directory remains\n", "backup failure changed existing backup directory")
        self.no_staging()

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
            self.group("reviewed_delivery", self.delivery)
            self.group("missing_owner", self.missing_owner)
            self.group("admission_guards", self.guards)
            self.create_identity()
            self.group("protected_active_refusal", self.protected_active_refusal)
            self.group("fresh_install", self.fresh)
            self.group("prior_source_replacement", self.replacement)
            self.group("backup_rename_failure", self.backup_failure)
            self.group("documented_delivery_and_failure", self.documented_entrypoint)
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
