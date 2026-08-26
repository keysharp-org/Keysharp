#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include "keysharp_inputd/daemon.h"

#include "connection_ref.h"
#include "active_session.h"
#include "keysharp_inputd/ipc.h"
#include "keysharp_inputd/linux_devices.h"
#include "keysharp_inputd/linux_synth.h"
#include "keysharp_trust/permissions.h"
#include "keysharp_inputd/platform.h"
#include "keysharp_inputd/protocol.h"
#include "keysharp_inputd/synthetic_hooks.h"
#include "pipe_ring.h"
#include "worker_pool.h"
#include "wake_pipe.h"

#include <errno.h>
#include <fcntl.h>
#include <linux/input-event-codes.h>
#include <poll.h>
#include <pthread.h>
#include <signal.h>
#include <stdbool.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sched.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

_Static_assert(KSI_CAP_HOOK_KEYBOARD == KST_CAP_INPUT_HOOK_KEYBOARD, "trust/input capability mismatch");
_Static_assert(KSI_CAP_HOOK_MOUSE == KST_CAP_INPUT_HOOK_MOUSE, "trust/input capability mismatch");
_Static_assert(KSI_CAP_SYNTH_KEYBOARD == KST_CAP_INPUT_SYNTH_KEYBOARD, "trust/input capability mismatch");
_Static_assert(KSI_CAP_SYNTH_MOUSE == KST_CAP_INPUT_SYNTH_MOUSE, "trust/input capability mismatch");
_Static_assert(KSI_CAP_BLOCK_INPUT == KST_CAP_INPUT_BLOCK, "trust/input capability mismatch");

#define KSI_MAX_CLIENTS 64
#define KSI_MAX_BACKEND_FDS 160
#define KSI_MAX_POLL_FDS (3 + KSI_MAX_BACKEND_FDS + KSI_MAX_CLIENTS)
#define KSI_MAX_PENDING_COMMANDS 256
#define KSI_MAX_SYNTH_INPUTS 1024
#define KSI_MAX_MODIFY_INPUTS KSI_MAX_SYNTH_INPUTS
#define KSI_HOOK_DECISION_TIMEOUT_MS 1000u
/* Per-lane consecutive hook failures before the client is disconnected. */
#define KSI_MAX_CONSECUTIVE_HOOK_FAILURES 5u
#define KSI_MAX_LANE_ACTIONS 512u
/* Cap on read() iterations per client per poll pass. Without it a client that
 * streams unanswered frames (e.g. one-way HEARTBEATs) keeps the evdev-reader
 * thread in the client read loop, starving physical-device ingestion — the
 * device plane already has KSI_MAX_DEVICE_EVENTS_PER_PASS. 8 x 64KB is generous
 * for legitimate bursts; the common case still exits on the first short read. */
#define KSI_MAX_CLIENT_READS_PER_PASS 8
#define KSI_MAX_OUTPUT_ACTIONS 4096u
#define KSI_OUTPUT_CLEANUP_RESERVE KSI_MAX_SYNTH_INPUTS
#define KSI_MAX_SYNTH_HOOK_ACTIONS 4096u
#define KSI_ACTIVE_SESSION_REFRESH_MS 100u
#define KSI_ACTIVE_SESSION_RESOLVER_RETRY_MS 5000u

#define KSI_MAX_OUTPUT_SYNTH_BYTES (1024u * 1024u)
#define KSI_OUTPUT_CLEANUP_BYTES ((size_t)KSI_MAX_SYNTH_INPUTS * sizeof(ksi_input))
#define KSI_MAX_SYNTH_HOOK_STEPS_PER_PASS 256u
#define KSI_MAX_RECURSION_DEPTH 32u
#define KSI_SHUTDOWN_TIMEOUT_MS 5000u
#define KSI_GRAB_LEASE_TIMEOUT_MS 15000u
/* CLIENT_HELLO deadline; peers that connect and send nothing cannot pin slots. */
#define KSI_HANDSHAKE_TIMEOUT_MS 10000u
/* Idle deadline for authenticated-but-capability-less connections (no grants, no
 * hook/block subscriptions). Reset by any message, so a live query channel that
 * polls within this window is never reaped; only truly-idle capless connections
 * that would otherwise pin a slot forever are dropped. */
#define KSI_CAPLESS_IDLE_TIMEOUT_MS 300000u
/* Minimum spacing between honored client-forced permission prompts. */
#define KSI_FORCED_PROMPT_COOLDOWN_MS 3000u
/* Reserve a few client slots for other users/root helpers on shared systems. */
#define KSI_MAX_CLIENTS_PER_UID (KSI_MAX_CLIENTS - 8u)
#define KSI_VK_LCONTROL 0xA2u

static volatile sig_atomic_t keep_running = 1;

/* Fail open if a safety-critical notification cannot reach the main thread.
 * Retaining a stale grab or BlockInput mask is worse than dropping hooks. */
static atomic_int g_fail_open_requested = 0;

/* Self-pipe wake; EAGAIN/EPIPE are harmless because readers drain eagerly. */
static inline void wake_pipe_write(int fd)
{
    uint8_t byte = 1;
    ssize_t written = write(fd, &byte, sizeof(byte));
    (void)written;
}

/* Two SEPARATE pools, not one shared pool, so a permission prompt -- which
 * blocks on the user for as long as they take to answer, potentially
 * indefinitely -- can never starve process-identity resolution for an
 * unrelated client out of a worker thread. Both connecting-client identity
 * resolution (fast, frequent) and permission prompts (slow, rare, blocks on a
 * human) used to share g_worker_pool's fixed thread count; two prompts alone
 * could fill it and stall every other client's CLIENT_HELLO indefinitely. */
static ksi_worker_pool g_worker_pool;
static ksi_worker_pool g_identify_worker_pool;

typedef enum ksi_client_state {
    KSI_CLIENT_STATE_IDENTIFYING,     /* process identity resolution running on worker thread */
    KSI_CLIENT_STATE_READY,           /* identity known, waiting for or able to process CLIENT_HELLO */
    KSI_CLIENT_STATE_AWAITING_PROMPT, /* permission prompt running on worker thread */
} ksi_client_state;

/* Main-thread-only: holds application state for one authenticated client. */
typedef struct ksi_client {
    int fd;
    ksi_hook_send_ref *hook_send_ref;
    uint8_t *rx_buffer;
    size_t rx_used;
    uint64_t connection_id;
    pid_t pid;
    uid_t uid;
    gid_t gid;
    uint64_t start_time;   /* field 22 of /proc/<pid>/stat; 0 if not yet known */
    /* Process start time captured at accept, compared against the value the
     * identity worker reads later; a mismatch means the pid was recycled between
     * accept and identify, so /proc no longer describes the connecting process. */
    uint64_t accept_start_time;
    ksi_client_state state;
    bool identity_attempted;
    bool has_identity;
    bool authenticated;
    bool role_initialized;
    uint32_t connection_role;
    /* Set by CLIENT_HELLO; missing handshakes are dropped after the deadline. */
    bool hello_seen;
    uint64_t connected_at_ms;
    uint32_t granted_capabilities;
    /* Session-only "Deny" bits for THIS connection (the managed side keeps one connection
     * per Keysharp run): suppress re-prompting for the rest of the run WITHOUT persisting to
     * disk, so a mis-clicked Deny recovers on the next run. Never serialized; dies with the fd. */
    uint32_t session_denied_capabilities;
    uint32_t hook_subscriptions;
    /* Windows installs each hook type independently at the chain head. A
     * disconnect/re-subscribe therefore gets a fresh per-type ordinal even if
     * its transport connection is older than other scripts. */
    uint64_t hook_subscription_ordinal[2];
    uint32_t block_input_mask;
    /* Consecutive hook failures per lane: index 0 keyboard, 1 mouse. */
    uint32_t consecutive_hook_failures[2];
    uint32_t quarantined_hooks;
    uint64_t quarantine_rearm_after_ms[2];
    uint64_t last_hook_quarantine_ms[2];
    uint64_t lease_expires_ms;
    /* Last time any message was received; drives the capless-idle reaper so an
     * authenticated but capability-less connection cannot pin a slot forever. */
    uint64_t last_activity_ms;
    char exe_path[KSI_PERMISSION_MAX_PATH];
    char command_line[KSI_PERMISSION_MAX_COMMAND_LINE];
    char exe_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    /* WILDCARD identity (exe portion only, no argv): keys the "Allow for all scripts" grant that
     * covers every script run by this same Keysharp binary. Empty when identity is unknown. */
    char wildcard_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    /* Buffered CLIENT_HELLO waiting for identification or prompt to complete. */
    bool pending_hello_valid;
    uint32_t pending_hello_requested;
    uint32_t pending_hello_flags;
    uint64_t pending_hello_correlation_id;
    uint32_t pending_hello_client_id;
} ksi_client;

/* Heap-allocated payload for KSI_DAEMON_COMMAND_CLIENT_IDENTIFIED. */
typedef struct ksi_client_identified_result {
    bool has_identity;
    char exe_path[KSI_PERMISSION_MAX_PATH];
    char command_line[KSI_PERMISSION_MAX_COMMAND_LINE];
    char exe_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    char wildcard_hash[KSI_PERMISSION_HASH_HEX_LENGTH + 1u];
    uint64_t start_time;
} ksi_client_identified_result;

typedef enum ksi_daemon_command_type {
    KSI_DAEMON_COMMAND_CLIENT_IDENTIFIED, /* worker -> main: identity resolution complete */
    KSI_DAEMON_COMMAND_CLIENT_PROMPT_DONE, /* worker → main: user permission prompt complete */
    KSI_DAEMON_COMMAND_LANE_HOOK_FAILURE, /* lane → main: send/timeout failure for a client */
} ksi_daemon_command_type;

typedef struct ksi_daemon_command {
    ksi_daemon_command_type type;
    int client_fd;
    uint64_t connection_id;
    union {
        struct {
            ksi_client_identified_result *result; /* heap-allocated; freed by consumer */
        } identified;
        struct {
            ksi_permission_decision decision;
            uint32_t requested_capabilities;
            uint32_t missing_capabilities;
            /* state->available_capabilities as it was WHEN THE PROMPT STARTED,
             * not when it completes. A permission prompt can sit open for
             * seconds; using the current (post-wait) availability here would
             * let a transient hotplug during that window silently shrink what
             * "success" means without ever surfacing an error. See
             * process_client_prompt_done. */
            uint32_t available_capabilities_at_start;
        } prompt_done;
        struct {
            uint32_t hook_type; /* KSI_HOOK_KEYBOARD_LL / KSI_HOOK_MOUSE_LL */
            uint64_t event_id;
            uint32_t nesting_depth;
            uint32_t elapsed_ms;
            uint32_t reason;
        } hook_failure;
    } data;
} ksi_daemon_command;

static void free_daemon_command(ksi_daemon_command *command);

/* --- Typed queues built on ksi_pipe_ring --- */

/* Worker/lane threads -> main thread. */
typedef struct ksi_daemon_command_queue { ksi_pipe_ring ring; } ksi_daemon_command_queue;

static int command_queue_init(ksi_daemon_command_queue *q)
{
    return q == NULL ? -1
        : ksi_pipe_ring_init(&q->ring, sizeof(ksi_daemon_command), KSI_MAX_PENDING_COMMANDS);
}

static void command_queue_destroy(ksi_daemon_command_queue *q)
{
    ksi_daemon_command cmd;

    if (q == NULL) {
        return;
    }

    /* Drain remaining items to free any heap-allocated frame buffers. */
    while (ksi_pipe_ring_pop(&q->ring, &cmd)) {
        free_daemon_command(&cmd);
    }

    ksi_pipe_ring_close(&q->ring);
}

/* Forward decls for types embedded in ksi_daemon_state.  Definitions and
 * helper functions live later in the file, alongside the sequencer thread. */
typedef enum ksi_output_action_type {
    KSI_OUTPUT_ACTION_REPLAY = 0,
    KSI_OUTPUT_ACTION_SYNTH,
    /* Release keys held by replay/synth, serialized with normal output. */
    KSI_OUTPUT_ACTION_RELEASE_ALL,
    /* Recreate a broken synthetic-output device. Runs the stop+start on the
     * output sequencer thread so it honors the single-thread invariant for the
     * uinput fds / key-down table instead of racing them from the main thread. */
    KSI_OUTPUT_ACTION_RECREATE_SYNTH,
} ksi_output_action_type;

typedef struct ksi_output_action {
    ksi_output_action_type type;
    /* Zero is reserved for daemon-internal safety actions. Client/physical
     * output is tagged with the active-seat generation which admitted it. */
    uint64_t input_generation;
    uint32_t hook_type;
    ksi_hook_event_payload replay_payload;
    uint32_t synth_flags;
    uint32_t synth_count;
    ksi_input *synth_inputs;
} ksi_output_action;

typedef struct ksi_output_queue {
    pthread_mutex_t mutex;
    bool mutex_initialized;
    /* Serializes the sequencer's epoch-check + backend write with a seat-owner
     * transition. Queue admission stays on mutex above, so ordinary producers
     * never wait for a potentially slow uinput write. */
    pthread_mutex_t execution_mutex;
    bool execution_mutex_initialized;
    ksi_output_action actions[KSI_MAX_OUTPUT_ACTIONS];
    size_t head;
    size_t count;
    size_t synth_bytes;
    int wake_read_fd;
    int wake_write_fd;
    struct ksi_daemon_state *state;
} ksi_output_queue;

typedef struct ksi_synth_completion {
    ksi_hook_send_ref *send_ref;
    struct ksi_daemon_state *state;
    uint32_t client_id;
    uint64_t correlation_id;
    atomic_uint remaining;
    _Atomic uint64_t terminal_result;
    bool owns_atomic_transaction;
    bool is_recursive_synthesis;
    uint64_t parent_hook_event_id;
} ksi_synth_completion;

typedef enum ksi_lane_egress_type {
    KSI_LANE_EGRESS_NONE = 0,
    KSI_LANE_EGRESS_REPLAY,
    KSI_LANE_EGRESS_SYNTH,
} ksi_lane_egress_type;

typedef struct ksi_lane_egress {
    ksi_lane_egress_type type;
    uint32_t synth_flags;
    uint32_t synth_count;
    ksi_input synth_inputs[1];
} ksi_lane_egress;

typedef struct ksi_synthetic_hook_item {
    ksi_input input;
    uint64_t queued_at_ns;
    ksi_synth_completion *completion;
    uint64_t input_generation;
    bool batch_start;  /* first node of a client batch; re-arms the surrogate reset */
} ksi_synthetic_hook_item;

typedef struct ksi_synthetic_hook_queue {
    pthread_mutex_t mutex;
    bool mutex_initialized;
    ksi_synthetic_hook_item items[KSI_MAX_SYNTH_HOOK_ACTIONS];
    size_t head;
    size_t count;
    int wake_read_fd;
    int wake_write_fd;
} ksi_synthetic_hook_queue;

typedef struct ksi_lane_subscriber {
    /* One HookStream is both transport and shared K/M hook-thread domain. */
    ksi_hook_send_ref *send_ref;
    uint64_t connection_id;
    uint64_t subscription_ordinal;
} ksi_lane_subscriber;

/* Immutable hook event snapshot. Lanes never touch state->clients[]. The
 * flexible tail stores only the subscribers which actually receive this event. */
typedef struct ksi_lane_event {
    uint64_t event_id;
    uint32_t hook_type;
    uint32_t generation;
    uint64_t input_generation;
    ksi_synth_completion *synthetic_completion;
    ksi_lane_egress egress;
    ksi_hook_event_payload payload;
    size_t payload_size;
    size_t subscriber_count;
    uint32_t nesting_depth;
    bool physical_input;
    ksi_lane_subscriber subscribers[];
} ksi_lane_event;

typedef struct ksi_nested_member {
    ksi_input input;
    ksi_lane_event *event;
} ksi_nested_member;

typedef struct ksi_nested_transaction {
    uint32_t count;
    uint32_t depth;
    uint32_t origin_generation;
    uint64_t input_generation;
    uint64_t origin_connection_id;
    uint64_t parent_hook_event_id;
    ksi_synth_completion *completion;
    ksi_nested_member members[];
} ksi_nested_transaction;

typedef struct ksi_nested_transaction_queue {
    ksi_pipe_ring ring;
} ksi_nested_transaction_queue;

/* Decision routed from the main thread into a lane thread. The globally unique
 * connection id identifies the subscriber; mismatched decisions are dropped. */
typedef struct ksi_lane_decision {
    uint64_t event_id;
    uint32_t decision;
    uint64_t responder_connection_id;
    uint32_t input_count;
    ksi_input inputs[KSI_MAX_MODIFY_INPUTS];
} ksi_lane_decision;

/* Bytes actually meaningful in a decision: the header plus input_count inputs.
 * The embedded inputs[] array is ~40KB; the common PASS decision uses 0 inputs,
 * so copying/clearing the whole struct per event is wasted memory traffic on the
 * hot path. Copy only the used prefix. All readers are bounded by input_count. */
static inline size_t ksi_lane_decision_size(uint32_t input_count)
{
    if (input_count > KSI_MAX_MODIFY_INPUTS) {
        input_count = KSI_MAX_MODIFY_INPUTS;
    }

    return offsetof(ksi_lane_decision, inputs) + (size_t)input_count * sizeof(ksi_input);
}

/* If a lane is full, new physical events bypass hooks and replay fail-open. */
typedef struct ksi_lane_action_queue {
    ksi_pipe_ring ring;
} ksi_lane_action_queue;

typedef struct ksi_lane_decision_queue {
    pthread_mutex_t mutex;
    pthread_cond_t condition;
    bool mutex_initialized;
    bool condition_initialized;
    bool has_decision;
    ksi_lane_decision decision;
} ksi_lane_decision_queue;

typedef struct ksi_hook_lane {
    uint32_t hook_type;
    pthread_t thread;
    bool thread_started;
    atomic_uint_least64_t current_event_id;  /* 0 = idle */
    atomic_uint_least64_t current_responder_connection_id;
    atomic_uint current_nesting_depth;
    /* Set by lane_shutdown to abort waits and drain promptly. */
    atomic_int shutting_down;
    /* Incremented by EmergencyPassthrough. Events captured under an older
     * generation skip subscriber callbacks and finalize immediately. */
    atomic_uint flush_generation;
    ksi_lane_action_queue action_queue;
    ksi_lane_decision_queue decision_queue;
    ksi_nested_transaction_queue nested_queue;
    struct ksi_daemon_state *state;
} ksi_hook_lane;

typedef struct ksi_daemon_state {
    const ksi_platform_backend *backend;
    ksi_client clients[KSI_MAX_CLIENTS];
    nfds_t client_count;
    ksi_daemon_command_queue *commands;
    ksi_permission_store *permissions;
    uint32_t available_capabilities;
    uint64_t next_connection_id;
    uint64_t next_event_id;
    uint64_t next_hook_subscription_ordinal[2];
    /* The system service is a single machine-wide transport, but only the
     * logind-active seat0 user is an input owner. Connections belonging to a
     * user who switched away remain open with their subscriptions stored, yet
     * are inert until that uid becomes active again. Manual per-user daemons do
     * not enable this gate; their Unix socket is already uid-private. */
    bool input_owner_enforced;
    atomic_bool active_input_uid_valid;
    atomic_uint active_input_uid;
    atomic_uint_least64_t active_input_generation;
    ksi_active_session_resolver active_session_resolver;
    bool active_session_resolver_initialized;
    uint64_t next_active_session_refresh_ms;
    uint64_t next_active_session_resolver_retry_ms;
    /* Single ordered uinput writer for replay, modify and SendInput output. */
    ksi_output_queue output_queue;
    ksi_synthetic_hook_queue synthetic_hook_queue;
    atomic_int synthetic_hook_inflight;
    atomic_uint active_synthetic_transactions;
    atomic_uint physical_hook_inflight;
    pthread_t sequencer_thread;
    bool sequencer_thread_started;
    atomic_int sequencer_running;
    /* Independent hook lanes; shared subscribers are serialized by hook_send_ref. */
    ksi_hook_lane keyboard_lane;
    ksi_hook_lane mouse_lane;
    /* Used to enqueue RELEASE_ALL on keyboard grab held->released. */
    bool keyboard_grab_active;
    bool interception_active;
    /* The main evdev reader needs real-time priority only while it owns an
     * interception grab. Idle observation remains ordinary SCHED_OTHER work. */
    bool reader_realtime;
    bool memory_locked;
    /* Per-uid FORCE_PROMPT throttle. A global timestamp let one uid on the shared
     * 0666 system socket suppress every other uid's forcePrompt re-ask; keying by
     * uid confines the cooldown to the spamming user. LRU-evict when full. */
    struct {
        uid_t uid;
        bool in_use;
        uint64_t last_ms;
    } forced_prompt_by_uid[16];
} ksi_daemon_state;

typedef struct ksi_binary_message_view {
    const ksi_message_header *header;
    const uint8_t *payload;
    size_t payload_size;
} ksi_binary_message_view;

static int update_grab_state(ksi_daemon_state *state);
static void prepare_requested_capabilities(ksi_daemon_state *state, uint32_t requested);
static void clear_hook_state(ksi_daemon_state *state);
static void record_client_hook_failure(
    ksi_daemon_state *state, nfds_t index, uint32_t hook_type,
    uint64_t event_id, uint32_t nesting_depth, uint32_t elapsed_ms,
    uint32_t reason);
static void lane_flush_passthrough(ksi_hook_lane *lane);
static bool output_queue_push_release_all(ksi_output_queue *q);
static bool input_owner_matches_uid(
    const ksi_daemon_state *state, uid_t uid);
static uint64_t current_input_generation(const ksi_daemon_state *state);

/* Per-hook-type failure-counter slot: 0 keyboard, 1 mouse. */
static size_t hook_type_to_lane_index(uint32_t hook_type)
{
    return hook_type == KSI_HOOK_MOUSE_LL ? 1u : 0u;
}
static void send_status(
    int client_fd,
    const ksi_message_header *request,
    uint32_t response_type,
    int32_t status,
    uint32_t detail);
static void remove_client(ksi_daemon_state *state, nfds_t index);
static void send_indicator_state_result(int client_fd, const ksi_message_header *request);
static void send_pointer_position_result(int client_fd, const ksi_message_header *request);
static void send_pointer_buttons_result(int client_fd, const ksi_message_header *request);
static void handle_get_key_state(
    ksi_daemon_state *state,
    ksi_client *client,
    const ksi_binary_message_view *message);
static bool set_realtime_priority(const char *thread_name);
static bool set_normal_priority(const char *thread_name);
static ssize_t find_client_index_by_fd(const ksi_daemon_state *state, int client_fd);
static ssize_t find_client_index_by_connection(
    const ksi_daemon_state *state,
    int client_fd,
    uint64_t connection_id);
static void process_client_identified(ksi_daemon_state *state, ksi_daemon_command *command);
static void process_client_prompt_done(ksi_daemon_state *state, ksi_daemon_command *command);

static void handle_signal(int signal_number)
{
    (void)signal_number;
    keep_running = 0;
}

static int install_signal_handlers(void)
{
    struct sigaction action;
    struct sigaction ignore_action;

    memset(&action, 0, sizeof(action));
    action.sa_handler = handle_signal;

    memset(&ignore_action, 0, sizeof(ignore_action));
    ignore_action.sa_handler = SIG_IGN;

    if (sigemptyset(&action.sa_mask) != 0) {
        return -1;
    }

    if (sigemptyset(&ignore_action.sa_mask) != 0) {
        return -1;
    }

    if (sigaction(SIGINT, &action, NULL) != 0) {
        return -1;
    }

    if (sigaction(SIGTERM, &action, NULL) != 0) {
        return -1;
    }

    if (sigaction(SIGPIPE, &ignore_action, NULL) != 0) {
        return -1;
    }

    return 0;
}

static void free_daemon_command(ksi_daemon_command *command)
{
    if (command == NULL) {
        return;
    }

    if (command->type == KSI_DAEMON_COMMAND_CLIENT_IDENTIFIED) {
        free(command->data.identified.result);
        command->data.identified.result = NULL;
    }
}

static bool input_owner_matches_uid(
    const ksi_daemon_state *state, uid_t uid)
{
    if (state == NULL) {
        return false;
    }

    if (!state->input_owner_enforced) {
        return true;
    }

    return atomic_load_explicit(
            &state->active_input_uid_valid, memory_order_acquire)
        && (uid_t)atomic_load_explicit(
            &state->active_input_uid, memory_order_relaxed) == uid;
}

static uint64_t current_input_generation(const ksi_daemon_state *state)
{
    return state == NULL
        ? 0u
        : atomic_load_explicit(
            &state->active_input_generation, memory_order_acquire);
}

#include "daemon/privilege_workers.inc"
#include "daemon/client_lifecycle.inc"
#include "daemon/hook_lanes.inc"
#include "daemon/grab_leases.inc"
#include "daemon/hook_dispatch.inc"
#include "daemon/hook_ingress.inc"
#include "daemon/protocol_server.inc"

static uint64_t advance_input_generation(ksi_daemon_state *state)
{
    uint64_t generation = atomic_fetch_add_explicit(
        &state->active_input_generation, 1u, memory_order_acq_rel) + 1u;

    if (generation == 0u) {
        generation = atomic_fetch_add_explicit(
            &state->active_input_generation, 1u, memory_order_acq_rel) + 1u;
    }

    return generation;
}

/* Change the sole input owner without tearing down either user's transport.
 * Invalid is published first, so every admission/filter path fails closed
 * throughout the transition. The generation fence makes lane/output work
 * captured under the old owner unexecutable even if a worker completes late. */
static void set_active_input_owner(
    ksi_daemon_state *state,
    bool valid,
    uid_t uid)
{
    bool old_valid;
    uid_t old_uid;
    uint64_t generation;

    if (state == NULL || !state->input_owner_enforced) {
        return;
    }

    old_valid = atomic_load_explicit(
        &state->active_input_uid_valid, memory_order_acquire);
    old_uid = (uid_t)atomic_load_explicit(
        &state->active_input_uid, memory_order_relaxed);

    if (old_valid == valid && (!valid || old_uid == uid)) {
        return;
    }

    atomic_store_explicit(
        &state->active_input_uid_valid, false, memory_order_release);
    generation = advance_input_generation(state);

    lane_flush_passthrough(&state->keyboard_lane);
    lane_flush_passthrough(&state->mouse_lane);
    synthetic_hook_queue_abort(&state->synthetic_hook_queue);
    /* If the sequencer already passed its epoch check, wait for that backend
     * write to finish while the owner is still invalid. Anything not yet in
     * flight will observe the new generation and be skipped. */
    output_queue_wait_for_inflight(&state->output_queue);

    /* No uid matches while invalid, so this releases every hook/BlockInput
     * grab without erasing stored subscriptions. */
    if (update_grab_state(state) != 0) {
        fprintf(stderr,
            "inputd: failed to release grabs during seat0 owner transition\n");
    }

    /* Consume evdev/libevdev backlog while the owner gate is invalid. The
     * callback deliberately emits nothing in this state, preventing an event
     * read under the former session from being stamped with the new epoch and
     * delivered to the newly active user's hooks. */
    ksi_linux_devices_drain_pending_input();

    output_queue_discard_stale(&state->output_queue, generation);

    if (!output_queue_push_release_all(&state->output_queue)) {
        fprintf(stderr,
            "inputd: failed to enqueue release-all during seat0 owner transition\n");
    }

    if (!valid) {
        fprintf(stderr,
            "inputd: seat0 has no active input owner; input IPC is fail-closed\n");
        return;
    }

    atomic_store_explicit(
        &state->active_input_uid, (unsigned int)uid, memory_order_relaxed);
    atomic_store_explicit(
        &state->active_input_uid_valid, true, memory_order_release);

    /* Resume stored subscriptions for this uid and give their leases a fresh
     * active interval; switched-away users' lease clocks remain paused. */
    for (nfds_t i = 0u; i < state->client_count; i++) {
        if (state->clients[i].uid == uid) {
            renew_client_lease(&state->clients[i]);
        }
    }

    if (update_grab_state(state) != 0) {
        fprintf(stderr,
            "inputd: failed to restore active seat0 user's grabs\n");
    }

    fprintf(stderr,
        "inputd: seat0 input owner uid=%ld generation=%llu\n",
        (long)uid, (unsigned long long)generation);
}

static void refresh_active_input_owner(
    ksi_daemon_state *state,
    bool force)
{
    uint64_t now;
    uid_t active_uid;

    if (state == NULL || !state->input_owner_enforced) {
        return;
    }

    now = monotonic_ms();

    if (!force && now < state->next_active_session_refresh_ms) {
        return;
    }

    state->next_active_session_refresh_ms = now + KSI_ACTIVE_SESSION_REFRESH_MS;

    if (!state->active_session_resolver_initialized
        && (force || now >= state->next_active_session_resolver_retry_ms)) {
        if (ksi_active_session_resolver_init(
                &state->active_session_resolver) == 0) {
            state->active_session_resolver_initialized = true;
        } else {
            state->next_active_session_resolver_retry_ms =
                now + KSI_ACTIVE_SESSION_RESOLVER_RETRY_MS;
            fprintf(stderr,
                "inputd: logind active-seat resolver unavailable (%s); "
                "input IPC remains fail-closed\n",
                strerror(errno));
        }
    }

    if (!state->active_session_resolver_initialized
        || ksi_active_session_resolver_get_uid(
            &state->active_session_resolver, &active_uid) != 0) {
        set_active_input_owner(state, false, (uid_t)-1);
        return;
    }

    set_active_input_owner(state, true, active_uid);
}

/* Runs the backend's periodic maintenance, then two follow-ups the daemon owns:
 *   1. If the backend reports its synthetic-output device has failed, enqueue a
 *      recreate action so the stop+start runs on the OUTPUT SEQUENCER thread
 *      (not here on the main thread, which would race the sequencer's writes).
 *   2. If the set of honorable capabilities changed (hotplug, or synth
 *      dying/recovering), re-run update_grab_state so a dead synth releases its
 *      hook grabs (fail open) and a recovered one re-grabs. Gating on an actual
 *      availability change -- rather than every tick -- avoids thrashing grabs. */
static void run_backend_maintenance(ksi_daemon_state *state)
{
    const ksi_platform_backend *backend;
    uint32_t previous_caps;
    uint32_t new_caps;

    if (state == NULL || (backend = state->backend) == NULL) {
        return;
    }

    retry_quarantined_hooks(state);

    if (backend->periodic_maintenance != NULL) {
        backend->periodic_maintenance();
    }

    if (backend->synth_needs_recovery != NULL
        && backend->synth_needs_recovery()
        && !output_queue_push_recreate_synth(&state->output_queue)) {
        fprintf(stderr, "inputd: failed to enqueue synthetic output device recovery\n");
    }

    previous_caps = state->available_capabilities;
    new_caps = daemon_available_capabilities(backend);
    state->available_capabilities = new_caps;

    if (new_caps != previous_caps && update_grab_state(state) != 0) {
        fprintf(stderr, "inputd: failed to refresh grabs after capability change\n");
    }
}

int ksi_daemon_run(const ksi_daemon_options *options)
{
    ksi_ipc_server *server = NULL;
    const ksi_platform_backend *backend = ksi_platform_backend_get();
    ksi_permission_store *permissions = NULL;
    ksi_daemon_command_queue command_queue;

    if (options == NULL || (!options->system_service && options->socket_path == NULL)) {
        fprintf(stderr, "daemon options are invalid\n");
        return 2;
    }

    if (install_signal_handlers() != 0) {
        fprintf(stderr, "failed to install signal handlers: %s\n", strerror(errno));
        return 1;
    }

    if (backend->start() != 0) {
        fprintf(stderr, "failed to start %s input backend\n", backend->name);
        return 1;
    }

    /* Snapshot capabilities based on what the service opened successfully. */
    uint32_t available_capabilities = daemon_available_capabilities(backend);

    if (ksi_permissions_create(&permissions) != 0) {
        fprintf(stderr,
            "inputd: warning: failed to initialize permissions store; "
            "all capability requests will be denied\n");
        permissions = NULL;
    }

    if ((options->system_service
            ? ksi_ipc_server_from_fd(3, &server)
            : ksi_ipc_server_open(options->socket_path, &server)) != 0) {
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    fprintf(stderr, "keysharp-inputd listening on %s using %s backend\n",
        options->system_service ? "systemd socket" : options->socket_path,
        backend->name);

    ksi_daemon_state *daemon_state = calloc(1, sizeof(*daemon_state));

    if (daemon_state == NULL) {
        fprintf(stderr, "inputd: failed to allocate daemon state\n");
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    daemon_state->backend = backend;
    daemon_state->commands = &command_queue;
    daemon_state->permissions = permissions;
    daemon_state->available_capabilities = available_capabilities;
    daemon_state->next_connection_id = 1;
    daemon_state->next_event_id = 1;
    daemon_state->input_owner_enforced = options->system_service;
    atomic_init(&daemon_state->active_input_uid_valid, false);
    atomic_init(&daemon_state->active_input_uid, 0u);
    atomic_init(&daemon_state->active_input_generation, 1u);

    if (command_queue_init(&command_queue) != 0) {
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    if (output_queue_init(&daemon_state->output_queue, daemon_state) != 0) {
        command_queue_destroy(&command_queue);
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    if (synthetic_hook_queue_init(&daemon_state->synthetic_hook_queue) != 0) {
        output_queue_close(&daemon_state->output_queue);
        command_queue_destroy(&command_queue);
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    atomic_store(&daemon_state->synthetic_hook_inflight, 0);

    /* Sequencer thread must be running before the hook callback fires for the
     * first time so the queue always has a draining consumer. */
    atomic_store(&daemon_state->sequencer_running, 1);

    if (pthread_create(&daemon_state->sequencer_thread, NULL,
            output_sequencer_thread_main, daemon_state) != 0) {
        atomic_store(&daemon_state->sequencer_running, 0);
        synthetic_hook_queue_close(&daemon_state->synthetic_hook_queue);
        output_queue_close(&daemon_state->output_queue);
        command_queue_destroy(&command_queue);
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    daemon_state->sequencer_thread_started = true;

    /* Initialize and start both hook lanes before the backend can start
     * delivering events.  Errors here unwind the sequencer thread cleanly. */
    if (lane_init(&daemon_state->keyboard_lane, daemon_state, KSI_HOOK_KEYBOARD_LL) != 0
        || lane_init(&daemon_state->mouse_lane, daemon_state, KSI_HOOK_MOUSE_LL) != 0
        || lane_start(&daemon_state->keyboard_lane) != 0
        || lane_start(&daemon_state->mouse_lane) != 0) {
        fprintf(stderr, "inputd: failed to start hook lanes\n");
        (void)lane_shutdown(&daemon_state->keyboard_lane, 0u);
        (void)lane_shutdown(&daemon_state->mouse_lane, 0u);
        atomic_store(&daemon_state->sequencer_running, 0);
        output_queue_wake(&daemon_state->output_queue);
        pthread_join(daemon_state->sequencer_thread, NULL);
        daemon_state->sequencer_thread_started = false;
        synthetic_hook_queue_close(&daemon_state->synthetic_hook_queue);
        output_queue_close(&daemon_state->output_queue);
        command_queue_destroy(&command_queue);
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    if (backend->set_hook_event_callback != NULL) {
        backend->set_hook_event_callback(daemon_handle_hook_event, daemon_state);
    }

    ksi_linux_devices_set_physical_key_event_callback(
        daemon_handle_physical_key_event, daemon_state);

    if (ksi_worker_pool_init(&g_worker_pool) != 0
        || ksi_worker_pool_init(&g_identify_worker_pool) != 0) {
        fprintf(stderr, "inputd: failed to start worker pool\n");
        ksi_worker_pool_destroy(&g_worker_pool);
        ksi_worker_pool_destroy(&g_identify_worker_pool);

        if (backend->set_hook_event_callback != NULL) {
            backend->set_hook_event_callback(NULL, NULL);
        }

        ksi_linux_devices_set_physical_key_event_callback(NULL, NULL);

        (void)lane_shutdown(&daemon_state->keyboard_lane, 0u);
        (void)lane_shutdown(&daemon_state->mouse_lane, 0u);
        atomic_store(&daemon_state->sequencer_running, 0);
        output_queue_wake(&daemon_state->output_queue);
        pthread_join(daemon_state->sequencer_thread, NULL);
        daemon_state->sequencer_thread_started = false;
        synthetic_hook_queue_close(&daemon_state->synthetic_hook_queue);
        output_queue_close(&daemon_state->output_queue);
        command_queue_destroy(&command_queue);
        free(daemon_state);
        ksi_ipc_server_close(server);
        ksi_permissions_destroy(permissions);
        backend->stop();
        return 1;
    }

    /* This thread also writes request/response frames to clients. Bound how long a
     * non-reading client can stall physical-input dispatch: tear its reply stream
     * down after ~10ms of backpressure instead of the default 100ms. Lane/worker
     * threads keep the full budget (their writes never gate device ingestion). */
    ksi_ipc_set_write_drain_budget_ms(10);

    /* The shared system socket is closed to all input clients until logind
     * supplies the active seat0 uid. Manual per-user daemons skip this gate. */
    refresh_active_input_owner(daemon_state, true);


    while (keep_running) {
        struct pollfd fds[KSI_MAX_POLL_FDS];
        nfds_t count = 0;
        nfds_t synthetic_index;
        nfds_t server_index;
        nfds_t backend_start;
        nfds_t backend_count;
        nfds_t client_start;
        nfds_t polled_client_count;

        memset(fds, 0, sizeof(fds));
        fds[count].fd = ksi_pipe_ring_wake_fd(&command_queue.ring);
        fds[count].events = POLLIN;
        count++;

        synthetic_index = count;
        fds[count].fd = daemon_state->synthetic_hook_queue.wake_read_fd;
        fds[count].events = POLLIN;
        count++;

        server_index = count;
        fds[count].fd = ksi_ipc_server_fd(server);
        fds[count].events = POLLIN;
        count++;

        backend_start = count;
        backend_count = backend->poll_fds == NULL
            ? 0
            : backend->poll_fds(&fds[count], KSI_MAX_POLL_FDS - count);
        count += backend_count;

        client_start = count;
        polled_client_count = daemon_state->client_count;

        for (nfds_t i = 0; i < polled_client_count; i++) {
            fds[count].fd = daemon_state->clients[i].fd;
            fds[count].events = POLLIN;
            count++;
        }

        /* Lane threads own hook-decision deadlines. */
        int poll_result = poll(fds, count,
            daemon_state->input_owner_enforced
                ? (int)KSI_ACTIVE_SESSION_REFRESH_MS
                : 1000);

        if (poll_result < 0) {
            if (errno == EINTR) {
                continue;
            }

            fprintf(stderr, "poll failed: %s\n", strerror(errno));
            break;
        }

        /* Publish any seat transition before accepting clients, reading
         * requests, or ingesting backend events from this poll pass. */
        refresh_active_input_owner(daemon_state, false);

        if (poll_result == 0) {
            (void)process_hook_ingress(daemon_state);
            apply_fail_open_if_requested(daemon_state);
            process_daemon_commands(daemon_state);
            expire_client_leases(daemon_state);
            expire_unauthenticated_clients(daemon_state);
            run_backend_maintenance(daemon_state);

            continue;
        }

        if ((fds[0].revents & POLLIN) != 0) {
            ksi_pipe_ring_drain_wake(&command_queue.ring);
        }

        if ((fds[synthetic_index].revents & POLLIN) != 0) {
            ksi_wake_pipe_drain(daemon_state->synthetic_hook_queue.wake_read_fd);
        }

        if ((fds[server_index].revents & POLLIN) != 0) {
            for (size_t accepted = 0; accepted < KSI_MAX_CLIENTS
                && accept_client(daemon_state, server, options->system_service);
                accepted++) {
            }
        }

        for (nfds_t i = polled_client_count; i > 0; i--) {
            nfds_t index = i - 1u;
            short revents = fds[client_start + index].revents;

            if ((revents & (POLLHUP | POLLERR | POLLNVAL)) != 0
                || ((revents & POLLIN) != 0
                    && read_client_frames_direct(daemon_state, &daemon_state->clients[index]) <= 0)) {
                remove_client(daemon_state, index);
            }
        }

        (void)process_hook_ingress(daemon_state);

        for (nfds_t i = backend_start; i < backend_start + backend_count; i++) {
            if ((fds[i].revents & (POLLIN | POLLHUP | POLLERR | POLLNVAL)) != 0
                && backend->process_fd != NULL) {
                backend->process_fd(fds[i].fd);
            }
        }

        (void)process_hook_ingress(daemon_state);

        /* Runs backend maintenance, requests synth recovery on the sequencer if
         * needed, refreshes available_capabilities (hotplug can change physical
         * hook/block capabilities) and re-applies grabs when availability changes. */
        run_backend_maintenance(daemon_state);

        apply_fail_open_if_requested(daemon_state);
        process_daemon_commands(daemon_state);
        expire_client_leases(daemon_state);
        expire_unauthenticated_clients(daemon_state);

    }

    keep_running = 0;

    if (backend->set_hook_event_callback != NULL) {
        backend->set_hook_event_callback(NULL, NULL);
    }

    ksi_linux_devices_set_physical_key_event_callback(NULL, NULL);

    /* Release grabs before any shutdown step that may block. */
    clear_hook_state(daemon_state);

    if (update_grab_state(daemon_state) != 0) {
        fprintf(stderr, "inputd: failed to release grabs before shutdown waits\n");
    }

    uint64_t shutdown_deadline_ms = monotonic_ms() + KSI_SHUTDOWN_TIMEOUT_MS;

    /* Queued physical events finalize as PASS during lane shutdown. */
    if (!lane_shutdown(&daemon_state->keyboard_lane, shutdown_deadline_ms)
        || !lane_shutdown(&daemon_state->mouse_lane, shutdown_deadline_ms)) {
        fprintf(stderr, "inputd: shutdown deadline exceeded waiting for hook lanes; terminating\n");
        fflush(NULL);
        _exit(EXIT_FAILURE);
    }

    /* Abort in-progress prompts so worker threads exit promptly. */
    ksi_permissions_cancel();
    ksi_worker_pool_request_stop(&g_worker_pool);
    ksi_worker_pool_request_stop(&g_identify_worker_pool);

    /* Wake the main loop so it observes keep_running=0. */
    ksi_pipe_ring_wake(&command_queue.ring);

    if (!ksi_worker_pool_join_before(&g_worker_pool, shutdown_deadline_ms)
        || !ksi_worker_pool_join_before(&g_identify_worker_pool, shutdown_deadline_ms)) {
        fprintf(stderr, "inputd: shutdown deadline exceeded waiting for worker pool; terminating\n");
        fflush(NULL);
        _exit(EXIT_FAILURE);
    }

    ksi_worker_pool_destroy(&g_worker_pool);
    ksi_worker_pool_destroy(&g_identify_worker_pool);

    while (daemon_state->client_count > 0u) {
        remove_client(daemon_state, daemon_state->client_count - 1u);
    }

    /* Drain final replay/synth output before tearing down the backend. */
    if (daemon_state->sequencer_thread_started) {
        atomic_store(&daemon_state->sequencer_running, 0);
        output_queue_wake(&daemon_state->output_queue);

        if (!join_thread_before(daemon_state->sequencer_thread, shutdown_deadline_ms)) {
            fprintf(stderr, "inputd: shutdown deadline exceeded waiting for output sequencer; terminating\n");
            fflush(NULL);
            _exit(EXIT_FAILURE);
        }

        daemon_state->sequencer_thread_started = false;
    }

    synthetic_hook_queue_close(&daemon_state->synthetic_hook_queue);
    output_queue_close(&daemon_state->output_queue);
    ksi_active_session_resolver_cleanup(
        &daemon_state->active_session_resolver);
    command_queue_destroy(&command_queue);
    free(daemon_state);
    ksi_ipc_server_close(server);
    ksi_permissions_destroy(permissions);
    backend->stop();

    return 0;
}
