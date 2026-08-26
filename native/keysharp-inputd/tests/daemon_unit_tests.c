#include <stdbool.h>
#include <errno.h>
#include <linux/input-event-codes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

#include "platform/vk_evdev.h"

bool g_verbose = false;

/* White-box coverage for daemon-private queue and transaction invariants. */
#include "../src/daemon.c"

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #condition); \
        return false; \
    } \
} while (0)

static void destroy_client_ref(ksi_client *client, int peer_fd)
{
    hook_send_ref_invalidate(client->hook_send_ref);
    hook_send_ref_release(client->hook_send_ref);
    client->hook_send_ref = NULL;
    close(peer_fd);
}

static bool test_nested_parent_mismatch_fails_open_as_one_batch(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_nested_transaction *transaction;
    ksi_output_action action;

    CHECK(state != NULL);
    CHECK(output_queue_init(&state->output_queue, state) == 0);
    state->keyboard_lane.state = state;
    atomic_store(&state->keyboard_lane.current_event_id, 1u);
    atomic_store(&state->keyboard_lane.current_responder_connection_id, 42u);

    transaction = calloc(1,
        sizeof(*transaction) + 2u * sizeof(transaction->members[0]));
    CHECK(transaction != NULL);
    transaction->count = 2u;
    transaction->depth = 1u;
    transaction->origin_connection_id = 42u;
    transaction->parent_hook_event_id = 99u;
    transaction->members[0].input.type = KSI_INPUT_KEYBOARD;
    transaction->members[0].input.data.keyboard.vk = KSI_VK_LCONTROL;
    transaction->members[1].input = transaction->members[0].input;
    transaction->members[1].input.data.keyboard.flags = KSI_KEYEVENTF_KEYUP;

    CHECK(lane_process_nested_transaction(&state->keyboard_lane, transaction));
    CHECK(output_queue_pop(&state->output_queue, &action));
    CHECK(action.type == KSI_OUTPUT_ACTION_SYNTH);
    CHECK(action.synth_count == 2u);
    CHECK((action.synth_flags & KSI_SYNTH_FLAG_BYPASS_HOOK) != 0u);
    CHECK(action.synth_inputs[0].data.keyboard.flags == 0u);
    CHECK((action.synth_inputs[1].data.keyboard.flags & KSI_KEYEVENTF_KEYUP) != 0u);

    free(action.synth_inputs);
    output_queue_close(&state->output_queue);
    free(state);
    return true;
}

static bool test_subscriber_snapshot_uses_hook_install_order(void)
{
    const uint64_t ordinals[] = { 10u, 30u, 20u };
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_keyboard_hook_event hook_event = { 0 };
    ksi_lane_event *event;
    int sockets[3][2];

    CHECK(state != NULL);
    state->client_count = 3u;
    state->next_event_id = 1u;

    for (size_t i = 0u; i < 3u; i++) {
        CHECK(socketpair(AF_UNIX, SOCK_STREAM, 0, sockets[i]) == 0);
        state->clients[i].fd = sockets[i][0];
        state->clients[i].connection_id = i + 1u;
        state->clients[i].hook_subscriptions = KSI_CAP_HOOK_KEYBOARD;
        state->clients[i].hook_subscription_ordinal[0] = ordinals[i];
        state->clients[i].hook_send_ref = hook_send_ref_create(sockets[i][0]);
        CHECK(state->clients[i].hook_send_ref != NULL);
    }

    event = create_hook_lane_event(state, KSI_HOOK_KEYBOARD_LL,
        &hook_event, sizeof(hook_event), NULL, NULL, 0u, 0u);
    CHECK(event != NULL);
    CHECK(event->subscriber_count == 3u);
    CHECK(event->subscribers[0].connection_id == 2u);
    CHECK(event->subscribers[1].connection_id == 3u);
    CHECK(event->subscribers[2].connection_id == 1u);
    lane_event_release_send_refs(event);
    free(event);

    for (size_t i = 0u; i < 3u; i++) {
        destroy_client_ref(&state->clients[i], sockets[i][1]);
    }

    free(state);
    return true;
}

static bool test_only_active_seat_user_enters_hook_snapshot(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_keyboard_hook_event hook_event = { 0 };
    ksi_lane_event *event;
    int sockets[2][2];

    CHECK(state != NULL);
    state->input_owner_enforced = true;
    atomic_init(&state->active_input_uid_valid, true);
    atomic_init(&state->active_input_uid, 1000u);
    state->client_count = 2u;
    state->next_event_id = 1u;

    for (size_t i = 0u; i < 2u; i++) {
        CHECK(socketpair(AF_UNIX, SOCK_STREAM, 0, sockets[i]) == 0);
        state->clients[i].fd = sockets[i][0];
        state->clients[i].uid = (uid_t)(1000u + i);
        state->clients[i].connection_id = i + 1u;
        state->clients[i].hook_subscriptions = KSI_CAP_HOOK_KEYBOARD;
        state->clients[i].hook_subscription_ordinal[0] = i + 1u;
        state->clients[i].hook_send_ref = hook_send_ref_create(sockets[i][0]);
        CHECK(state->clients[i].hook_send_ref != NULL);
    }

    event = create_hook_lane_event(state, KSI_HOOK_KEYBOARD_LL,
        &hook_event, sizeof(hook_event), NULL, NULL, 0u, 0u);
    CHECK(event != NULL);
    CHECK(event->subscriber_count == 1u);
    CHECK(event->subscribers[0].connection_id == 1u);
    lane_event_release_send_refs(event);
    free(event);

    for (size_t i = 0u; i < 2u; i++) {
        destroy_client_ref(&state->clients[i], sockets[i][1]);
    }

    free(state);
    return true;
}

static bool test_seat_transition_fences_output_and_preserves_subscriptions(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_input old_input = { .type = KSI_INPUT_KEYBOARD };
    ksi_output_action action;

    CHECK(state != NULL);
    CHECK(output_queue_init(&state->output_queue, state) == 0);
    state->input_owner_enforced = true;
    atomic_init(&state->active_input_uid_valid, true);
    atomic_init(&state->active_input_uid, 1000u);
    atomic_init(&state->active_input_generation, 1u);
    state->client_count = 2u;
    state->clients[0].uid = 1000u;
    state->clients[0].hook_subscriptions = KSI_CAP_HOOK_KEYBOARD;
    state->clients[1].uid = 1001u;
    state->clients[1].hook_subscriptions = KSI_CAP_HOOK_MOUSE;

    CHECK(active_hook_subscription_mask(state) == KSI_CAP_HOOK_KEYBOARD);
    CHECK(output_queue_push_synth(
        &state->output_queue, &old_input, 1u, 0u, 1u));
    CHECK(output_queue_push_release_all(&state->output_queue));

    set_active_input_owner(state, true, 1001u);

    CHECK(atomic_load(&state->active_input_uid_valid));
    CHECK(atomic_load(&state->active_input_uid) == 1001u);
    CHECK(atomic_load(&state->active_input_generation) == 2u);
    CHECK(active_hook_subscription_mask(state) == KSI_CAP_HOOK_MOUSE);
    CHECK(state->clients[0].hook_subscriptions == KSI_CAP_HOOK_KEYBOARD);
    CHECK(state->clients[1].hook_subscriptions == KSI_CAP_HOOK_MOUSE);

    /* The old generation was discarded. Only the pre-existing internal safety
     * release and the transition's new release remain. */
    CHECK(output_queue_pop(&state->output_queue, &action));
    CHECK(action.type == KSI_OUTPUT_ACTION_RELEASE_ALL);
    CHECK(output_queue_pop(&state->output_queue, &action));
    CHECK(action.type == KSI_OUTPUT_ACTION_RELEASE_ALL);
    CHECK(!output_queue_pop(&state->output_queue, &action));

    output_queue_close(&state->output_queue);
    free(state);
    return true;
}

static bool test_cancelled_callback_is_not_delivered_after_turn_claim(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_lane_event *event;
    ksi_lane_decision decision;
    ksi_subscriber_result result;
    int sockets[2];
    char byte;

    CHECK(state != NULL);
    CHECK(socketpair(AF_UNIX, SOCK_STREAM, 0, sockets) == 0);
    state->keyboard_lane.state = state;
    atomic_store(&state->keyboard_lane.flush_generation, 2u);

    event = calloc(1, sizeof(*event) + sizeof(event->subscribers[0]));
    CHECK(event != NULL);
    event->event_id = 9u;
    event->hook_type = KSI_HOOK_KEYBOARD_LL;
    event->generation = 1u;
    event->subscriber_count = 1u;
    event->subscribers[0].send_ref = hook_send_ref_create(sockets[0]);
    CHECK(event->subscribers[0].send_ref != NULL);
    event->subscribers[0].connection_id = 7u;

    result = lane_call_subscriber(&state->keyboard_lane, event,
        &event->subscribers[0], &decision);
    CHECK(result == KSI_SUBSCRIBER_EVENT_CANCELLED);
    errno = 0;
    CHECK(recv(sockets[1], &byte, sizeof(byte), MSG_DONTWAIT) == -1);
    CHECK(errno == EAGAIN || errno == EWOULDBLOCK);

    lane_event_release_send_refs(event);
    free(event);
    close(sockets[1]);
    free(state);
    return true;
}

static bool test_quarantine_retries_in_daemon(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    int sockets[2];

    CHECK(state != NULL);
    CHECK(socketpair(AF_UNIX, SOCK_STREAM, 0, sockets) == 0);
    CHECK(output_queue_init(&state->output_queue, state) == 0);
    state->client_count = 1u;
    state->clients[0].fd = sockets[0];
    state->clients[0].connection_id = 77u;
    state->clients[0].hook_subscriptions = KSI_CAP_HOOK_KEYBOARD;
    state->clients[0].hook_send_ref = hook_send_ref_create(sockets[0]);
    CHECK(state->clients[0].hook_send_ref != NULL);

    record_client_hook_failure(state, 0u, KSI_HOOK_KEYBOARD_LL,
        1u, 0u, KSI_HOOK_DECISION_TIMEOUT_MS,
        KSI_HOOK_QUARANTINE_REASON_TIMEOUT);
    CHECK((state->clients[0].quarantined_hooks & KSI_CAP_HOOK_KEYBOARD) != 0u);
    CHECK(hook_send_ref_is_stalled(state->clients[0].hook_send_ref, 0u));

    state->clients[0].quarantine_rearm_after_ms[0] = 1u;
    retry_quarantined_hooks(state);
    CHECK((state->clients[0].quarantined_hooks & KSI_CAP_HOOK_KEYBOARD) == 0u);
    CHECK(!hook_send_ref_is_stalled(state->clients[0].hook_send_ref, 0u));

    destroy_client_ref(&state->clients[0], sockets[1]);
    output_queue_close(&state->output_queue);
    free(state);
    return true;
}

static bool test_fifth_timeout_invalidates_only_that_session(void)
{
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    int sockets[2];

    CHECK(state != NULL);
    CHECK(socketpair(AF_UNIX, SOCK_STREAM, 0, sockets) == 0);
    CHECK(output_queue_init(&state->output_queue, state) == 0);
    state->client_count = 1u;
    state->clients[0].fd = sockets[0];
    state->clients[0].connection_id = 77u;
    state->clients[0].hook_subscriptions = KSI_CAP_HOOK_KEYBOARD;
    state->clients[0].hook_send_ref = hook_send_ref_create(sockets[0]);
    CHECK(state->clients[0].hook_send_ref != NULL);

    for (unsigned int strike = 1u;
            strike <= KSI_MAX_CONSECUTIVE_HOOK_FAILURES; strike++) {
        record_client_hook_failure(state, 0u, KSI_HOOK_KEYBOARD_LL,
            strike, 0u, KSI_HOOK_DECISION_TIMEOUT_MS,
            KSI_HOOK_QUARANTINE_REASON_TIMEOUT);

        if (strike < KSI_MAX_CONSECUTIVE_HOOK_FAILURES) {
            CHECK(state->client_count == 1u);
            state->clients[0].quarantined_hooks = 0u;
            hook_send_ref_clear_stalled(state->clients[0].hook_send_ref, 0u);
        }
    }

    CHECK(state->client_count == 0u);
    output_queue_close(&state->output_queue);
    close(sockets[1]);
    free(state);
    return true;
}

static bool test_release_transitions_keep_reserved_output_capacity(void)
{
    ksi_daemon_state state = { 0 };
    ksi_input down = { .type = KSI_INPUT_KEYBOARD };
    ksi_input up = { .type = KSI_INPUT_KEYBOARD };
    ksi_hook_event_payload replay = { .hook_type = KSI_HOOK_KEYBOARD_LL };
    size_t occupied = KSI_MAX_OUTPUT_ACTIONS - KSI_OUTPUT_CLEANUP_RESERVE;
    size_t slot;

    up.data.keyboard.flags = KSI_KEYEVENTF_KEYUP;
    CHECK(output_queue_init(&state.output_queue, &state) == 0);
    state.output_queue.count = occupied;
    CHECK(!output_queue_push_synth(&state.output_queue, &down, 1u, 0u, 0u));
    CHECK(output_queue_push_synth(&state.output_queue, &up, 1u, 0u, 0u));

    slot = occupied % KSI_MAX_OUTPUT_ACTIONS;
    free(state.output_queue.actions[slot].synth_inputs);
    memset(&state.output_queue.actions[slot], 0,
        sizeof(state.output_queue.actions[slot]));
    state.output_queue.count = occupied;
    state.output_queue.synth_bytes = 0u;

    replay.event.keyboard.message = KSI_WM_KEYDOWN;
    CHECK(!output_queue_push_replay(
        &state.output_queue, KSI_HOOK_KEYBOARD_LL, &replay, 0u));
    replay.event.keyboard.message = KSI_WM_KEYUP;
    CHECK(output_queue_push_replay(
        &state.output_queue, KSI_HOOK_KEYBOARD_LL, &replay, 0u));

    state.output_queue.count = 0u;
    ksi_linux_synth_reset_enqueued_synth();
    output_queue_close(&state.output_queue);
    return true;
}

static bool test_synthetic_queue_rejects_whole_batch(void)
{
    ksi_synthetic_hook_queue queue;
    ksi_input inputs[2] = {
        { .type = KSI_INPUT_KEYBOARD },
        { .type = KSI_INPUT_KEYBOARD },
    };

    CHECK(synthetic_hook_queue_init(&queue) == 0);
    queue.count = KSI_MAX_SYNTH_HOOK_ACTIONS - 1u;
    CHECK(!synthetic_hook_queue_push(
        &queue, inputs, 2u, 2u, 123u, NULL, 0u));
    CHECK(queue.count == KSI_MAX_SYNTH_HOOK_ACTIONS - 1u);
    queue.count = 0u;
    synthetic_hook_queue_close(&queue);
    return true;
}

static bool test_detached_synth_completion_keeps_batch_atomic(void)
{
    ksi_daemon_state state = { 0 };
    ksi_synthetic_hook_queue queue;
    ksi_synth_completion *completion;
    ksi_synth_completion *popped_completion;
    ksi_input inputs[2] = {
        { .type = KSI_INPUT_KEYBOARD },
        { .type = KSI_INPUT_KEYBOARD },
    };
    ksi_input popped;
    uint64_t queued_at;
    uint64_t generation;
    bool batch_start;

    CHECK(synthetic_hook_queue_init(&queue) == 0);
    completion = synth_completion_create(&state, NULL, NULL);
    CHECK(completion != NULL);
    synth_completion_begin_atomic(completion);
    CHECK(atomic_load(&state.active_synthetic_transactions) == 1u);
    CHECK(synthetic_hook_queue_push(
        &queue, inputs, 2u, 2u, 123u, completion, 7u));

    CHECK(synthetic_hook_queue_pop(&queue, &popped, &queued_at,
        &popped_completion, &batch_start, &generation));
    CHECK(popped_completion == completion);
    CHECK(batch_start);
    synth_completion_complete(popped_completion);
    CHECK(atomic_load(&state.active_synthetic_transactions) == 1u);

    CHECK(synthetic_hook_queue_pop(&queue, &popped, &queued_at,
        &popped_completion, &batch_start, &generation));
    CHECK(popped_completion == completion);
    CHECK(!batch_start);
    synth_completion_complete(popped_completion);
    CHECK(atomic_load(&state.active_synthetic_transactions) == 0u);

    synthetic_hook_queue_close(&queue);
    return true;
}

static unsigned int prepare_capabilities_calls;

static void record_prepare_capabilities(uint32_t requested)
{
    if (requested != 0u) {
        prepare_capabilities_calls++;
    }
}

static uint32_t prepared_available_capabilities(void)
{
    return prepare_capabilities_calls == 0u ? 0u : KSI_CAP_HOOK_KEYBOARD;
}

static bool test_capless_query_does_not_prepare_input_devices(void)
{
    const ksi_platform_backend backend = {
        .prepare_capabilities = record_prepare_capabilities,
        .get_available_capabilities = prepared_available_capabilities,
    };
    ksi_daemon_state state = { .backend = &backend };

    prepare_capabilities_calls = 0u;
    prepare_requested_capabilities(&state, 0u);
    CHECK(prepare_capabilities_calls == 0u);
    CHECK(state.available_capabilities == 0u);
    prepare_requested_capabilities(&state, KSI_CAP_HOOK_KEYBOARD);
    CHECK(prepare_capabilities_calls == 1u);
    CHECK(state.available_capabilities == KSI_CAP_HOOK_KEYBOARD);
    return true;
}

static bool test_vk_evdev_mapping_covers_portable_and_alternate_codes(void)
{
    CHECK(ksi_vk_to_evdev(0x03u) == KEY_CANCEL);
    CHECK(ksi_vk_to_evdev(0x15u) == KEY_KATAKANAHIRAGANA);
    CHECK(ksi_vk_to_evdev(0x19u) == KEY_HANJA);
    CHECK(ksi_vk_to_evdev(0xB4u) == KEY_MAIL);
    CHECK(ksi_vk_to_evdev(0xABu) == KEY_BOOKMARKS);
    CHECK(ksi_vk_to_evdev(0xE2u) == KEY_102ND);
    CHECK(ksi_evdev_to_vk(KEY_KATAKANA) == 0x15u);
    CHECK(ksi_evdev_to_vk(KEY_HANGEUL) == 0x15u);
    CHECK(ksi_evdev_to_vk(KEY_KPJPCOMMA) == 0x6Cu);
    CHECK(ksi_evdev_to_vk(KEY_KPEQUAL) == 0xBBu);
    CHECK(ksi_evdev_to_vk(KEY_YEN) == 0xDCu);
    CHECK(ksi_evdev_to_vk(KEY_MENU) == 0x5Du);
    CHECK(ksi_evdev_to_vk(KEY_WWW) == 0xACu);
    CHECK(ksi_evdev_to_vk(KEY_BOOKMARKS) == 0xABu);
    CHECK(ksi_evdev_to_vk(KEY_FAVORITES) == 0xABu);
    CHECK(ksi_evdev_to_vk(KEY_EMAIL) == 0xB4u);
    CHECK(ksi_evdev_to_vk(KEY_RO) == 0xE2u);
    return true;
}

static bool test_panic_chord_requires_all_physical_keys(void)
{
    uint8_t keys[KSI_KEY_STATE_BITMAP_BYTES] = { 0 };

    keys[KEY_BACKSPACE >> 3u] |= (uint8_t)(1u << (KEY_BACKSPACE & 7u));
    keys[KEY_ESC >> 3u] |= (uint8_t)(1u << (KEY_ESC & 7u));
    CHECK(!panic_chord_is_down(keys));
    keys[KEY_ENTER >> 3u] |= (uint8_t)(1u << (KEY_ENTER & 7u));
    CHECK(panic_chord_is_down(keys));
    keys[KEY_ESC >> 3u] &= (uint8_t)~(1u << (KEY_ESC & 7u));
    CHECK(!panic_chord_is_down(keys));
    return true;
}

static bool test_lane_event_allocation_benchmark(void)
{
    const unsigned int iterations = 100000u;
    ksi_daemon_state *state = calloc(1, sizeof(*state));
    ksi_keyboard_hook_event event = {
        .message = KSI_WM_KEYDOWN,
        .vk_code = 0x41u,
    };
    struct timespec begin;
    struct timespec end;
    uint64_t elapsed_ns;

    CHECK(state != NULL);
    CHECK(clock_gettime(CLOCK_MONOTONIC, &begin) == 0);

    for (unsigned int i = 0u; i < iterations; i++) {
        ksi_lane_event *lane_event = create_hook_lane_event(
            state, KSI_HOOK_KEYBOARD_LL, &event, sizeof(event),
            NULL, NULL, 0u, 0u);
        CHECK(lane_event != NULL);
        free(lane_event);
    }

    CHECK(clock_gettime(CLOCK_MONOTONIC, &end) == 0);
    elapsed_ns = ((uint64_t)(end.tv_sec - begin.tv_sec) * 1000000000u)
        + (uint64_t)(end.tv_nsec - begin.tv_nsec);
    printf("lane event snapshot: %.1f ns/event\n",
        (double)elapsed_ns / iterations);
    free(state);
    return true;
}

int main(void)
{
    const struct {
        const char *name;
        bool (*run)(void);
    } tests[] = {
        { "VK/evdev mapping covers portable and alternate codes", test_vk_evdev_mapping_covers_portable_and_alternate_codes },
        { "panic chord requires all physical keys", test_panic_chord_requires_all_physical_keys },
        { "stale nested transaction preserves its whole batch", test_nested_parent_mismatch_fails_open_as_one_batch },
        { "hook snapshot follows install order", test_subscriber_snapshot_uses_hook_install_order },
        { "only active seat user receives callbacks", test_only_active_seat_user_enters_hook_snapshot },
        { "seat transition fences output and preserves subscriptions", test_seat_transition_fences_output_and_preserves_subscriptions },
        { "cancelled callback is not delivered after turn claim", test_cancelled_callback_is_not_delivered_after_turn_claim },
        { "quarantine retries within the daemon", test_quarantine_retries_in_daemon },
        { "fifth timeout invalidates one session", test_fifth_timeout_invalidates_only_that_session },
        { "output queue reserves release capacity", test_release_transitions_keep_reserved_output_capacity },
        { "synthetic queue rejects whole batch", test_synthetic_queue_rejects_whole_batch },
        { "detached synthesis completion preserves batch atomicity", test_detached_synth_completion_keeps_batch_atomic },
        { "capless query leaves devices inactive", test_capless_query_does_not_prepare_input_devices },
        { "lane snapshot allocation benchmark", test_lane_event_allocation_benchmark },
    };

    for (size_t i = 0u; i < sizeof(tests) / sizeof(tests[0]); i++) {
        if (!tests[i].run()) {
            return 1;
        }

        printf("PASS %s\n", tests[i].name);
    }

    return 0;
}
