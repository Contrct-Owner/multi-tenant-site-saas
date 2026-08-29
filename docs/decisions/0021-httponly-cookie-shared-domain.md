---
title: "Session model"
status: accepted
pinned: true
date: 2026-08-29
---

# 0021. Session model

## Decision

The API issues an encrypted HttpOnly, Secure, SameSite cookie after the AuthKit exchange; both apps live on subdomains of one parent. Magic links redeem server-side into the same cookie. CSRF protection mandatory.

## Why

No token is ever reachable from JavaScript.

## Consequences

Domain planning per fork; guests also receive session cookies (see ADR 7).
