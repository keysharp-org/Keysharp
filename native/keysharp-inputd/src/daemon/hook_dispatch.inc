static void complete_hook_dispatch_fallback(
    ksi_daemon_state *state,
    uint32_t hook_type,
    const void *event,
    size_t event_size,
    const ksi_lane_egress *egress,
    ksi_synth_completion *completion,
    uint64_t input_generation)
{
    bool ok = true;

    if (egress != NULL) {
        if (egress->type == KSI_LANE_EGRESS_SYNTH) {
            ok = output_queue_push_synth(&state->output_queue,
                egress->synth_inputs,
                egress->synth_count,
                egress->synth_flags,
                input_generation);
        } else if (egress->type == KSI_LANE_EGRESS_REPLAY) {
            ksi_hook_event_payload payload;

            memset(&payload, 0, sizeof(payload));
            payload.hook_type = hook_type;
            memcpy(&payload.event, event, event_size);
            ok = output_queue_push_replay(
                &state->output_queue, hook_type, &payload, input_generation);
        }
    }

    if (completion != NULL) {
        if (ok) {
            synth_completion_complete(completion);
        } else {
            synth_completion_fail(completion, -1, KSI_DETAIL_RESOURCE_EXHAUSTED);
        }

        synthetic_hook_queue_complete_inflight(state);
    }

    if (!ok && egress != NULL && egress->type == KSI_LANE_EGRESS_REPLAY) {
        request_fail_open(state);
    }
}

static ksi_lane_event *create_hook_lane_event(
    ksi_daemon_state *state,
    uint32_t hook_type,
    const void *event,
    size_t event_size,
    const ksi_lane_egress *egress,
    ksi_synth_completion *completion,
    uint32_t nesting_depth,
    uint64_t input_generation)
{
    ksi_hook_lane *lane;
    ksi_lane_event *lane_event;
    ksi_lane_subscriber candidates[KSI_MAX_CLIENTS];
    size_t candidate_count = 0u;
    uint64_t event_id;
    uint32_t subscription_bit;
    size_t lane_index;

    lane = lane_for_hook_type(state, hook_type);

    if (lane == NULL || event_size > sizeof(lane_event->payload.event)) {
        return NULL;
    }

    subscription_bit = hook_type_to_cap_bit(hook_type);

    if (subscription_bit == 0u) {
        return NULL;
    }

    event_id = state->next_event_id++;
    lane_index = hook_type_to_lane_index(hook_type);

    /* Windows installs each hook TYPE at the head of its own chain. Connection
     * age is not install age: an older process which unsubscribes/re-subscribes
     * becomes newest for that type. Collect by monotonic subscription ordinal;
     * connection id is only a deterministic fallback for zeroed test fixtures. */
    for (nfds_t i = state->client_count;
            i-- > 0 && candidate_count < KSI_MAX_CLIENTS; ) {
        const ksi_client *c = &state->clients[i];
        ksi_lane_subscriber candidate;
        size_t position;

        if ((c->hook_subscriptions & subscription_bit) == 0u
            || (c->quarantined_hooks & subscription_bit) != 0u
            || !input_owner_matches_uid(state, c->uid)) {
            continue;
        }

        candidate = (ksi_lane_subscriber) {
            .send_ref = c->hook_send_ref,
            .connection_id = c->connection_id,
            .subscription_ordinal = c->hook_subscription_ordinal[lane_index],
        };

        position = candidate_count;

        while (position > 0u
            && (candidate.subscription_ordinal
                    > candidates[position - 1u].subscription_ordinal
                || (candidate.subscription_ordinal
                        == candidates[position - 1u].subscription_ordinal
                    && candidate.connection_id
                        > candidates[position - 1u].connection_id))) {
            candidates[position] = candidates[position - 1u];
            position--;
        }

        candidates[position] = candidate;
        candidate_count++;
    }

    lane_event = calloc(1,
        sizeof(*lane_event) + candidate_count * sizeof(lane_event->subscribers[0]));

    if (lane_event == NULL) {
        return NULL;
    }

    lane_event->event_id = event_id;
    lane_event->hook_type = hook_type;
    lane_event->generation = atomic_load(&lane->flush_generation);
    lane_event->input_generation = input_generation;
    lane_event->synthetic_completion = completion;
    lane_event->nesting_depth = nesting_depth;

    if (egress != NULL) {
        lane_event->egress = *egress;
    }

    lane_event->payload.event_id = event_id;
    lane_event->payload.hook_type = hook_type;
    memcpy(&lane_event->payload.event, event, event_size);
    lane_event->payload_size =
        sizeof(lane_event->payload.event_id)
        + sizeof(lane_event->payload.hook_type)
        + sizeof(lane_event->payload.reserved)
        + event_size;

    for (size_t i = 0u; i < candidate_count; i++) {
        ksi_lane_subscriber candidate = candidates[i];

        if (!hook_send_ref_acquire(candidate.send_ref)) {
            fprintf(stderr, "inputd: hook subscriber has no send handle\n");
            continue;
        }

        lane_event->subscribers[lane_event->subscriber_count++] = candidate;
    }

    return lane_event;
}

static void enqueue_hook_lane_event(ksi_daemon_state *state, ksi_lane_event *event)
{
    ksi_hook_lane *lane = lane_for_hook_type(state, event->hook_type);

    if (event->physical_input) {
        atomic_fetch_add(&state->physical_hook_inflight, 1u);
    }

    if (!ksi_pipe_ring_push(&lane->action_queue.ring, &event)) {
        bool egress_ok = lane_event_enqueue_egress(lane, event);

        if (!egress_ok && event->egress.type == KSI_LANE_EGRESS_REPLAY) {
            request_fail_open(state);
        }

        fprintf(stderr, "inputd: hook lane queue full; bypassed hook event %llu\n",
            (unsigned long long)event->event_id);
        lane_event_release_send_refs(event);

        if (event->physical_input) {
            atomic_fetch_sub(&state->physical_hook_inflight, 1u);
        }

        if (event->synthetic_completion != NULL) {
            if (egress_ok) {
                synth_completion_complete(event->synthetic_completion);
            } else {
                synth_completion_fail(event->synthetic_completion, -1,
                    KSI_DETAIL_RESOURCE_EXHAUSTED);
            }

            synthetic_hook_queue_complete_inflight(state);
        }

        free(event);
    }
}

static void dispatch_hook_event_to_lane(
    ksi_daemon_state *state,
    uint32_t hook_type,
    const void *event,
    size_t event_size,
    const ksi_lane_egress *egress,
    ksi_synth_completion *completion,
    bool physical_input,
    uint64_t input_generation)
{
    ksi_lane_event *lane_event = create_hook_lane_event(
        state, hook_type, event, event_size, egress, completion, 0u,
        input_generation);

    if (lane_event == NULL) {
        complete_hook_dispatch_fallback(
            state, hook_type, event, event_size, egress, completion,
            input_generation);
        return;
    }

    lane_event->physical_input = physical_input;
    enqueue_hook_lane_event(state, lane_event);
}

static bool physical_key_is_down(const uint8_t *keys, uint32_t code)
{
    return (keys[code >> 3u] & (uint8_t)(1u << (code & 7u))) != 0u;
}

static bool panic_chord_is_down(const uint8_t *physical_keys)
{
    return physical_key_is_down(physical_keys, KEY_BACKSPACE)
        && physical_key_is_down(physical_keys, KEY_ESC)
        && physical_key_is_down(physical_keys, KEY_ENTER);
}

static void daemon_handle_physical_key_event(void *context, uint32_t scan_code)
{
    ksi_daemon_state *state = context;
    ksi_key_state_payload key_state;

    if (state == NULL || !state->interception_active
        || (scan_code != KEY_BACKSPACE && scan_code != KEY_ESC && scan_code != KEY_ENTER)) {
        return;
    }

    if (!ksi_linux_devices_get_key_state(&key_state)
        || !panic_chord_is_down(key_state.physical_keys)) {
        return;
    }

    fprintf(stderr, "inputd: physical input panic chord activated\n");
    clear_hook_state(state);

    if (update_grab_state(state) != 0) {
        fprintf(stderr, "inputd: panic chord failed to release grabs\n");
    }
}

static void daemon_handle_hook_event(
    void *context,
    uint32_t hook_type,
    const void *event,
    size_t event_size)
{
    ksi_daemon_state *state = context;
    uint32_t subscription_bit = hook_type_to_cap_bit(hook_type);
    uint32_t block_bit;
    bool is_injected;

    if (state == NULL || subscription_bit == 0
        || (state->input_owner_enforced
            && !atomic_load_explicit(
                &state->active_input_uid_valid, memory_order_acquire))) {
        return;
    }

    block_bit = hook_type == KSI_HOOK_KEYBOARD_LL
        ? KSI_BLOCK_INPUT_KEYBOARD
        : KSI_BLOCK_INPUT_MOUSE;
    is_injected = hook_type == KSI_HOOK_KEYBOARD_LL
        ? ((((const ksi_keyboard_hook_event *)event)->flags & KSI_LLKHF_INJECTED) != 0)
        : ((((const ksi_mouse_hook_event *)event)->flags & KSI_LLMHF_INJECTED) != 0);

    /* BlockInput suppresses delivery to APPLICATIONS, not to the owning hook.
     * On Windows BlockInput() is independent of the LL hook chain: hooks (and
     * therefore hotkeys/InputHooks, including the one that will turn BlockInput
     * back off) keep firing on physical input while it is active. So do NOT drop
     * the event here; dispatch it to the hook as usual but force egress to NONE
     * below so it is never replayed to apps regardless of the PASS decision. */
    bool block_active = !is_injected && (active_block_input_mask(state) & block_bit) != 0;

    if (!any_matching_hook_subscriptions(state, hook_type)) {
        /* Mixed devices may be grabbed for the other input class. When BlockInput
         * is active with no hook subscribed, keep the old behavior of dropping the
         * event from apps (no replay). */
        if (!is_injected && !block_active) {
            ksi_hook_event_payload payload;
            memset(&payload, 0, sizeof(payload));
            payload.hook_type = hook_type;
            memcpy(&payload.event, event, event_size);

            if (!output_queue_push_replay(&state->output_queue, hook_type, &payload,
                    current_input_generation(state))) {
                fprintf(stderr, "inputd: replay enqueue of unhooked grabbed input failed\n");
                request_fail_open(state);
            }
        }
        return;
    }

    {
        ksi_lane_egress egress;

        memset(&egress, 0, sizeof(egress));
        egress.type = (is_injected || block_active) ? KSI_LANE_EGRESS_NONE : KSI_LANE_EGRESS_REPLAY;
        dispatch_hook_event_to_lane(
            state, hook_type, event, event_size, &egress, NULL, true,
            current_input_generation(state));
    }
}
