---
title: "Localization and accessibility posture"
status: accepted
pinned: false
date: 2026-08-30
---

# 0047. Localization and accessibility posture

## Decision

**Localization: the template is English-source and translation-READY, not
translated.** What that means concretely:

- Anything locale-sensitive that is NOT prose already goes through the
  platform's own formatting: dates and times render via `Intl` with the
  viewer's locale (the `fmt*` helpers pass no hardcoded locale), site-local
  correctness comes from IANA zones server-side, and both documents declare
  `lang="en"`. (The one hardcoded locale, `en-CA` in the public app's hours
  code, is an ISO-date plumbing trick, not display formatting - commented
  as such.)
- Prose stays inline in English. String extraction is deferred to the
  first fork that needs a second language, and the library choice is
  pinned NOW so that fork does not relitigate it: `react-i18next` for the
  frontends; server-side email prose lives behind the `EmailTemplate`
  renderer, which is the single extraction point for mail.
- Why deferred: extraction has a real ongoing tax (every string change
  touches a catalog, every review reads indirection) and pays nothing
  until a second language exists. A template that guessed wrong about
  which fork goes multilingual would tax every fork that doesn't.

**Accessibility: axe-clean is the maintained floor.** Both apps pass
axe-core with zero violations as of this ADR (all console pages, locator,
site page), and the fixes live in the shared components where possible
(focusable scroll regions, sr-only action headers) so new pages inherit
them. The method is repeatable: serve `axe.min.js` (workspace
devDependency) from the app's public dir during dev and `axe.run()` per
route. Automated checks are the floor, not the ceiling - a manual
screen-reader audit is real-user work that belongs to a fork approaching
launch.

## Consequences

- The a11y pass found and fixed a theming-correctness bug (media-based
  `dark:` utilities over class-switched tokens); the dark variant is now
  class-pinned in the token file - forks adding a real dark mode toggle
  the `.dark` class and get the whole theme, never half of it.
- New user-facing prose is written in English without ceremony; forks
  extracting strings start from the `EmailTemplate` seam and the pages,
  in that order (email is the smallest, highest-stakes surface).
