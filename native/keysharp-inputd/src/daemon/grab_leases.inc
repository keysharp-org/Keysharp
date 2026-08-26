static void send_status(
    int client_fd,
    const ksi_message_header *request,
    uint32_t response_type,
    int32_t status,
    uint32_t detail)
{
    ksi_status_payload payload = {
        .status = status,
        .detail = detail,
    };

    (void)ipc_send_locked(
        client_fd,
        response_type,
        request->client_id,
        request->correlation_id,
        &payload,
        sizeof(payload));
}

static void send_hello_status(
    const ksi_client *client,
    uint32_t client_id,
    uint64_t correlation_id,
    int32_t status,
    uint32_t granted_capabilities)
{
    ksi_client_hello_result_payload payload = {
        .status = status,
        .granted_capabilities = granted_capabilities,
    };

    (void)ipc_send_locked(
        client->fd,
        KSI_MESSAGE_CLIENT_HELLO,
        client_id,
        correlation_id,
        &payload,
        sizeof(payload));
}

static uint32_t permission_capabilities_for_request(uint32_t requested)
{
    return requested;
}

/* Persistently-allowed capabilities for a client: its per-script identity OR, when known, its
 * WILDCARD identity (the "Allow for executable" grant that covers every script run by this binary).
 * The single place the wildcard is OR'd in, so every read path (grant check + response recompute)
 * stays consistent. An empty wildcard_hash contributes nothing. */
static uint32_t client_persistent_allowed(const ksi_daemon_state *state, const ksi_client *client)
{
    uint32_t caps = ksi_permissions_get_allowed_capabilities(
        state->permissions, client->uid, client->exe_hash);

    if (client->wildcard_hash[0] != '\0')
        caps |= ksi_permissions_get_allowed_capabilities(
            state->permissions, client->uid, client->wildcard_hash);

    return caps;
}

/* Returns the capabilities currently granted from the store without prompting.
 * Sets *out_missing to the grouped permission capabilities not yet granted. */
static uint32_t check_capabilities_sync(
    ksi_daemon_state *state,
    const ksi_client *client,
    uint32_t requested,
    bool force_prompt,
    uint32_t *out_missing)
{
    uint32_t expanded;
    uint32_t granted = 0u;
    uint32_t all_process_caps;
    uint32_t missing = 0u;

    if (out_missing != NULL) {
        *out_missing = 0u;
    }

    if (state == NULL || client == NULL || requested == 0u) {
        return 0u;
    }

    if (!client->has_identity || state->permissions == NULL) {
        return 0u;
    }

    expanded = permission_capabilities_for_request(requested) & state->available_capabilities;

    if (expanded != 0u) {
        granted = client_persistent_allowed(state, client) & expanded;

        /* Inherit per-process session grants from sibling clients of the same
         * Keysharp process (same uid+pid).  This is how "Allow once" decisions
         * survive across a script's separate hook and synthesis channels without
         * being persisted to disk or surviving past the process.
         * all_process_caps accumulates unfiltered sibling caps for implication checks. */
        all_process_caps = client->granted_capabilities;

        for (nfds_t i = 0; i < state->client_count; i++) {
            const ksi_client *sibling = &state->clients[i];

            if (sibling == client) {
                continue;
            }

            if (!sibling->authenticated || sibling->pid != client->pid || sibling->uid != client->uid) {
                continue;
            }

            granted |= (sibling->granted_capabilities & expanded);
            all_process_caps |= sibling->granted_capabilities;
        }

        /* KSI_CAP_BLOCK_INPUT is implied by hook capabilities. */
        if ((expanded & KSI_CAP_BLOCK_INPUT) != 0u &&
            (all_process_caps & (KSI_CAP_HOOK_KEYBOARD | KSI_CAP_HOOK_MOUSE)) != 0u)
            granted |= KSI_CAP_BLOCK_INPUT;

        /* Check the shared PID-keyed session file for physical capabilities.
         * This picks up "Allow once" decisions that were written by any daemon
         * (including a prior keysharp-helper prompt) for this specific Keysharp process. */
        if (client->start_time != 0u) {
            uint32_t pid_session = ksi_permissions_get_session_by_pid(
                client->uid, client->pid, client->start_time);
            granted |= pid_session & expanded;
        }

        missing = expanded & ~granted;

        /* Persistent denials suppress prompts. */
        if (!force_prompt) {
            missing &= ~ksi_permissions_get_denied_capabilities(
                state->permissions, client->uid, client->exe_hash);
        }
    }

    /* Non-physical capabilities (e.g. screen capture) that inputd does not
     * provide directly, but brokers via the combined prompt so that the physical
     * daemon (keysharp-helper) can check the PID session and skip its own prompt.
     * Check persistent trust store and PID session; add to missing if neither
     * covers them, so the prompt includes them in the requested capability list. */
    {
        uint32_t non_physical = requested & ~state->available_capabilities;

        if (non_physical != 0u) {
            uint32_t np_granted = client_persistent_allowed(state, client) & non_physical;

            if (client->start_time != 0u)
                np_granted |= ksi_permissions_get_session_by_pid(
                    client->uid, client->pid, client->start_time) & non_physical;

            uint32_t np_missing = non_physical & ~np_granted;

            if (!force_prompt)
                np_missing &= ~ksi_permissions_get_denied_capabilities(
                    state->permissions, client->uid, client->exe_hash);

            missing |= np_missing;
        }
    }

    /* Session-only denials (a plain "Deny" chosen earlier THIS run) suppress re-prompting for
     * the rest of the connection without any persistent record, so the next script run re-asks.
     * Held on the client (one connection per Keysharp run), so it clears when the run ends. */
    if (!force_prompt) {
        missing &= ~client->session_denied_capabilities;
    }

    if (out_missing != NULL) {
        *out_missing = missing;
    }

    return granted;  /* physical capabilities only */
}

/* Applies a completed prompt decision and sends the CLIENT_HELLO response. */
static void process_client_prompt_done(ksi_daemon_state *state, ksi_daemon_command *command)
{
    ssize_t client_index;
    ksi_client *client;
    ksi_permission_decision decision;
    uint32_t requested;
    uint32_t missing;
    uint32_t granted;
    uint32_t available_capabilities_at_start;

    if (state == NULL || command == NULL) {
        return;
    }

    decision = command->data.prompt_done.decision;
	requested = command->data.prompt_done.requested_capabilities;
	missing = command->data.prompt_done.missing_capabilities;
	available_capabilities_at_start = command->data.prompt_done.available_capabilities_at_start;

    client_index = find_client_index_by_connection(
        state,
        command->client_fd,
        command->connection_id);

    if (client_index < 0) {
        return;
    }

    client = &state->clients[client_index];
    client->state = KSI_CLIENT_STATE_READY;

    if (!client->pending_hello_valid) {
        return;
    }

    if (!input_owner_matches_uid(state, client->uid)) {
        client->pending_hello_valid = false;
        send_hello_status(client,
            client->pending_hello_client_id,
            client->pending_hello_correlation_id,
            -1, 0u);
        return;
    }

    client->pending_hello_valid = false;

    /* Separate physical (inputd-managed) from non-physical (e.g. screen capture)
     * capabilities.  Both appear in `missing` because check_capabilities_sync
     * includes non-physical caps so they appear in the combined prompt.
     *
     * Masked by available_capabilities_at_start (a snapshot taken when the
     * prompt was STARTED), not the current state->available_capabilities: a
     * permission prompt can sit open for as long as the user takes to answer
     * it, and a hotplug during that window must not silently redefine what
     * the request "was" -- that would let (granted & physical_requested) ==
     * physical_requested spuriously pass below even though a capability the
     * user was actually asked about is no longer being granted, reporting a
     * false success instead of the honest failure. What we actually PROMISE
     * the client (the `granted` mask itself, below) is still gated by the
     * CURRENT availability, so a capability that genuinely became unavailable
     * is never claimed as granted -- only the pass/fail determination and the
     * ALLOW_ONCE in-process grant use the start-of-prompt snapshot. */
    uint32_t physical_missing    = missing &  available_capabilities_at_start;
    uint32_t non_physical_missing = missing & ~available_capabilities_at_start;
    uint32_t physical_requested  = requested & available_capabilities_at_start;

	if (decision == KSI_PERMISSION_DECISION_ALLOW_ALWAYS) {
		/* Grant ALL missing capabilities persistently — physical ones will be
		 * honoured by inputd on future connections; non-physical ones (e.g. screen
		 * capture) are stored in the shared trust store where keysharp-helper reads them. */
		(void)ksi_permissions_grant_persistent(
			state->permissions, client->uid, client->exe_hash, client->exe_path, missing);
	} else if (decision == KSI_PERMISSION_DECISION_ALLOW_ALL_SCRIPTS) {
		/* "Allow for executable": persist under the WILDCARD identity (exe portion only) so the grant
		 * covers every script run by this same binary, not just the one that prompted. If the wildcard
		 * identity is somehow unknown, fall back to the per-script grant so the user's Allow still takes
		 * effect for at least this script. */
		(void)ksi_permissions_grant_persistent(
			state->permissions, client->uid,
			client->wildcard_hash[0] != '\0' ? client->wildcard_hash : client->exe_hash,
			client->exe_path, missing);
	} else if (decision == KSI_PERMISSION_DECISION_PROMPT_UNAVAILABLE) {
		/* The prompt could not actually be shown — don't record a denial at all. */
	} else if (decision != KSI_PERMISSION_DECISION_ALLOW_ONCE) {
		/* A plain "Deny" is SESSION-ONLY: suppress re-prompting for the rest of this run by
		 * flagging the bits on the connection (see check_capabilities_sync), rather than writing
		 * a persistent denial. A mis-clicked Deny therefore recovers on the next run; a durable
		 * denial is available out-of-band via the keysharp-trust CLI. */
		client->session_denied_capabilities |= missing;
	}

    if (decision == KSI_PERMISSION_DECISION_ALLOW_ONCE && missing != 0u) {
        /* Write ALL granted capabilities (physical + non-physical) to the shared
         * PID session file.  keysharp-helper and other daemons read this file and skip
         * their own prompts when they find the relevant capability bits set. */
        if (client->start_time != 0u) {
            (void)ksi_permissions_grant_session_for_pid(
                client->uid, client->pid, client->start_time, missing);
        }
    }

    /* Recompute the physical capabilities actually granted for the HELLO response.
     * Non-physical caps are intentionally excluded — inputd cannot provide them. */
    granted = client_persistent_allowed(state, client)
              & (permission_capabilities_for_request(requested) & state->available_capabilities);

    if (decision == KSI_PERMISSION_DECISION_ALLOW_ONCE) {
        /* Physical missing caps are granted in-process; propagate to siblings.
         * Re-masked by the CURRENT availability (not the prompt-start
         * snapshot physical_missing was computed from): the pass/fail check
         * below correctly uses the snapshot so a mid-prompt hotplug loss is
         * never silently reported as success, but what we actually hand back
         * as `granted` must never claim a capability that isn't deliverable
         * right now. */
        granted |= physical_missing & state->available_capabilities;
    }

    (void)non_physical_missing;  /* acknowledged; handled via PID session above */

    if (decision == KSI_PERMISSION_DECISION_DENY
        || decision == KSI_PERMISSION_DECISION_PROMPT_UNAVAILABLE
        || (granted & physical_requested) != physical_requested) {
        /* Union, not overwrite -- and only de-authenticate if nothing has ever
         * been granted on this connection. See the matching comment in
         * protocol_server.inc's handle_client_hello for the full rationale. */
        client->granted_capabilities |= granted;
        client->authenticated = client->authenticated || client->granted_capabilities != 0u;
        send_hello_status(client,
            client->pending_hello_client_id, client->pending_hello_correlation_id,
            -1, granted);
        return;
    }

	ksi_permissions_note_seen(state->permissions, client->uid, client->exe_hash, client->exe_path);
	client->authenticated = true;
    client->granted_capabilities |= granted;
    send_hello_status(client,
        client->pending_hello_client_id, client->pending_hello_correlation_id,
        0, granted);
}

/* Stores the identification result and, if a CLIENT_HELLO was buffered,
 * processes it immediately or starts the permission prompt. */
static void process_client_identified(ksi_daemon_state *state, ksi_daemon_command *command)
{
    ssize_t client_index;
    ksi_client *client;
    ksi_client_identified_result *result;

    if (state == NULL || command == NULL) {
        return;
    }

    result = command->data.identified.result;
    client_index = find_client_index_by_connection(
        state,
        command->client_fd,
        command->connection_id);

    if (client_index < 0) {
        return;
    }

    client = &state->clients[client_index];

    if (!input_owner_matches_uid(state, client->uid)) {
        client->state = KSI_CLIENT_STATE_READY;

        if (client->pending_hello_valid) {
            client->pending_hello_valid = false;
            send_hello_status(client,
                client->pending_hello_client_id,
                client->pending_hello_correlation_id,
                -1, 0u);
        }

        return;
    }

    /* Reject the identity if the process the worker read is not the one we
     * accepted: a zero/mismatched start time means the pid was recycled between
     * accept and the /proc read (TOCTOU). Deny caps rather than trust a possibly
     * different process's exe. (Note: exec() within the same pid does NOT change
     * start_time, so this closes pid-reuse only, not a fork-then-exec-in-pid race;
     * that residual is accepted — the (uid, exe) boundary is same-uid-weak by
     * design, see permissions.c.) */
    if (result != NULL && result->has_identity
        && (result->start_time == 0u || client->accept_start_time == 0u
            || result->start_time != client->accept_start_time)) {
        fprintf(stderr,
            "inputd: rejecting identity for client pid=%ld: process start time changed "
            "between accept and identify (pid recycled)\n", (long)client->pid);
        result = NULL;
    }

    if (result != NULL && result->has_identity) {
        client->has_identity = true;
        client->start_time = result->start_time;
        (void)snprintf(client->exe_path, sizeof(client->exe_path), "%s", result->exe_path);
        (void)snprintf(client->command_line, sizeof(client->command_line), "%s", result->command_line);
        (void)snprintf(client->exe_hash, sizeof(client->exe_hash), "%s", result->exe_hash);
        (void)snprintf(client->wildcard_hash, sizeof(client->wildcard_hash), "%s", result->wildcard_hash);

        if (g_verbose) {
            printf("identified client fd=%d hash=%s path=%s command=%s\n",
                client->fd, client->exe_hash, client->exe_path, client->command_line);
        }
    } else {
        client->has_identity = false;
        fprintf(stderr, "inputd: unable to identify process for client pid=%ld uid=%ld\n",
            (long)client->pid, (long)client->uid);
    }

    client->state = KSI_CLIENT_STATE_READY;

    if (!client->pending_hello_valid) {
        return;
    }

    /* There is a buffered CLIENT_HELLO — process it now that we have identity. */
	{
		uint32_t requested = client->pending_hello_requested;
		uint32_t flags = client->pending_hello_flags;
		bool force_prompt = (flags & KSI_CLIENT_HELLO_FLAG_FORCE_PROMPT) != 0u;
		uint32_t missing = 0u;
		uint32_t granted;

		prepare_requested_capabilities(state, requested);
		granted = check_capabilities_sync(state, client, requested, force_prompt, &missing);

        if (missing != 0u && state->permissions != NULL) {
            /* Need to prompt. Transition to AWAITING_PROMPT and start the worker. */
            if (!start_prompt_task(state->commands, client->fd, client->connection_id, requested, missing,
                                   state->available_capabilities,
                                   client->exe_path, client->command_line, client->exe_hash,
                                   client->pid, client->uid, client->gid)) {
                /* Prompt thread failed; deny immediately. */
                client->pending_hello_valid = false;
                client->state = KSI_CLIENT_STATE_READY;
                send_hello_status(client,
                    client->pending_hello_client_id, client->pending_hello_correlation_id,
                    -1, 0u);
            } else {
                client->state = KSI_CLIENT_STATE_AWAITING_PROMPT;
            }
            return;
        }

        /* No prompt needed — grant immediately. */
        client->pending_hello_valid = false;

        if (requested != 0u && (granted & requested) != requested) {
            /* Union, not overwrite -- and only de-authenticate if nothing has
             * ever been granted on this connection. See the matching comment
             * in protocol_server.inc's handle_client_hello for the full
             * rationale. */
            client->granted_capabilities |= granted;
            client->authenticated = client->authenticated || client->granted_capabilities != 0u;
            send_hello_status(client,
                client->pending_hello_client_id, client->pending_hello_correlation_id,
                -1, granted);
            return;
        }

		if (granted != 0u) {
			ksi_permissions_note_seen(state->permissions, client->uid, client->exe_hash, client->exe_path);
		}

        client->authenticated = true;
        client->granted_capabilities |= granted;
        send_hello_status(client,
            client->pending_hello_client_id, client->pending_hello_correlation_id,
            0, granted);
    }
}

static uint32_t daemon_available_capabilities(const ksi_platform_backend *backend)
{
    if (backend->get_available_capabilities != NULL) {
        return backend->get_available_capabilities();
    }

    return 0;
}

static void prepare_requested_capabilities(ksi_daemon_state *state, uint32_t requested)
{
    if (state == NULL || state->backend == NULL || requested == 0u) {
        return;
    }

    if (state->backend->prepare_capabilities != NULL) {
        state->backend->prepare_capabilities(requested);
    }

    state->available_capabilities = daemon_available_capabilities(state->backend);
}

static bool client_has_capability(const ksi_client *client, uint32_t capability)
{
    if (!client->authenticated)
        return false;
    return (client->granted_capabilities & capability) == capability;
}

static uint32_t hook_type_to_cap_bit(uint32_t hook_type)
{
    if (hook_type == KSI_HOOK_KEYBOARD_LL) {
        return KSI_CAP_HOOK_KEYBOARD;
    }

    if (hook_type == KSI_HOOK_MOUSE_LL) {
        return KSI_CAP_HOOK_MOUSE;
    }

    return 0;
}


static uint32_t required_synthesis_capabilities(const ksi_input *inputs, size_t count)
{
    uint32_t required = 0;

    for (size_t i = 0; i < count; i++) {
        if (inputs[i].type == KSI_INPUT_KEYBOARD) {
            required |= KSI_CAP_SYNTH_KEYBOARD;
        } else if (inputs[i].type == KSI_INPUT_MOUSE) {
            required |= KSI_CAP_SYNTH_MOUSE;
        }
    }

    return required;
}

static uint32_t active_hook_subscription_mask(const ksi_daemon_state *state)
{
    uint32_t mask = 0;

    if (state == NULL) {
        return 0;
    }

    for (nfds_t i = 0; i < state->client_count; i++) {
        if (input_owner_matches_uid(state, state->clients[i].uid)) {
            mask |= state->clients[i].hook_subscriptions
                & (uint32_t)~state->clients[i].quarantined_hooks;
        }
    }

    return mask;
}

static uint32_t active_block_input_mask(const ksi_daemon_state *state)
{
    uint32_t mask = 0;

    if (state == NULL) {
        return 0;
    }

    for (nfds_t i = 0; i < state->client_count; i++) {
        if (input_owner_matches_uid(state, state->clients[i].uid)) {
            mask |= state->clients[i].block_input_mask;
        }
    }

    return mask;
}

static bool any_matching_hook_subscriptions(const ksi_daemon_state *state, uint32_t hook_type)
{
    uint32_t subscription_bit;

    if (state == NULL) {
        return false;
    }

    subscription_bit = hook_type_to_cap_bit(hook_type);

    if (subscription_bit == 0) {
        return false;
    }

    for (nfds_t i = 0; i < state->client_count; i++) {
        if ((state->clients[i].hook_subscriptions & subscription_bit) != 0
            && (state->clients[i].quarantined_hooks & subscription_bit) == 0u
            && input_owner_matches_uid(state, state->clients[i].uid)) {
            return true;
        }
    }

    return false;
}

static int update_grab_state(ksi_daemon_state *state)
{
    uint32_t hook_mask = active_hook_subscription_mask(state);
    uint32_t block_mask = active_block_input_mask(state);
    uint32_t available;
    uint32_t effective_hook_mask;
    bool keyboard_grabbed;
    bool interception_active;
    int result;

    if (state == NULL || state->backend == NULL) {
        return 0;
    }

    /* Fail open when synthetic output is unavailable. A HOOK grab EVIOCGRAB's the
     * physical device and relies on the synth device to replay every passed event;
     * if that output is dead (write failure, torn-down uinput) an already-grabbed
     * keyboard/mouse would stay captured while every replay write() fails -- a
     * fully dead device. So mask the hook portion of the grab by the capabilities
     * the backend can currently honor: when synth is down, get_available_
     * capabilities() drops KSI_CAP_HOOK_* and the grab is released, letting raw
     * hardware pass through. The next update_grab_state (re-run by
     * run_backend_maintenance on the availability-change edge) restores the real
     * mask once synth recovers. BlockInput is intentionally NOT gated this way:
     * blocking only drops events and needs no synth, so its grab must persist. */
    available = state->backend->get_available_capabilities != NULL
        ? state->backend->get_available_capabilities()
        : ~0u; /* no availability query -> assume everything honorable */
    effective_hook_mask = hook_mask & available;
    interception_active = effective_hook_mask != 0u || block_mask != 0u;

    /* Idle tracking is an ordinary read-only observer and must not preempt the
     * desktop at SCHED_FIFO priority. Promote the main reader immediately before
     * an interception grab can be acquired; demote it after all grabs are gone. */
    if (interception_active && !state->reader_realtime) {
        state->reader_realtime = set_realtime_priority("evdev reader");
    }

    /* Page locking is valuable only while grabbed input depends on this
     * process meeting hook deadlines. Do not reserve resident memory for the
     * boot-time idle observer. */
    if (interception_active && !state->memory_locked) {
        if (mlockall(MCL_CURRENT | MCL_FUTURE) == 0) {
            state->memory_locked = true;
        } else {
            fprintf(stderr,
                "inputd: note: mlockall failed (%s); hook latency may degrade under "
                "memory pressure\n", strerror(errno));
        }
    }

    result = state->backend->set_grab_hook_mask == NULL
        ? 0
        : state->backend->set_grab_hook_mask(effective_hook_mask);

    if (result != 0) {
        return result;
    }

    /* Platforms without BlockInput support must not advertise KSI_CAP_BLOCK_INPUT.
     * If a non-zero block mask somehow arrives on such a platform, fail so that
     * handle_set_block_input rolls back and reports an error rather than silently
     * accepting a mask that cannot be enforced. */
    result = state->backend->set_block_input_mask == NULL
        ? (block_mask == 0 ? 0 : -1)
        : state->backend->set_block_input_mask(block_mask);

    if (result != 0) {
        return result;
    }

    state->interception_active = interception_active;

    /* When the keyboard grab is released, any key the daemon replayed "down" on the
     * uinput virtual device would be stranded there: the user's physical key-up lands
     * on the now-ungrabbed real device and does not cancel the uinput key, so the
     * compositor keeps seeing it held. Enqueue the release on the OUTPUT SEQUENCER
     * thread (not here) so it is serialized with replay/synth -- no race on the synth
     * state -- and the daemon main thread never blocks doing it. Fires only on the
     * held->released edge so a steady grab does not repeatedly release. */
    /* Use the EFFECTIVE hook mask (post fail-open) so the held->released edge
     * fires -- and the stranded-key RELEASE_ALL below runs -- when a dead synth
     * drops the keyboard grab, not only on a client unsubscribe. */
    keyboard_grabbed = (effective_hook_mask & KSI_CAP_HOOK_KEYBOARD) != 0
        || (block_mask & KSI_BLOCK_INPUT_KEYBOARD) != 0;

    if (state->keyboard_grab_active && !keyboard_grabbed) {
        /* Only consume the held->released edge once the release is actually queued.
         * If the enqueue fails (queue saturated / OOM), leave keyboard_grab_active set
         * so the next update_grab_state call -- there are many callers and the queue
         * drains continuously -- retries, rather than stranding a key "down" on uinput
         * with no recovery until the next grab cycle. */
        if (output_queue_push_release_all(&state->output_queue)) {
            state->keyboard_grab_active = false;
        } else {
            fprintf(stderr, "inputd: failed to enqueue synthetic key release on keyboard ungrab; will retry\n");
        }
    } else {
        state->keyboard_grab_active = keyboard_grabbed;
    }

    if (!interception_active && state->reader_realtime) {
        if (set_normal_priority("evdev reader")) {
            state->reader_realtime = false;
        }
    }

    if (!interception_active && state->memory_locked) {
        if (munlockall() == 0) {
            state->memory_locked = false;
        } else {
            fprintf(stderr, "inputd: note: munlockall failed: %s\n", strerror(errno));
        }
    }

    return 0;
}

static void clear_hook_state(ksi_daemon_state *state)
{
    if (state == NULL) {
        return;
    }

    lane_flush_passthrough(&state->keyboard_lane);
    lane_flush_passthrough(&state->mouse_lane);
    synthetic_hook_queue_abort(&state->synthetic_hook_queue);

    for (nfds_t i = 0; i < state->client_count; i++) {
        state->clients[i].hook_subscriptions = 0;
        state->clients[i].hook_subscription_ordinal[0] = 0u;
        state->clients[i].hook_subscription_ordinal[1] = 0u;
        state->clients[i].block_input_mask = 0;
        state->clients[i].consecutive_hook_failures[0] = 0;
        state->clients[i].consecutive_hook_failures[1] = 0;
        state->clients[i].lease_expires_ms = 0u;
    }
}

static void renew_client_lease(ksi_client *client)
{
    if (client == NULL) {
        return;
    }

    if (client->hook_subscriptions != 0u || client->block_input_mask != 0u) {
        client->lease_expires_ms = monotonic_ms() + KSI_GRAB_LEASE_TIMEOUT_MS;
    } else {
        client->lease_expires_ms = 0u;
    }
}

static void expire_client_leases(ksi_daemon_state *state)
{
    uint64_t now;

    if (state == NULL) {
        return;
    }

    now = monotonic_ms();

    for (nfds_t i = state->client_count; i > 0; i--) {
        nfds_t index = i - 1u;
        ksi_client *client = &state->clients[index];
        if (client->lease_expires_ms == 0u || now < client->lease_expires_ms) {
            continue;
        }

        /* A switched-away user's stored hook state is deliberately dormant,
         * not abandoned. Pause its lease while logind says another uid owns
         * seat0; refresh it when that uid becomes active again. */
        if (!input_owner_matches_uid(state, client->uid)) {
            continue;
        }

        fprintf(stderr,
            "inputd: client fd=%d grab lease expired; failing open and disconnecting\n",
            client->fd);

        remove_client(state, index);
    }
}

/* Drops connections that never completed a CLIENT_HELLO within the handshake
 * window. A peer that connects and sends nothing otherwise keeps its slot forever
 * (leases arm only for hook/block clients), so N such peers could exhaust the
 * client table and deny the broker to everyone. Authenticated idle clients (e.g. a
 * query connection that sent a zero-capability hello) set hello_seen and are kept. */
static void expire_unauthenticated_clients(ksi_daemon_state *state)
{
    uint64_t now;

    if (state == NULL) {
        return;
    }

    now = monotonic_ms();

    for (nfds_t i = state->client_count; i > 0; i--) {
        nfds_t index = i - 1u;
        ksi_client *client = &state->clients[index];

        /* Capless-idle reaper: an authenticated connection holding no grants and
         * no hook/block subscriptions, idle past the timeout, is dropped so it
         * cannot pin a per-uid slot forever. Guarded on READY + !pending_hello so
         * a client mid-identify or mid-prompt is never reaped; any message resets
         * last_activity_ms, so a live query channel that polls is kept. */
        if (client->hello_seen
            && client->state == KSI_CLIENT_STATE_READY
            && !client->pending_hello_valid
            && client->granted_capabilities == 0u
            && client->hook_subscriptions == 0u
            && client->block_input_mask == 0u
            && client->last_activity_ms != 0u
            && now >= client->last_activity_ms + KSI_CAPLESS_IDLE_TIMEOUT_MS) {
            fprintf(stderr,
                "inputd: dropping client fd=%d: capability-less and idle for %u ms\n",
                client->fd, (unsigned)KSI_CAPLESS_IDLE_TIMEOUT_MS);
            remove_client(state, index);
            continue;
        }

        if (client->hello_seen || client->connected_at_ms == 0u
            || now < client->connected_at_ms + KSI_HANDSHAKE_TIMEOUT_MS) {
            continue;
        }

        fprintf(stderr,
            "inputd: dropping client fd=%d: no CLIENT_HELLO within %u ms\n",
            client->fd, (unsigned)KSI_HANDSHAKE_TIMEOUT_MS);
        remove_client(state, index);
    }
}

static void apply_fail_open_if_requested(ksi_daemon_state *state)
{
    if (state == NULL || atomic_exchange(&g_fail_open_requested, 0) == 0) {
        return;
    }

    fprintf(stderr,
        "inputd: safety-critical command queue overflow; clearing hooks and BlockInput\n");
    clear_hook_state(state);

    if (update_grab_state(state) != 0) {
        fprintf(stderr, "inputd: failed to release grabs during fail-open recovery\n");
    }
}

static void record_client_hook_success(ksi_daemon_state *state, nfds_t index, uint32_t hook_type)
{
    ksi_client *client;
    size_t lane_index;

    if (state == NULL || index >= state->client_count) {
        return;
    }

    client = &state->clients[index];

    if (!input_owner_matches_uid(state, client->uid)) {
        return;
    }
    lane_index = hook_type_to_lane_index(hook_type);

    if (client->last_hook_quarantine_ms[lane_index] != 0u
        && monotonic_ms() >= client->last_hook_quarantine_ms[lane_index] + 60000u) {
        client->consecutive_hook_failures[lane_index] = 0u;
    }
}

static void retry_quarantined_hooks(ksi_daemon_state *state)
{
    uint64_t now = monotonic_ms();
    bool changed = false;

    if (state == NULL) {
        return;
    }

    for (nfds_t i = 0u; i < state->client_count; i++) {
        ksi_client *client = &state->clients[i];

        for (size_t lane = 0u; lane < 2u; lane++) {
            uint32_t bit = lane == 0u ? KSI_CAP_HOOK_KEYBOARD : KSI_CAP_HOOK_MOUSE;

            if ((client->quarantined_hooks & bit) == 0u
                || client->quarantine_rearm_after_ms[lane] == 0u
                || now < client->quarantine_rearm_after_ms[lane]) {
                continue;
            }

            client->quarantined_hooks &= ~bit;
            client->quarantine_rearm_after_ms[lane] = 0u;
            hook_send_ref_clear_stalled(client->hook_send_ref, lane);
            changed = true;
        }
    }

    if (changed && update_grab_state(state) != 0) {
        fprintf(stderr, "inputd: failed to restore grabs after hook retry\n");
    }
}

static void record_client_hook_failure(
    ksi_daemon_state *state, nfds_t index, uint32_t hook_type,
    uint64_t event_id, uint32_t nesting_depth, uint32_t elapsed_ms,
    uint32_t reason)
{
    ksi_client *client;
    size_t lane_index;
    uint32_t lane_bit;
    uint32_t retry_after_ms;
    uint64_t now;
    ksi_hook_quarantined_payload notification;

    if (state == NULL || index >= state->client_count) {
        return;
    }

    client = &state->clients[index];

    if (!input_owner_matches_uid(state, client->uid)) {
        return;
    }

    lane_index = hook_type_to_lane_index(hook_type);
    lane_bit = (lane_index == 1u) ? KSI_CAP_HOOK_MOUSE : KSI_CAP_HOOK_KEYBOARD;

    if ((client->hook_subscriptions & lane_bit) == 0u
        || (client->quarantined_hooks & lane_bit) != 0u) {
        return;
    }

    now = monotonic_ms();

    if (client->last_hook_quarantine_ms[lane_index] != 0u
        && now >= client->last_hook_quarantine_ms[lane_index] + 60000u) {
        client->consecutive_hook_failures[lane_index] = 0u;
    }

    client->consecutive_hook_failures[lane_index]++;
    client->last_hook_quarantine_ms[lane_index] = now;
    client->quarantined_hooks |= lane_bit;
    hook_send_ref_mark_stalled(client->hook_send_ref, lane_index);
    retry_after_ms = 1000u << (client->consecutive_hook_failures[lane_index] > 5u
        ? 4u
        : client->consecutive_hook_failures[lane_index] - 1u);
    client->quarantine_rearm_after_ms[lane_index] = now + retry_after_ms;

    fprintf(stderr,
        "inputd: hook quarantined pid=%ld fd=%d hook=%u event=%llu depth=%u "
        "elapsed=%ums reason=%s strike=%u retry=%ums\n",
        (long)client->pid, client->fd, hook_type,
        (unsigned long long)event_id, nesting_depth, elapsed_ms,
        reason == KSI_HOOK_QUARANTINE_REASON_TIMEOUT ? "timeout" : "transport",
        client->consecutive_hook_failures[lane_index], retry_after_ms);

    memset(&notification, 0, sizeof(notification));
    notification.hook_type = hook_type;
    notification.reason = reason;
    notification.event_id = event_id;
    notification.generation = 0u;
    notification.strike_count = client->consecutive_hook_failures[lane_index];
    notification.retry_after_ms = retry_after_ms;

    (void)ipc_send_locked(client->fd, KSI_MESSAGE_HOOK_QUARANTINED, 0,
        event_id, &notification, sizeof(notification));

    if (update_grab_state(state) != 0) {
        fprintf(stderr, "inputd: failed to update grabs after hook quarantine\n");
    }

    if (client->consecutive_hook_failures[lane_index] < KSI_MAX_CONSECUTIVE_HOOK_FAILURES) {
        return;
    }

    fprintf(stderr,
        "inputd: invalidating hook session pid=%ld fd=%d after %u consecutive %s timeouts\n",
        (long)client->pid, client->fd, KSI_MAX_CONSECUTIVE_HOOK_FAILURES,
        lane_index == 1u ? "mouse" : "keyboard");

    remove_client(state, index);
}

/* Constructs a ksi_lane_event snapshotting the current hook subscribers and
 * pushes it onto the matching lane's action queue.  If allocation or enqueue
 * fails, the event is dropped; replaying it would violate the hook contract by
 * turning a backpressured hooked key into delayed text later.
 *
 * Called only from the main thread (via daemon_handle_hook_event from the
 * platform backend).  Reads state->clients[] without locking — that array is
 * owned by the main thread. */
