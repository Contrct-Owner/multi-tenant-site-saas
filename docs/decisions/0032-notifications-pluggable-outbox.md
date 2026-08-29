---
title: "Notifications"
status: accepted
pinned: false
date: 2026-08-29
---

# 0032. Notifications

## Decision

INotificationTransport port with adapters (SES/SendGrid/Postmark/SMTP/local catcher); templates versioned in-repo; sends enqueued through the outbox so a notification is transactional with its cause. Per-org branding and per-user preferences from the start.

## Why

Magic links deliver the contact tier - email is on the authentication critical path.

## Consequences

We own template rendering, localization, bounce handling; an email outage is a partial auth outage - document the degradation story.
