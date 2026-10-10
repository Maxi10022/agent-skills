# Flake catalogue

Every entry was a real failure in a suite of about 900 host, page and unit tests on Marten, Wolverine, Alba and
bUnit. Each looked random under full-suite load and turned out to be deterministic. Before you call a failure
"flaky", check whether one of these explains it.

Each entry gives the **symptom**, the **cause**, the **fix**, and **how it surfaced**.

## Contents

- Wolverine: 1–3
- Marten daemon: 4–6
- Test data: 7–9
- Shared fakes and fixtures: 10–11
- Load-only failures: 12

---

## 1. A message that retries forever blocks every later tracked session

- **Symptom:** after an import test ran in a host, every later tracked command in the same host timed out, in
  tests that had nothing to do with imports.
- **Cause:** the import cascaded an enrichment message that needs an external API key. No test configures one, so
  the handler threw a configuration exception. The error policy requeued the message and paused the queue
  indefinitely. A tracked session waits for all activity, including that envelope.
- **Fix:** one `TrackActivity()` on the base:
  `Host.TrackActivity().Timeout(Timeouts.Tracked).IgnoreMessageType<ThatMessage>()`. Every send, invoke and tracked
  call goes through it. Add every background message type that cannot finish without configuration.
- **How it surfaced:** splitting a large shared collection. Test order had hidden it.

## 2. Host shutdown takes 30 seconds

- **Symptom:** each fixture's teardown took about 31 s, so the suite's tail was long.
- **Cause:** the paused queue from entry 1 held shutdown for Wolverine's default `Durability.DrainTimeout` (30 s).
- **Fix:** `services.ConfigureWolverine(options => options.Durability.DrainTimeout = 2.Seconds())` in the fixture
  base. Teardown dropped to 3 s.

## 3. Tracked commands time out on the first command of a run

- **Symptom:** the first command in a fixture timed out intermittently, mostly at the start of a full run.
- **Cause:** raw `Host.InvokeMessageAndWaitAsync` ran on Wolverine's 5 s default timeout. The first command of a host
  pays for Wolverine's handler codegen, while every other fixture boots at the same time.
- **Fix:** one `Timeouts` class (tracked 60 s, projection 30 s, probe 30 s, render 60 s) used through the base
  helpers. A convention test bans raw tracked invokes, raw projection waits and numeric timeouts.

## 4. "No event activity was detected within the timeout span", at once

- **Symptom:** a page test failed in `InitializeAsync` with that `TimeoutException`, immediately, not after the
  timeout.
- **Cause:** Marten's `WaitForNonStaleProjectionDataAsync` throws when the event store holds no events. A fresh class
  fixture wiping before its first test hits exactly that.
- **Fix:** the base's wait reads `Advanced.FetchEventStoreStatistics()` and returns when `EventCount == 0`. Never call
  Marten's wait directly.

## 5. A page or subscriber receives the late notification of its own seed

- **Symptoms:**
  - an edit in flight was overwritten by the stored value ("settles on the new note" saw the old note);
  - a paging test listed rows twice;
  - a notification tray toasted its own seed rows.
- **Cause:** the app forwards events from Marten's subscription through Wolverine to an in-process hub. Those
  handlers run after the projection wait returns. The page rendered after `Given(...)` plus the projection wait,
  then got a "live" notification for data it had already loaded.
- **Fix:** `GivenForwarded(...)` appends inside a tracked session with
  `WaitForNonStaleDaemonDataAfterExecution(...)`. A first version still flaked in full runs: the daemon catch-up
  must also run inside the session, so the session tracks what the forwarding subscription publishes meanwhile.
  Batch seeds in one call.
- **How it surfaced:** two independent agents sweeping different slices found it on the same day.

## 6. A stray row, or a NullReferenceException, in the next page test

- **Symptoms:** a page test's "rows.Count == 2" wait found three rows. Another page test threw a
  NullReferenceException loading a row without a note.
- **Cause:** the previous test ended while its command was still in flight. It landed after the next test's wipe,
  and the projection recreated the document from that single event, with default values for every field the
  creation event would have set.
- **Fix:**
  - A test that sends a command waits for that command's visible result (the new expiry, the new name), not for
    "any settled state", and then for the non-stale projections.
  - `WipeAsync<T>()` catches the daemon up before deleting.
  - The wipe alone is not enough. Its daemon wait only covers events already appended, and a command still queued
    in Wolverine has not appended anything yet. Only the test that sent the command can wait for it.

## 7. "Unique" values collide between tests

- **Symptom:** a probe never completed. The real exception (`SingleOrDefault` found two items) was thrown inside an
  event handler and swallowed.
- **Cause:** values built from `fixture.Faker.UniqueIndex`. Bogus fixes `UniqueIndex` when the `Faker` instance is
  created, and the faker was shared by the whole collection, so every test got the same "unique" name. A filter over
  a process-wide singleton then matched another test's item.
- **Fix:** a faker per test, seeded from the test's identity. Unique text comes from `Random.AlphaNumeric(n)` or a
  Guid. Anything that filters a shared singleton filters by a per-test id.

## 8. Seeded rows come back in the wrong order

- **Symptom:** a paging test saw the oldest row arrive early.
- **Cause:** v7 Guids created within the same millisecond do not keep their creation order. Seeds created in a tight
  loop sorted arbitrarily.
- **Fix:** derive seeded ids from a fixed instant that counts up:
  `Guid.CreateVersion7(SeedingStart.AddSeconds(++_seeded))`.

## 9. A date test fails in one time zone

- **Symptom:** a short-date formatting test failed on a machine in UTC+14.
- **Cause:** the test built "noon" in UTC, and the formatter renders in the machine's zone, which crossed midnight.
- **Fix:** build the instant in the zone the code under test uses:
  `new DateTimeOffset(new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Local))`. Better still, inject a `TimeProvider`.

## 10. A canned reply answers the wrong test

- **Symptom:** a chat test received an answer scripted by another test.
- **Cause:** a fake completions endpoint in a shared fixture kept one queue of canned replies. A reply one test left
  unused answered the next test's request.
- **Fix:** key the fake's state by a per-test value (the model id from the test data):
  `Replies.GetOrAdd(model, ...)`.

## 11. A big shared collection hides ordering bugs

- **Symptom:** after a collection split, tests that had always passed began to fail (entries 1 and 2 surfaced this
  way).
- **Cause:** a fixed test order in one big collection happened to run the poisoning test last.
- **Fix:** fix the leak and keep the split. Never merge collections, or add a fixture, to make a failure go away.

## 12. Green alone, red in the full suite

- **Symptom:** a page test passed on its own and failed in 18 of 18 full runs.
- **Cause:** a helper clicked a record and typed at once. Under load, the pane had not yet shown the clicked
  record, so the typed edit went to the form that the load then replaced, and Save was a no-op.
- **Fix:** the helper waits until the pane shows the clicked item before it returns. In general, wait for the state
  the next step depends on, never assume a render landed.
- **How to find these:** after a slice runs green three times, run the whole suite several times in a row.

---

## Ground rules that came out of the catalogue

- A fixture is never a fix. It moves a leak somewhere you no longer see it.
- Every wait goes through the base: one tracking entry point, one daemon wait, one timeout class.
- A test ends only after everything it started has visibly finished.
- Seeds that something listens to are forwarded seeds.
- A failure inside an event handler can be swallowed. When a probe "never fires", look for an exception in the
  handler's predicate or in the subscriber.
