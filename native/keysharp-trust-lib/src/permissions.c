#include "keysharp_trust/permissions.h"

#include <ctype.h>
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <pwd.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <linux/if_alg.h>
#include <poll.h>
#include <sys/file.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

/* Set to 1 by ksi_permissions_cancel() to abort any in-progress prompt. */
static atomic_int g_prompt_cancelled = 0;

void ksi_permissions_cancel(void)
{
    atomic_store(&g_prompt_cancelled, 1);
}

#define KSI_PERMISSION_STORE_DIRECTORY "/var/lib/keysharp-trust"
#define KSI_PERMISSION_STORE_FILE_NAME "permissions.tsv"
#define KSI_PERMISSION_STORE_VERSION "v1"
#define KSI_PERMISSION_RECORD_TTL_SECONDS (60u * 24u * 60u * 60u)
#define KSI_PROMPT_TIMEOUT_SECONDS 60u

typedef struct ksi_permission_record {
    uid_t uid;
    char exe_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    uint32_t persistent_allowed_capabilities;
    uint32_t persistent_denied_capabilities;
    uint32_t session_allowed_capabilities;
    uint64_t last_seen_utc;
    char *exe_path;
} ksi_permission_record;

struct ksi_permission_store {
    char *path;
    ksi_permission_record *records;
    size_t count;
    size_t capacity;
};


static uint64_t utc_now_seconds(void)
{
    time_t now = time(NULL);
    return now < 0 ? 0u : (uint64_t)now;
}

/* --- Kernel AF_ALG SHA-256 wrapper (no external library dependency) ----------
 *
 * Opens an AF_ALG hash socket once per context.  Call _init, any number of
 * _update calls with MSG_MORE, then _finish to obtain the hex digest.
 * Always call _cleanup, even on error paths.
 * --------------------------------------------------------------------------- */

typedef struct {
    int alg_fd; /* bound template socket */
    int op_fd;  /* per-operation socket  */
} ksi_sha256_ctx;

static int sha256_ctx_init(ksi_sha256_ctx *ctx)
{
    static const struct sockaddr_alg sa = {
        .salg_family = AF_ALG,
        .salg_type   = "hash",
        .salg_name   = "sha256",
    };

    ctx->alg_fd = socket(AF_ALG, SOCK_SEQPACKET | SOCK_CLOEXEC, 0);

    if (ctx->alg_fd < 0) {
        fprintf(stderr, "keysharp-trust: AF_ALG socket failed: %s\n", strerror(errno));
        ctx->op_fd = -1;
        return -1;
    }

    if (bind(ctx->alg_fd, (const struct sockaddr *)&sa, sizeof(sa)) != 0) {
        fprintf(stderr, "keysharp-trust: AF_ALG bind(sha256) failed: %s\n", strerror(errno));
        close(ctx->alg_fd);
        ctx->alg_fd = -1;
        ctx->op_fd  = -1;
        return -1;
    }

    ctx->op_fd = accept(ctx->alg_fd, NULL, NULL);

    if (ctx->op_fd < 0) {
        fprintf(stderr, "keysharp-trust: AF_ALG accept failed: %s\n", strerror(errno));
        close(ctx->alg_fd);
        ctx->alg_fd = -1;
        return -1;
    }

    return 0;
}

static int sha256_ctx_update(ksi_sha256_ctx *ctx, const void *data, size_t len)
{
    if (ctx->op_fd < 0) {
        return -1;
    }

    if (send(ctx->op_fd, data, len, MSG_MORE) != (ssize_t)len) {
        fprintf(stderr, "keysharp-trust: AF_ALG send failed: %s\n", strerror(errno));
        return -1;
    }

    return 0;
}

static int sha256_ctx_finish(ksi_sha256_ctx *ctx, char output[KSI_PERMISSION_HASH_HEX_LENGTH + 1u])
{
    static const char hex_chars[] = "0123456789abcdef";
    uint8_t hash[32];

    if (ctx->op_fd < 0) {
        return -1;
    }

    if (read(ctx->op_fd, hash, sizeof(hash)) != (ssize_t)sizeof(hash)) {
        fprintf(stderr, "keysharp-trust: AF_ALG read digest failed: %s\n", strerror(errno));
        return -1;
    }

    for (size_t i = 0; i < 32u; i++) {
        output[i * 2u]        = hex_chars[(hash[i] >> 4u) & 0x0fu];
        output[(i * 2u) + 1u] = hex_chars[hash[i] & 0x0fu];
    }

    output[KSI_PERMISSION_HASH_HEX_LENGTH] = '\0';
    return 0;
}

static void sha256_ctx_cleanup(ksi_sha256_ctx *ctx)
{
    if (ctx->op_fd  >= 0) { close(ctx->op_fd);  ctx->op_fd  = -1; }
    if (ctx->alg_fd >= 0) { close(ctx->alg_fd); ctx->alg_fd = -1; }
}

/* Hash the open file descriptor and hex-encode the digest into output. */
static int compute_fd_sha256_hex(int fd, char output[KSI_PERMISSION_HASH_HEX_LENGTH + 1u])
{
    uint8_t io_buf[8192];
    ksi_sha256_ctx ctx;
    ssize_t n;
    int result = -1;

    if (lseek(fd, 0, SEEK_SET) < 0) {
        return -1;
    }

    if (sha256_ctx_init(&ctx) != 0) {
        return -1;
    }

    while ((n = read(fd, io_buf, sizeof(io_buf))) > 0) {
        if (sha256_ctx_update(&ctx, io_buf, (size_t)n) != 0) {
            goto cleanup;
        }
    }

    if (n < 0) {
        goto cleanup;
    }

    result = sha256_ctx_finish(&ctx, output);

cleanup:
    sha256_ctx_cleanup(&ctx);
    return result;
}

static void append_argument_display(
    char *output,
    size_t output_size,
    size_t *output_length,
    bool *skipping_argv0,
    const uint8_t *data,
    size_t data_length)
{
    if (output == NULL || output_size == 0u || output_length == NULL || skipping_argv0 == NULL) {
        return;
    }

    for (size_t i = 0; i < data_length; i++) {
        uint8_t value = data[i];

        if (*skipping_argv0) {
            if (value == '\0') {
                *skipping_argv0 = false;
            }

            continue;
        }

        if (*output_length + 1u >= output_size) {
            break;
        }

        if (value == '\0') {
            output[(*output_length)++] = ' ';
        } else if (isprint(value)) {
            output[(*output_length)++] = (char)value;
        } else {
            output[(*output_length)++] = '?';
        }
    }

    output[*output_length] = '\0';
}

/* Returns true when exe at path is owned by root and not writable by group or
 * others — i.e. only a privileged process can replace it.  In that case the
 * path string is a sufficient trust identity and we skip reading file content. */
static bool is_protected_exe(const char *path)
{
    struct stat info;

    if (path == NULL || stat(path, &info) != 0)
        return false;

    return info.st_uid == 0 && (info.st_mode & (S_IWGRP | S_IWOTH)) == 0;
}

/* Produces a stable identity hash for executables in protected (root-owned,
 * non-world-writable) locations.  Uses the path STRING — not file content — so
 * the record survives package updates, PLUS the process's full command line so
 * each distinct invocation (in particular each script path carried in argv) gets
 * its own prompt.  Also fills command_line_buffer (argv0 skipped) for the
 * prompt's display text.  A missing/empty cmdline degrades gracefully to a
 * path-only identity rather than failing.
 *
 * SECURITY BOUNDARY: the enforced boundary is (uid, exe) — uid comes from
 * SO_PEERCRED (unforgeable) and the exe portion is the kernel's real path/inode
 * of the running binary (unforgeable).  The cmdline component is NOT a security
 * separator: a process can rewrite its own /proc/<pid>/cmdline (setproctitle),
 * so a HOSTILE same-uid process running the same trusted interpreter can forge a
 * previously-trusted sibling script's command line and inherit that grant
 * without a prompt.  Per-script prompting is therefore per-invocation
 * granularity for COOPERATIVE scripts, not a defense against a malicious
 * same-uid peer.  Cross-uid and cross-executable are genuinely blocked. */
/* WILDCARD identity: SHA-256 over a domain tag plus ONLY the exe portion (path for a protected
 * install, content digest for a dev build) — deliberately excluding the command line, so it is the
 * same for every script run by the binary. Backs the "Allow for executable" grant. */
static int hash_wildcard_identity(
    const void *exe_portion,
    size_t exe_portion_length,
    char output[KSI_PERMISSION_HASH_HEX_LENGTH + 1u])
{
    static const uint8_t domain[] = "Keysharp-wildcard-identity-v1";
    ksi_sha256_ctx ctx;
    int result = -1;

    if (sha256_ctx_init(&ctx) != 0)
        return -1;

    if (sha256_ctx_update(&ctx, domain, sizeof(domain)) != 0
        || sha256_ctx_update(&ctx, exe_portion, exe_portion_length) != 0)
        goto cleanup;

    result = sha256_ctx_finish(&ctx, output);

cleanup:
    sha256_ctx_cleanup(&ctx);
    return result;
}

static int hash_protected_path_identity(
    const char *exe_path,
    pid_t pid,
    char *command_line_buffer,
    size_t command_line_buffer_size,
    char output[KSI_PERMISSION_HASH_HEX_LENGTH + 1u],
    char *wildcard_output)
{
    static const uint8_t domain[] = "Keysharp-protected-path-identity-v2";
    char proc_path[64];
    uint8_t buffer[8192];
    ksi_sha256_ctx ctx;
    size_t display_length = 0u;
    bool skipping_argv0 = true;
    ssize_t bytes_read;
    int cmdline_fd;
    int result = -1;

    if (command_line_buffer != NULL && command_line_buffer_size != 0u)
        command_line_buffer[0] = '\0';

    if (wildcard_output != NULL)
        wildcard_output[0] = '\0';

    if (sha256_ctx_init(&ctx) != 0)
        return -1;

    if (sha256_ctx_update(&ctx, domain, sizeof(domain)) != 0
        || sha256_ctx_update(&ctx, exe_path, strlen(exe_path)) != 0)
        goto cleanup;

    (void)snprintf(proc_path, sizeof(proc_path), "/proc/%ld/cmdline", (long)pid);
    cmdline_fd = open(proc_path, O_RDONLY | O_CLOEXEC);

    if (cmdline_fd >= 0) {
        while ((bytes_read = read(cmdline_fd, buffer, sizeof(buffer))) > 0) {
            if (sha256_ctx_update(&ctx, buffer, (size_t)bytes_read) != 0) {
                close(cmdline_fd);
                goto cleanup;
            }

            append_argument_display(
                command_line_buffer, command_line_buffer_size, &display_length, &skipping_argv0,
                buffer, (size_t)bytes_read);
        }

        close(cmdline_fd);

        if (command_line_buffer != NULL)
            while (display_length > 0u && command_line_buffer[display_length - 1u] == ' ')
                command_line_buffer[--display_length] = '\0';
    }

    result = sha256_ctx_finish(&ctx, output);

    /* Wildcard identity from the exe PATH only (no argv). Optional: on failure leave it empty so it
     * simply never matches an "all scripts" record, rather than failing identification outright. */
    if (result == 0 && wildcard_output != NULL
        && hash_wildcard_identity(exe_path, strlen(exe_path), wildcard_output) != 0)
        wildcard_output[0] = '\0';

cleanup:
    sha256_ctx_cleanup(&ctx);
    return result;
}

static int hash_process_identity(
    int exe_fd,
    pid_t pid,
    char *command_line_buffer,
    size_t command_line_buffer_size,
    char output[KSI_PERMISSION_HASH_HEX_LENGTH + 1u],
    char *wildcard_output)
{
    static const uint8_t domain[] = "Keysharp-process-identity-v1";
    char exe_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    char proc_path[64];
    uint8_t buffer[8192];
    ksi_sha256_ctx ctx;
    size_t display_length = 0u;
    bool has_command_line = false;
    bool skipping_argv0 = true;
    ssize_t bytes_read;
    int cmdline_fd;
    int result = -1;

    if (command_line_buffer == NULL || command_line_buffer_size == 0u) {
        return -1;
    }

    command_line_buffer[0] = '\0';

    if (wildcard_output != NULL)
        wildcard_output[0] = '\0';

    if (compute_fd_sha256_hex(exe_fd, exe_hash) != 0) {
        return -1;
    }

    (void)snprintf(proc_path, sizeof(proc_path), "/proc/%ld/cmdline", (long)pid);
    cmdline_fd = open(proc_path, O_RDONLY | O_CLOEXEC);

    if (cmdline_fd < 0) {
        return -1;
    }

    if (sha256_ctx_init(&ctx) != 0) {
        close(cmdline_fd);
        return -1;
    }

    if (sha256_ctx_update(&ctx, domain, sizeof(domain)) != 0
        || sha256_ctx_update(&ctx, exe_hash, KSI_PERMISSION_HASH_HEX_LENGTH) != 0) {
        goto cleanup;
    }

    while ((bytes_read = read(cmdline_fd, buffer, sizeof(buffer))) > 0) {
        has_command_line = true;

        if (sha256_ctx_update(&ctx, buffer, (size_t)bytes_read) != 0) {
            goto cleanup;
        }

        append_argument_display(
            command_line_buffer, command_line_buffer_size, &display_length, &skipping_argv0,
            buffer, (size_t)bytes_read);
    }

    if (bytes_read < 0 || !has_command_line) {
        goto cleanup;
    }

    while (display_length > 0u && command_line_buffer[display_length - 1u] == ' ') {
        command_line_buffer[--display_length] = '\0';
    }

    result = sha256_ctx_finish(&ctx, output);

    /* Wildcard identity from the exe CONTENT digest only (no argv) — same across every script run. */
    if (result == 0 && wildcard_output != NULL
        && hash_wildcard_identity(exe_hash, KSI_PERMISSION_HASH_HEX_LENGTH, wildcard_output) != 0)
        wildcard_output[0] = '\0';

cleanup:
    sha256_ctx_cleanup(&ctx);
    close(cmdline_fd);
    return result;
}

static bool looks_like_sha256(const char *text)
{
    if (text == NULL || strlen(text) != KSI_PERMISSION_HASH_HEX_LENGTH) {
        return false;
    }

    for (size_t i = 0; i < KSI_PERMISSION_HASH_HEX_LENGTH; i++) {
        if (!isxdigit((unsigned char)text[i])) {
            return false;
        }
    }

    return true;
}

/* Creates the directory if it does not exist and verifies it is a directory.
 * When fix_permissions is true, strips group/other bits if they are set
 * (used for the leaf directory that holds sensitive files). */
static int ensure_directory(const char *path, bool fix_permissions)
{
    struct stat info;

    if (path == NULL || path[0] == '\0') {
        return -1;
    }

    if (mkdir(path, S_IRWXU) != 0 && errno != EEXIST) {
        return -1;
    }

    if (stat(path, &info) != 0 || !S_ISDIR(info.st_mode)) {
        return -1;
    }

    if (fix_permissions && (info.st_mode & (S_IRWXG | S_IRWXO)) != 0) {
        (void)chmod(path, S_IRWXU);
    }

    return 0;
}

static int ensure_parent_directories(const char *path)
{
    char *copy;
    size_t length;

    if (path == NULL) {
        return -1;
    }

    copy = strdup(path);

    if (copy == NULL) {
        return -1;
    }

    length = strlen(copy);

    for (size_t i = 1; i < length; i++) {
        if (copy[i] != '/') {
            continue;
        }

        copy[i] = '\0';

        if (ensure_directory(copy, false) != 0) {
            free(copy);
            return -1;
        }

        copy[i] = '/';
    }

    free(copy);
    return 0;
}

static char *resolve_store_path(void)
{
    char full_path[KSI_PERMISSION_MAX_PATH];
    int written;

    if (ensure_parent_directories(KSI_PERMISSION_STORE_DIRECTORY) != 0
        || ensure_directory(KSI_PERMISSION_STORE_DIRECTORY, true) != 0) {
        return NULL;
    }

    written = snprintf(
        full_path, sizeof(full_path), "%s/%s",
        KSI_PERMISSION_STORE_DIRECTORY, KSI_PERMISSION_STORE_FILE_NAME);

    if (written < 0 || (size_t)written >= sizeof(full_path)) {
        return NULL;
    }

    return strdup(full_path);
}

static char *escape_field(const char *value)
{
    size_t length;
    char *result;
    char *cursor;

    if (value == NULL) {
        return strdup("");
    }

    length = strlen(value);
    result = calloc((length * 2u) + 1u, sizeof(char));

    if (result == NULL) {
        return NULL;
    }

    cursor = result;

    for (size_t i = 0; i < length; i++) {
        switch (value[i]) {
            case '\\':
                *cursor++ = '\\';
                *cursor++ = '\\';
                break;

            case '\t':
                *cursor++ = '\\';
                *cursor++ = 't';
                break;

            case '\n':
                *cursor++ = '\\';
                *cursor++ = 'n';
                break;

            case '\r':
                *cursor++ = '\\';
                *cursor++ = 'r';
                break;

            default:
                *cursor++ = value[i];
                break;
        }
    }

    *cursor = '\0';
    return result;
}

static char *unescape_field(const char *value)
{
    size_t length;
    char *result;
    char *cursor;

    if (value == NULL) {
        return strdup("");
    }

    length = strlen(value);
    result = calloc(length + 1u, sizeof(char));

    if (result == NULL) {
        return NULL;
    }

    cursor = result;

    for (size_t i = 0; i < length; i++) {
        if (value[i] == '\\' && i + 1u < length) {
            i++;

            switch (value[i]) {
                case 't':
                    *cursor++ = '\t';
                    break;

                case 'n':
                    *cursor++ = '\n';
                    break;

                case 'r':
                    *cursor++ = '\r';
                    break;

                case '\\':
                    *cursor++ = '\\';
                    break;

                default:
                    *cursor++ = value[i];
                    break;
            }
        } else {
            *cursor++ = value[i];
        }
    }

    *cursor = '\0';
    return result;
}

static void free_record(ksi_permission_record *record)
{
    if (record == NULL) {
        return;
    }

    free(record->exe_path);
    record->exe_path = NULL;
}

static bool ensure_capacity(ksi_permission_store *store, size_t required)
{
    ksi_permission_record *records;
    size_t capacity;

    if (store->capacity >= required) {
        return true;
    }

    capacity = store->capacity == 0u ? 8u : store->capacity * 2u;

    while (capacity < required) {
        capacity *= 2u;
    }

    records = realloc(store->records, capacity * sizeof(*records));

    if (records == NULL) {
        return false;
    }

    store->records = records;
    store->capacity = capacity;
    return true;
}

static ssize_t find_record_index(const ksi_permission_store *store, uid_t uid, const char *exe_hash)
{
    if (store == NULL || exe_hash == NULL) {
        return -1;
    }

    for (size_t i = 0; i < store->count; i++) {
        if (store->records[i].uid == uid && strcmp(store->records[i].exe_hash, exe_hash) == 0) {
            return (ssize_t)i;
        }
    }

    return -1;
}

static ksi_permission_record *get_or_add_record(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    const char *exe_path)
{
    ssize_t index;
    ksi_permission_record *record;

    if (store == NULL || exe_hash == NULL || !looks_like_sha256(exe_hash)) {
        return NULL;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index >= 0) {
        record = &store->records[index];
    } else {
        if (!ensure_capacity(store, store->count + 1u)) {
            return NULL;
        }

        record = &store->records[store->count++];
        memset(record, 0, sizeof(*record));
        record->uid = uid;
        (void)snprintf(record->exe_hash, sizeof(record->exe_hash), "%s", exe_hash);
    }

    if (exe_path != NULL && exe_path[0] != '\0') {
        char *copy = strdup(exe_path);

        if (copy == NULL) {
            return NULL;
        }

        free(record->exe_path);
        record->exe_path = copy;
    }

    record->last_seen_utc = utc_now_seconds();
    return record;
}

static bool prune_expired_records(ksi_permission_store *store)
{
    uint64_t now = utc_now_seconds();
    uint64_t cutoff = now > KSI_PERMISSION_RECORD_TTL_SECONDS
        ? now - KSI_PERMISSION_RECORD_TTL_SECONDS
        : 0u;
    bool changed = false;

    if (store == NULL) {
        return false;
    }

    for (size_t i = 0; i < store->count;) {
        ksi_permission_record *record = &store->records[i];
        bool expired = record->last_seen_utc < cutoff;
        bool empty_persistent = record->persistent_allowed_capabilities == 0u
            && record->persistent_denied_capabilities == 0u;
        bool empty_session = record->session_allowed_capabilities == 0u;

        if (expired || (empty_persistent && empty_session)) {
            free_record(record);

            if (i + 1u < store->count) {
                memmove(record, record + 1u, (store->count - i - 1u) * sizeof(*record));
            }

            store->count--;
            changed = true;
            continue;
        }

        i++;
    }

    return changed;
}

static int save_store(const ksi_permission_store *store)
{
    char *temp_path;
    FILE *file;
    int fd;

    if (store == NULL || store->path == NULL) {
        return -1;
    }

    temp_path = calloc(strlen(store->path) + 5u, sizeof(char));

    if (temp_path == NULL) {
        return -1;
    }

    (void)snprintf(temp_path, strlen(store->path) + 5u, "%s.tmp", store->path);
    file = fopen(temp_path, "w");

    if (file == NULL) {
        free(temp_path);
        return -1;
    }

    (void)fprintf(file, "%s\n", KSI_PERMISSION_STORE_VERSION);

    for (size_t i = 0; i < store->count; i++) {
        const ksi_permission_record *record = &store->records[i];
        char *escaped_path;

        if (record->persistent_allowed_capabilities == 0u
            && record->persistent_denied_capabilities == 0u) {
            continue;
        }

        escaped_path = escape_field(record->exe_path);

        if (escaped_path == NULL) {
            fclose(file);
            (void)unlink(temp_path);
            free(temp_path);
            return -1;
        }

        (void)fprintf(
            file,
            "%lu\t%s\t%08x\t%08x\t%llu\t%s\n",
            (unsigned long)record->uid,
            record->exe_hash,
            record->persistent_allowed_capabilities,
            record->persistent_denied_capabilities,
            (unsigned long long)record->last_seen_utc,
            escaped_path);
        free(escaped_path);
    }

    if (fflush(file) != 0) {
        fclose(file);
        (void)unlink(temp_path);
        free(temp_path);
        return -1;
    }

    fd = fileno(file);

    if (fd >= 0 && fsync(fd) != 0) {
        fprintf(stderr, "keysharp-trust: fsync on trust store failed: %s\n", strerror(errno));
    }

    if (fclose(file) != 0) {
        (void)unlink(temp_path);
        free(temp_path);
        return -1;
    }

    if (rename(temp_path, store->path) != 0) {
        (void)unlink(temp_path);
        free(temp_path);
        return -1;
    }

    free(temp_path);
    return 0;
}

static int load_store(ksi_permission_store *store)
{
    FILE *file;
    char *line = NULL;
    size_t line_capacity = 0u;
    ssize_t line_length;

    if (store == NULL || store->path == NULL) {
        return -1;
    }

    file = fopen(store->path, "r");

    if (file == NULL) {
        return errno == ENOENT ? 0 : -1;
    }

    while ((line_length = getline(&line, &line_capacity, file)) >= 0) {
        char *uid_text;
        char *hash_text;
        char *allowed_text;
        char *denied_text;
        char *seen_text;
        char *path_text;
        char *cursor;
        char *unescaped_path;
        unsigned long allowed_value;
        unsigned long denied_value;
        unsigned long long seen_value;
        ksi_permission_record *record;

        while (line_length > 0 && (line[line_length - 1] == '\n' || line[line_length - 1] == '\r')) {
            line[--line_length] = '\0';
        }

        if (line[0] == '\0' || strcmp(line, KSI_PERMISSION_STORE_VERSION) == 0) {
            continue;
        }

        uid_text = line;
        hash_text = strchr(uid_text, '\t');

        if (hash_text == NULL) {
            continue;
        }

        *hash_text++ = '\0';
        allowed_text = strchr(hash_text, '\t');

        if (allowed_text == NULL) {
            continue;
        }

        *allowed_text++ = '\0';
        denied_text = strchr(allowed_text, '\t');

        if (denied_text == NULL) {
            continue;
        }

        *denied_text++ = '\0';
        seen_text = strchr(denied_text, '\t');

        if (seen_text == NULL) {
            continue;
        }

        *seen_text++ = '\0';
        path_text = strchr(seen_text, '\t');

        if (path_text == NULL) {
            continue;
        }

        *path_text++ = '\0';

        errno = 0;
        unsigned long uid_value = strtoul(uid_text, &cursor, 10);

        if (errno != 0 || cursor == uid_text || *cursor != '\0'
            || !looks_like_sha256(hash_text)) {
            continue;
        }

        errno = 0;
        allowed_value = strtoul(allowed_text, &cursor, 16);

        if (errno != 0 || cursor == allowed_text || *cursor != '\0') {
            continue;
        }

        errno = 0;
        denied_value = strtoul(denied_text, &cursor, 16);

        if (errno != 0 || cursor == denied_text || *cursor != '\0') {
            continue;
        }

        errno = 0;
        seen_value = strtoull(seen_text, &cursor, 10);

        if (errno != 0 || cursor == seen_text || *cursor != '\0') {
            continue;
        }

        unescaped_path = unescape_field(path_text);

        if (unescaped_path == NULL) {
            continue;
        }

        record = get_or_add_record(store, (uid_t)uid_value, hash_text, unescaped_path);
        free(unescaped_path);

        if (record == NULL) {
            continue;
        }

        record->persistent_allowed_capabilities = (uint32_t)allowed_value;
        record->persistent_denied_capabilities = (uint32_t)denied_value;
        record->last_seen_utc = (uint64_t)seen_value;
    }

    free(line);
    fclose(file);

    if (prune_expired_records(store)) {
        return save_store(store);
    }

    return 0;
}

typedef struct ksi_prompt_environment {
    char *display;
    char *wayland_display;
    char *runtime_dir;
    char *dbus_session_bus_address;
    char *xauthority;
} ksi_prompt_environment;

static void free_prompt_environment(ksi_prompt_environment *environment)
{
    if (environment == NULL) {
        return;
    }

    free(environment->display);
    free(environment->wayland_display);
    free(environment->runtime_dir);
    free(environment->dbus_session_bus_address);
    free(environment->xauthority);
    memset(environment, 0, sizeof(*environment));
}

static void copy_prompt_environment_value(
    ksi_prompt_environment *environment,
    const char *name,
    const char *value)
{
    char **target = NULL;

    if (strcmp(name, "DISPLAY") == 0) {
        target = &environment->display;
    } else if (strcmp(name, "WAYLAND_DISPLAY") == 0) {
        target = &environment->wayland_display;
    } else if (strcmp(name, "XDG_RUNTIME_DIR") == 0) {
        target = &environment->runtime_dir;
    } else if (strcmp(name, "DBUS_SESSION_BUS_ADDRESS") == 0) {
        target = &environment->dbus_session_bus_address;
    } else if (strcmp(name, "XAUTHORITY") == 0) {
        target = &environment->xauthority;
    }

    if (target != NULL && *target == NULL && value[0] != '\0') {
        *target = strdup(value);
    }
}

static int read_prompt_environment(pid_t pid, ksi_prompt_environment *environment)
{
    char proc_path[64];
    char buffer[32768];
    ssize_t bytes_read;
    int fd;
    size_t offset = 0u;

    memset(environment, 0, sizeof(*environment));
    (void)snprintf(proc_path, sizeof(proc_path), "/proc/%ld/environ", (long)pid);
    fd = open(proc_path, O_RDONLY | O_CLOEXEC);

    if (fd < 0) {
        return -1;
    }

    bytes_read = read(fd, buffer, sizeof(buffer) - 1u);
    close(fd);

    if (bytes_read <= 0) {
        return -1;
    }

    buffer[bytes_read] = '\0';

    while (offset < (size_t)bytes_read) {
        char *entry = &buffer[offset];
        size_t length = strnlen(entry, (size_t)bytes_read - offset);
        char *equals;

        if (length == 0u) {
            offset++;
            continue;
        }

        equals = memchr(entry, '=', length);

        if (equals != NULL) {
            *equals = '\0';
            copy_prompt_environment_value(environment, entry, equals + 1);
        }

        offset += length + 1u;
    }

    return 0;
}

static void set_prompt_environment_value(const char *name, const char *value)
{
    if (value != NULL && value[0] != '\0') {
        (void)setenv(name, value, 1);
    }
}

static int run_prompt_command(
    const char *file_name,
    char *const argv[],
    pid_t requester_pid,
    uid_t requester_uid,
    gid_t requester_gid,
    char *output,
    size_t output_size)
{
    int pipe_fds[2];
    pid_t child;
    int wait_status;
    bool child_reaped = false;
    struct timespec deadline;
    ksi_prompt_environment environment;
    size_t output_used = 0;

    if (file_name == NULL || file_name[0] != '/' || access(file_name, X_OK) != 0
        || read_prompt_environment(requester_pid, &environment) != 0) {
        return -1;
    }

    if (pipe(pipe_fds) != 0) {
        free_prompt_environment(&environment);
        return -1;
    }

    child = fork();

    if (child < 0) {
        close(pipe_fds[0]);
        close(pipe_fds[1]);
        free_prompt_environment(&environment);
        return -1;
    }

    if (child == 0) {
        struct passwd *user = getpwuid(requester_uid);
        int devnull = open("/dev/null", O_WRONLY | O_CLOEXEC);

        (void)dup2(pipe_fds[1], STDOUT_FILENO);

        if (devnull >= 0) {
            (void)dup2(devnull, STDERR_FILENO);
            close(devnull);
        }

        close(pipe_fds[0]);
        close(pipe_fds[1]);

        if (geteuid() == 0) {
            if (setgroups(0, NULL) != 0
                || setresgid(requester_gid, requester_gid, requester_gid) != 0
                || setresuid(requester_uid, requester_uid, requester_uid) != 0) {
                _exit(127);
            }
        }

        (void)clearenv();
        (void)setenv("PATH", "/usr/sbin:/usr/bin:/sbin:/bin", 1);

        if (user != NULL && user->pw_dir != NULL) {
            (void)setenv("HOME", user->pw_dir, 1);
        }

        set_prompt_environment_value("DISPLAY", environment.display);
        set_prompt_environment_value("WAYLAND_DISPLAY", environment.wayland_display);
        set_prompt_environment_value("XDG_RUNTIME_DIR", environment.runtime_dir);
        set_prompt_environment_value("DBUS_SESSION_BUS_ADDRESS", environment.dbus_session_bus_address);
        set_prompt_environment_value("XAUTHORITY", environment.xauthority);
        execv(file_name, argv);
        _exit(127);
    }

    free_prompt_environment(&environment);
    close(pipe_fds[1]);

    /* Set the read end non-blocking so we can drain it in the wait loop
     * without risking a block if the child hasn't written output yet. */
    {
        int flags = fcntl(pipe_fds[0], F_GETFL);

        if (flags >= 0) {
            (void)fcntl(pipe_fds[0], F_SETFL, flags | O_NONBLOCK);
        }
    }

    /* Monotonic deadline — immune to NTP steps and DST transitions. */
    if (clock_gettime(CLOCK_MONOTONIC, &deadline) != 0) {
        (void)kill(child, SIGTERM);
        (void)waitpid(child, &wait_status, 0);
        close(pipe_fds[0]);
        return -1;
    }

    deadline.tv_sec += (time_t)KSI_PROMPT_TIMEOUT_SECONDS;

    while (!child_reaped) {
        struct timespec now;
        struct pollfd wait_pfd;
        int64_t remaining_ms;
        int poll_timeout_ms;
        pid_t reap_result;

        if (atomic_load(&g_prompt_cancelled)) {
            break;
        }

        if (clock_gettime(CLOCK_MONOTONIC, &now) != 0) {
            break;
        }

        remaining_ms = ((int64_t)(deadline.tv_sec - now.tv_sec) * 1000LL)
                       + ((int64_t)(deadline.tv_nsec - now.tv_nsec) / 1000000LL);

        if (remaining_ms <= 0) {
            break;
        }

        /* Cap at 100 ms so we check cancellation frequently, but wake
         * immediately when the child writes output to the pipe. */
        poll_timeout_ms = (int)(remaining_ms > 100LL ? 100LL : remaining_ms);
        wait_pfd.fd = pipe_fds[0];
        wait_pfd.events = POLLIN;
        (void)poll(&wait_pfd, 1, poll_timeout_ms);

        /* Drain whatever the child has written, whether or not poll fired. */
        if (output != NULL && output_size > 0u) {
            ssize_t n;

            while (output_used < output_size - 1u) {
                n = read(pipe_fds[0], output + output_used,
                         output_size - 1u - output_used);

                if (n > 0) {
                    output_used += (size_t)n;
                } else {
                    break;
                }
            }
        }

        reap_result = waitpid(child, &wait_status, WNOHANG);

        if (reap_result == child) {
            child_reaped = true;
        } else if (reap_result < 0) {
            break;
        }
    }

    if (!child_reaped) {
        (void)kill(child, SIGTERM);
        (void)waitpid(child, &wait_status, 0);
    }

    /* Final drain: collect any output written after the last poll iteration. */
    if (output != NULL && output_size > 0u) {
        ssize_t n;

        while (output_used < output_size - 1u) {
            n = read(pipe_fds[0], output + output_used, output_size - 1u - output_used);

            if (n > 0) {
                output_used += (size_t)n;
            } else {
                break;
            }
        }

        output[output_used] = '\0';

        while (output_used > 0
               && (output[output_used - 1u] == '\n' || output[output_used - 1u] == '\r')) {
            output[--output_used] = '\0';
        }
    }

    close(pipe_fds[0]);

    if (child_reaped && WIFEXITED(wait_status)) {
        return WEXITSTATUS(wait_status);
    }

    return -1;
}

static void append_capability_line(char *buffer, size_t buffer_size, const char *line)
{
    size_t used = strlen(buffer);

    if (used >= buffer_size) {
        return;
    }

    (void)snprintf(buffer + used, buffer_size - used, "- %s\n", line);
}

/* Wraps str at word boundaries for display, inserting '\n' so no line exceeds
 * line_width characters. Falls back to hard line breaks for runs longer than
 * line_width with no spaces (e.g. paths, hashes). Writes at most
 * out_size-1 bytes plus a NUL into out. */
static void wrap_text(const char *str, size_t line_width, char *out, size_t out_size)
{
    size_t in_pos = 0u;
    size_t out_pos = 0u;
    size_t col = 0u;
    size_t in_len;

    if (str == NULL || out == NULL || out_size == 0u || line_width == 0u) {
        return;
    }

    in_len = strlen(str);

    while (in_pos < in_len && out_pos + 1u < out_size) {
        char c = str[in_pos];

        if (c == '\n') {
            out[out_pos++] = '\n';
            col = 0u;
            in_pos++;
            continue;
        }

        if (c == ' ') {
            /* Drop spaces that would open a new line. */
            if (col > 0u && out_pos + 1u < out_size) {
                out[out_pos++] = ' ';
                col++;
            }

            in_pos++;
            continue;
        }

        /* Non-space: find the end of this word. */
        {
            size_t word_end = in_pos;
            size_t word_len;

            while (word_end < in_len && str[word_end] != ' ' && str[word_end] != '\n') {
                word_end++;
            }

            word_len = word_end - in_pos;

            /* Soft wrap: if the whole word fits on the next line but not this
             * one, emit a line break before it. */
            if (col > 0u && col + word_len > line_width && word_len <= line_width) {
                if (out_pos + 1u < out_size) {
                    out[out_pos++] = '\n';
                    col = 0u;
                }
            }

            /* Write word characters, hard-breaking overlong words. */
            while (in_pos < word_end && out_pos + 1u < out_size) {
                if (col >= line_width && out_pos + 1u < out_size) {
                    out[out_pos++] = '\n';
                    col = 0u;
                }

                if (out_pos + 1u < out_size) {
                    out[out_pos++] = str[in_pos++];
                    col++;
                }
            }
        }
    }

    out[out_pos < out_size ? out_pos : out_size - 1u] = '\0';
}

static void describe_capabilities(uint32_t capabilities, char *buffer, size_t buffer_size)
{
    if (buffer == NULL || buffer_size == 0u) {
        return;
    }

    buffer[0] = '\0';

    if ((capabilities & KST_CAP_INPUT_HOOK) == KST_CAP_INPUT_HOOK) {
        append_capability_line(buffer, buffer_size, "Monitor keyboard and mouse input");
    } else if ((capabilities & KST_CAP_INPUT_HOOK_KEYBOARD) != 0u) {
        append_capability_line(buffer, buffer_size, "Monitor keyboard input");
    } else if ((capabilities & KST_CAP_INPUT_HOOK_MOUSE) != 0u) {
        append_capability_line(buffer, buffer_size, "Monitor mouse input");
    }

    if ((capabilities & KST_CAP_INPUT_SYNTH) == KST_CAP_INPUT_SYNTH) {
        append_capability_line(buffer, buffer_size, "Synthesize keyboard and mouse input");
    } else if ((capabilities & KST_CAP_INPUT_SYNTH_KEYBOARD) != 0u) {
        append_capability_line(buffer, buffer_size, "Synthesize keyboard input");
    } else if ((capabilities & KST_CAP_INPUT_SYNTH_MOUSE) != 0u) {
        append_capability_line(buffer, buffer_size, "Synthesize mouse input");
    }

    if ((capabilities & KST_CAP_INPUT_BLOCK) != 0u) {
        append_capability_line(buffer, buffer_size, "Block or suppress input");
    }

    if ((capabilities & KST_CAP_SCREEN_CAPTURE) != 0u) {
        append_capability_line(buffer, buffer_size, "Capture the screen");
    }

    if ((capabilities & KST_CAP_ACCESSIBILITY_AUTOMATION) != 0u) {
        append_capability_line(buffer, buffer_size, "Automate applications via accessibility APIs");
    }
}

/* ── PID-keyed session implementation ─────────────────────────────────────── */

#define KSI_SESSION_DIR "/run/keysharp-trust/sessions"

static int build_session_path(uid_t uid, pid_t pid, uint64_t start_time,
                               char *buf, size_t buf_size)
{
    int written = snprintf(buf, buf_size, KSI_SESSION_DIR "/%lu-%ld-%llx",
                           (unsigned long)uid, (long)pid,
                           (unsigned long long)start_time);
    return (written > 0 && (size_t)written < buf_size) ? 0 : -1;
}

uint64_t ksi_permissions_get_process_start_time(pid_t pid)
{
    char path[64];
    char buf[512];
    int fd;
    ssize_t n;
    char *p;
    int field;

    (void)snprintf(path, sizeof(path), "/proc/%ld/stat", (long)pid);
    fd = open(path, O_RDONLY | O_CLOEXEC);
    if (fd < 0) return 0u;
    n = read(fd, buf, sizeof(buf) - 1u);
    close(fd);
    if (n <= 0) return 0u;
    buf[n] = '\0';

    /* Skip past the comm field "pid (comm) state ..." — comm may contain spaces
     * and parentheses, so find the last ')' to locate the end of the field. */
    p = strrchr(buf, ')');
    if (p == NULL) return 0u;
    p += 2; /* skip ') ' */

    /* starttime is field 22 (1-indexed); we are now pointing at field 3 (state).
     * Skip fields 3–21 (19 spaces) to land on field 22. */
    for (field = 3; field < 22; field++) {
        p = strchr(p, ' ');
        if (p == NULL) return 0u;
        p++;
    }

    return (uint64_t)strtoull(p, NULL, 10);
}

uint32_t ksi_permissions_get_session_by_pid(uid_t uid, pid_t pid, uint64_t start_time)
{
    char path[KSI_PERMISSION_MAX_PATH];
    char buf[32];
    int fd;
    ssize_t n;

    if (start_time == 0u) return 0u;
    if (build_session_path(uid, pid, start_time, path, sizeof(path)) != 0) return 0u;

    fd = open(path, O_RDONLY | O_CLOEXEC);
    if (fd < 0) return 0u;

    (void)flock(fd, LOCK_SH);
    n = read(fd, buf, sizeof(buf) - 1u);
    (void)flock(fd, LOCK_UN);
    close(fd);

    if (n <= 0) return 0u;
    buf[n] = '\0';
    return (uint32_t)strtoul(buf, NULL, 16);
}

int ksi_permissions_grant_session_for_pid(uid_t uid, pid_t pid, uint64_t start_time,
                                          uint32_t capabilities)
{
    char path[KSI_PERMISSION_MAX_PATH];
    char buf[32];
    int fd;
    ssize_t n;
    uint32_t existing = 0u;

    if (start_time == 0u || capabilities == 0u) return -1;

    /* Ensure /run/keysharp-trust/sessions/ exists (world-executable so other root
     * processes can enter it, but only root can list or create files). */
    if (ensure_parent_directories(KSI_SESSION_DIR) != 0
        || ensure_directory(KSI_SESSION_DIR, false) != 0)
        return -1;

    if (build_session_path(uid, pid, start_time, path, sizeof(path)) != 0) return -1;

    /* Open for read-write, creating if absent.  flock serialises concurrent
     * writers (e.g. inputd and keysharp-helper granting at the same time). */
    fd = open(path, O_RDWR | O_CREAT | O_CLOEXEC, 0600);
    if (fd < 0) return -1;

    (void)flock(fd, LOCK_EX);

    n = read(fd, buf, sizeof(buf) - 1u);
    if (n > 0) {
        buf[n] = '\0';
        existing = (uint32_t)strtoul(buf, NULL, 16);
    }

    capabilities |= existing;

    if (lseek(fd, 0, SEEK_SET) < 0
        || ftruncate(fd, 0) != 0) {
        (void)flock(fd, LOCK_UN);
        close(fd);
        return -1;
    }

    n = (ssize_t)snprintf(buf, sizeof(buf), "%08x\n", (unsigned int)capabilities);

    if (n > 0 && write(fd, buf, (size_t)n) != n) {
        (void)flock(fd, LOCK_UN);
        close(fd);
        return -1;
    }

    (void)fsync(fd);
    (void)flock(fd, LOCK_UN);
    close(fd);
    return 0;
}

/* ── End PID-keyed session implementation ─────────────────────────────────── */

int ksi_permissions_create(ksi_permission_store **store)
{
    ksi_permission_store *created;

    if (store == NULL) {
        return -1;
    }

    created = calloc(1, sizeof(*created));

    if (created == NULL) {
        return -1;
    }

    created->path = resolve_store_path();

    if (created->path == NULL) {
        free(created);
        return -1;
    }

    if (load_store(created) != 0) {
        ksi_permissions_destroy(created);
        return -1;
    }

    *store = created;
    return 0;
}

void ksi_permissions_destroy(ksi_permission_store *store)
{
    if (store == NULL) {
        return;
    }

    for (size_t i = 0; i < store->count; i++) {
        free_record(&store->records[i]);
    }

    free(store->records);
    free(store->path);
    free(store);
}

int ksi_permissions_identify_process(
    pid_t pid,
    char *path_buffer,
    size_t path_buffer_size,
    char *command_line_buffer,
    size_t command_line_buffer_size,
    char hash_buffer[KSI_PERMISSION_HASH_HEX_LENGTH + 1u],
    char *wildcard_hash_buffer)
{
    char proc_path[64];
    int fd;
    ssize_t path_length;

    if (path_buffer == NULL || path_buffer_size == 0u
        || command_line_buffer == NULL || command_line_buffer_size == 0u
        || hash_buffer == NULL || pid <= 0) {
        return -1;
    }

    path_buffer[0] = '\0';
    command_line_buffer[0] = '\0';
    hash_buffer[0] = '\0';

    if (wildcard_hash_buffer != NULL)
        wildcard_hash_buffer[0] = '\0';

    (void)snprintf(proc_path, sizeof(proc_path), "/proc/%ld/exe", (long)pid);
    fd = open(proc_path, O_RDONLY | O_CLOEXEC);

    if (fd < 0) {
        return -1;
    }

    path_length = readlink(proc_path, path_buffer, path_buffer_size - 1u);

    if (path_length < 0) {
        close(fd);
        return -1;
    }

    path_buffer[path_length] = '\0';

    if (is_protected_exe(path_buffer)) {
        /* Fast path: root-owned, non-world-writable — only a privileged actor can
         * replace this binary, so the path (not its content) is a safe, stable
         * exe identity; skipping the content read eliminates the O(filesize)
         * startup cost and lets records survive package updates. The command line
         * is folded in too so each script (its path in argv) is prompted for
         * separately — but note that is per-invocation granularity, NOT a security
         * boundary: cmdline is process-forgeable, so the enforced separation is
         * (uid, exe) only (see hash_protected_path_identity). */
        close(fd);
        return hash_protected_path_identity(
            path_buffer, pid, command_line_buffer, command_line_buffer_size, hash_buffer, wildcard_hash_buffer);
    }

    if (hash_process_identity(fd, pid, command_line_buffer, command_line_buffer_size, hash_buffer, wildcard_hash_buffer) != 0) {
        close(fd);
        return -1;
    }

    close(fd);
    return 0;
}

uint32_t ksi_permissions_get_allowed_capabilities(
    const ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash)
{
    ssize_t index;
    const ksi_permission_record *record;

    if (store == NULL || exe_hash == NULL) {
        return 0u;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index < 0) {
        return 0u;
    }

    record = &store->records[index];
    return record->persistent_allowed_capabilities | record->session_allowed_capabilities;
}

void ksi_permissions_note_seen(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    const char *exe_path)
{
    ssize_t index;
    ksi_permission_record *record;

    if (store == NULL || exe_hash == NULL) {
        return;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index < 0) {
        return;
    }

    record = &store->records[index];
    record->last_seen_utc = utc_now_seconds();

    if (exe_path != NULL && exe_path[0] != '\0') {
        char *copy = strdup(exe_path);

        if (copy != NULL) {
            free(record->exe_path);
            record->exe_path = copy;
        }
    }

    if (record->persistent_allowed_capabilities != 0u) {
        (void)prune_expired_records(store);
        (void)save_store(store);
    }
}

int ksi_permissions_grant_session(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    const char *exe_path,
    uint32_t capabilities)
{
    ksi_permission_record *record;

    if (store == NULL || capabilities == 0u) {
        return -1;
    }

    record = get_or_add_record(store, uid, exe_hash, exe_path);

    if (record == NULL) {
        return -1;
    }

    record->session_allowed_capabilities |= capabilities;
    return 0;
}

int ksi_permissions_grant_persistent(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    const char *exe_path,
    uint32_t capabilities)
{
    ksi_permission_record *record;

    if (store == NULL || capabilities == 0u) {
        return -1;
    }

    record = get_or_add_record(store, uid, exe_hash, exe_path);

    if (record == NULL) {
        return -1;
    }

    record->persistent_allowed_capabilities |= capabilities;
    record->session_allowed_capabilities |= capabilities;
    /* Granting clears any prior denial for the same caps so the user's most
     * recent decision wins. */
    record->persistent_denied_capabilities &= ~capabilities;
    (void)prune_expired_records(store);
    return save_store(store);
}

uint32_t ksi_permissions_get_denied_capabilities(
    const ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash)
{
    ssize_t index;

    if (store == NULL || exe_hash == NULL) {
        return 0u;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index < 0) {
        return 0u;
    }

    return store->records[index].persistent_denied_capabilities;
}

int ksi_permissions_deny_persistent(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    const char *exe_path,
    uint32_t capabilities)
{
    ksi_permission_record *record;

    if (store == NULL || capabilities == 0u) {
        return -1;
    }

    record = get_or_add_record(store, uid, exe_hash, exe_path);

    if (record == NULL) {
        return -1;
    }

    record->persistent_denied_capabilities |= capabilities;
    /* A persistent deny supersedes any prior allow for the same caps. */
    record->persistent_allowed_capabilities &= ~capabilities;
    record->session_allowed_capabilities &= ~capabilities;
    (void)prune_expired_records(store);
    return save_store(store);
}

int ksi_permissions_clear_persistent(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    uint32_t capabilities)
{
    ssize_t index;
    ksi_permission_record *record;

    if (store == NULL || exe_hash == NULL || capabilities == 0u) {
        return -1;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index < 0) {
        return 0;
    }

    record = &store->records[index];
    record->persistent_allowed_capabilities &= ~capabilities;
    record->persistent_denied_capabilities &= ~capabilities;
    record->session_allowed_capabilities &= ~capabilities;
    (void)prune_expired_records(store);
    return save_store(store);
}

int ksi_permissions_clear_all(
    ksi_permission_store *store,
    uid_t uid,
    uint32_t capabilities)
{
    bool changed = false;
    size_t i;

    if (store == NULL || capabilities == 0u) {
        return -1;
    }

    for (i = 0; i < store->count; i++) {
        ksi_permission_record *record = &store->records[i];

        if (uid != (uid_t)-1 && record->uid != uid) {
            continue;
        }

        record->persistent_allowed_capabilities &= ~capabilities;
        record->persistent_denied_capabilities &= ~capabilities;
        record->session_allowed_capabilities &= ~capabilities;
        changed = true;
    }

    if (!changed) {
        return 0;
    }

    (void)prune_expired_records(store);
    return save_store(store);
}

int ksi_permissions_clear_session(
    ksi_permission_store *store,
    uid_t uid,
    const char *exe_hash,
    uint32_t capabilities)
{
    ssize_t index;

    if (store == NULL || exe_hash == NULL || capabilities == 0u) {
        return -1;
    }

    index = find_record_index(store, uid, exe_hash);

    if (index < 0) {
        return 0;
    }

    store->records[index].session_allowed_capabilities &= ~capabilities;
    return 0;
}

void ksi_permissions_for_each(
    const ksi_permission_store *store,
    uid_t uid_filter,
    ksi_permissions_visit_fn visit,
    void *user_data)
{
    if (store == NULL || visit == NULL) {
        return;
    }

    for (size_t i = 0; i < store->count; i++) {
        const ksi_permission_record *record = &store->records[i];
        ksi_permission_entry entry;

        if (uid_filter != (uid_t)-1 && record->uid != uid_filter) {
            continue;
        }

        memset(&entry, 0, sizeof(entry));
        entry.uid = record->uid;
        (void)snprintf(entry.exe_hash, sizeof(entry.exe_hash), "%s", record->exe_hash);
        entry.exe_path = record->exe_path;
        entry.persistent_allowed_capabilities = record->persistent_allowed_capabilities;
        entry.persistent_denied_capabilities = record->persistent_denied_capabilities;
        entry.last_seen_utc = record->last_seen_utc;

        if (!visit(&entry, user_data)) {
            return;
        }
    }
}

ksi_permission_decision ksi_permissions_prompt(
    pid_t pid,
    uid_t uid,
    gid_t gid,
    const char *exe_path,
    const char *command_line,
    const char *exe_hash,
    uint32_t capabilities)
{
    char capability_text[512];
    char prompt_text[4096];
    char display_path[512];
    char display_args[512];

    wrap_text(
        exe_path != NULL && exe_path[0] != '\0' ? exe_path : "(unknown)",
        72u, display_path, sizeof(display_path));
    wrap_text(
        command_line != NULL && command_line[0] != '\0' ? command_line : "(none)",
        72u, display_args, sizeof(display_args));

    describe_capabilities(capabilities, capability_text, sizeof(capability_text));
    (void)snprintf(
        prompt_text,
        sizeof(prompt_text),
        "An application is requesting sensitive system capabilities access.\n\n"
        "Executable:\n%s\n\n"
        "Arguments:\n%s\n\n"
        "Permission identity hash:\n%s\n\n"
        "Requested access:\n%s\n"
        "Only allow applications you trust.",
        display_path,
        display_args,
        exe_hash != NULL && exe_hash[0] != '\0' ? exe_hash : "(unknown)",
        capability_text[0] != '\0' ? capability_text : "- No capabilities requested\n");

    {
        static const char *zenity_paths[] = {
            "/usr/bin/zenity",
            "/bin/zenity",
            "/run/current-system/sw/bin/zenity",
            NULL
        };
        static const char *kdialog_paths[] = {
            "/usr/bin/kdialog",
            "/bin/kdialog",
            "/run/current-system/sw/bin/kdialog",
            NULL
        };

        /* Simple two-button prompt: [Allow] / [Deny]. Allow grants access to this
         * EXECUTABLE persistently ("always allow this program" — the wildcard
         * identity, so it survives restart and covers every script the binary
         * runs). Deny / window-close / Esc is SESSION-ONLY: it denies now and a
         * re-run of the app prompts again (see process_client_prompt_done and
         * keysharp-helper). A question dialog auto-sizes to its text, so there is
         * no listbox height to tune. */
        for (size_t i = 0; zenity_paths[i] != NULL; i++) {
            char title_arg[] = "--title=Capability permissions";
            char text_arg[sizeof(prompt_text) + 8u];
            char *argv[] = {
                "zenity",
                "--question",
                title_arg,
                text_arg,
                "--width=640",
                "--ok-label=Allow",
                "--cancel-label=Deny",
                NULL,
            };

            (void)snprintf(text_arg, sizeof(text_arg), "--text=%s", prompt_text);

            int rc = run_prompt_command(zenity_paths[i], argv, pid, uid, gid, NULL, 0);

            if (rc < 0) {
                /* Could not launch this zenity path; try the next, then kdialog. */
                continue;
            }

            /* Launched: commit to its answer (do NOT cascade to another backend).
             * Exit 0 = Allow; any other exit (Deny button, close, Esc) = Deny. */
            return rc == 0
                ? KSI_PERMISSION_DECISION_ALLOW_ALL_SCRIPTS
                : KSI_PERMISSION_DECISION_DENY;
        }

        for (size_t i = 0; kdialog_paths[i] != NULL; i++) {
            char *argv[] = {
                "kdialog",
                "--title",
                "Capability permissions",
                "--yesno",
                prompt_text,
                "--yes-label",
                "Allow",
                "--no-label",
                "Deny",
                NULL,
            };

            int rc = run_prompt_command(kdialog_paths[i], argv, pid, uid, gid, NULL, 0);

            if (rc < 0) {
                continue;  /* this kdialog path could not be launched; try the next */
            }

            return rc == 0
                ? KSI_PERMISSION_DECISION_ALLOW_ALL_SCRIPTS
                : KSI_PERMISSION_DECISION_DENY;
        }
    }

    fprintf(stderr, "keysharp-trust: permission prompt unavailable for pid=%ld uid=%ld\n", (long)pid, (long)uid);
    return KSI_PERMISSION_DECISION_PROMPT_UNAVAILABLE;
}
