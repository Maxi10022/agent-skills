# Before and after

Typical agent messages, each rewritten into the style of the skill. The example app has webhooks, customers and share
links.

## The run-on line

Before, one subject line of 520 characters:

```
Expiring share links are cleaned up again: ScheduleCleanup set the tenant after ScheduledAt, which bound WithTenant to the scheduled wrapper itself, so the bus published the wrapper with no route and no cleanup was scheduled since 4f457d0; the tenant now comes first and the time last, so both sit on one envelope; links created with an expiry before this commit still have none scheduled, and ShareLinkValidator does not check ExpiresAt, so they stay valid until they are revoked by hand
```

After:

```
Fixed ScheduleCleanup.cs, expiring share links are cleaned up again

ScheduleCleanup set the tenant after the delivery time, which put the
tenant on the scheduled wrapper instead of the message. The bus found
no route for the wrapper, so no cleanup was scheduled since 4f457d0.
The tenant is now set first and the time last.

Links created before this commit still have no cleanup scheduled, and
ShareLinkValidator ignores ExpiresAt, so they stay valid until they
are revoked by hand.
```

What moved where:

- **The subject** keeps the file and the visible effect.
- **The first paragraph** explains the cause and the fix.
- **The second paragraph** names the consequence outside the diff.
- **The semicolons** became sentence ends.

## The file-first line that kept going

Before, already in the right shape, but 210 characters:

```
Updated Webhook.cs, Customer.cs and ShareLink.cs, the status, retry policy, delivery mode and expiry docs defer to the constants classes instead of repeating the values in prose so a renamed value cannot drift
```

After:

```
Updated Webhook.cs, Customer.cs and ShareLink.cs, docs cite constants

The status, retry policy, delivery mode and expiry docs repeated the
values in prose, which drifted whenever a value was renamed.
```

The subject names the files and the change. The list of documents and the reason go into the body.

## The changelog of renames

Before:

```
Swept the webhook tests to the agreed formatting: WebhookTestData now draws its payloads from the seeded faker, renamed Delivers_Webhook to Given_A_Subscriber_The_Webhook_Is_Delivered, Retries_On_Timeout to Given_A_Timeout_The_Delivery_Is_Retried, split Handles_Errors into three tests (one per status code), dropped the duplicate signature assertion
```

After:

```
Refactored WebhookDispatcherTests.cs, tests follow the naming rules

Renamed:
- Delivers_Webhook to Given_A_Subscriber_The_Webhook_Is_Delivered
- Retries_On_Timeout to Given_A_Timeout_The_Delivery_Is_Retried

Split Handles_Errors into one test per status code, and dropped the
signature assertion that WebhookSignatureTests already covers.
```

A list belongs in the body as bullets. That keeps it readable in `git log` and searchable with `git log --grep`.

## Three changes in one commit

Before:

```
Customer search matches accents now, CustomerTable got a sticky header, and bumped the test packages (xUnit 3.2.0 -> 4.0.2, bUnit 2.9.0 -> 2.11.3); also fixed a typo in the README
```

After, four commits:

```
Fixed CustomerSearch.cs, matches names regardless of accents
Updated CustomerTable.razor, the header stays visible while scrolling
Updated the test packages, xUnit 4.0.2 and bUnit 2.11.3
Fixed a typo in README.md
```

The semicolon and the "also" were the signal. Each change can now be reverted on its own, and a reviewer reads four
small diffs instead of one mixed one.

## Too thin

Some messages say nothing:

```
Fixed bug
Updated files
WIP
Changes from review
```

The fix is to name the target and the effect, for example:

```
Fixed ShareLinkPage.razor, expired links show their expiry, not a 404
```

When work really is unfinished, say what state it is in:

```
Added WebhookReplay.cs, a draft that replays only the newest delivery
```

## Session talk

Before:

```
As requested, I updated the dispatcher so it now retries with backoff like we discussed
```

After:

```
Updated WebhookDispatcher.cs, retries back off exponentially
```

The history outlives the conversation. A reader a year later has no idea who requested what. They need the change
and, when it is not obvious, its reason.

## Subjects that need no body

```
Added CustomerName.cs value object and tests
Removed ShareLinkCache.cs, links are read straight from the store now
Renamed DeliveryJob.cs to WebhookDeliveryJob.cs
Updated Shop.Tests.csproj, bumped bUnit and Alba
Fixed Contact.cs, empty strings fail validation like nulls
```

The subject plus the diff tells the whole story, so a body would only repeat it.
