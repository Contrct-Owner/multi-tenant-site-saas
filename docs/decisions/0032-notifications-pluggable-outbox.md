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

## Delivered shape (2026-08-30)

- **Transport**: SMTP adapter (MailKit) as the built-in production transport;
  local catcher is dev/test only and Production refuses to boot on it.
- **Templates**: one code-versioned renderer (`EmailTemplate`) producing
  text (source of truth) plus a minimal inline-styled HTML alternative;
  branding is the ORG's name - contact links arrive "from" the org the
  recipient recognizes. Forks needing richer mail replace the renderer, not
  the call sites. `brand.color` stays a public-app concern until an org
  branding editor exists - piping an uneditable setting into mail is motion,
  not progress.
- **Bounces**: a provider-neutral intake (`POST /notifications/bounce`,
  shared-secret header, disabled until `Notifications:BounceToken` is set)
  feeds a platform-global suppression list. The transport decorator drops
  suppressed sends with a loud log (never a throw - that would dead-letter
  an undeliverable message forever); contact-link issuance checks the list
  FIRST so the human who can act is told. Unsuppression is row deletion.
- **Per-user preferences**: deliberately absent. Every template email is
  transactional and auth-critical (access links, resets); preference and
  unsubscribe machinery ships with the first fork that adds non-transactional
  mail, not before.
- **The degradation story** the consequences demanded lives in
  `docs/production.md`.
