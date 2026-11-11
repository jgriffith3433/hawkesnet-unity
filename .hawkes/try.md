1. Open the Hawkes Net for Unity beta demo and wait for the Join screen.
2. Press Enter (or tap Join) and wait until the status says online.
3. Run playtest scenario `reconnect` (or ask Studio to play it): after a short wait the bot drops the socket — you should see “reconnecting…” then back online within about 3 seconds, still in the same room.
4. The same run then poisons the resume token and drops again: one failed resume, then a fresh ticket, and you are online again (events `blip-recovered`, `resume-refused`, `ticket-rejoined` on the playtest panel).
5. Optional (Editor): Test Runner → EditMode → `HawkesNetReconnectTests` (blip + refused resume).
