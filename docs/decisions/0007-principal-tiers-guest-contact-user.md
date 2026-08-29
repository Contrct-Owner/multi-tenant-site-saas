---
title: "Principal tiers"
status: accepted
pinned: false
date: 2026-08-29
---

# 0007. Principal tiers

## Decision

Three tiers: Guest (built from request host - org, sometimes site - before authn), identified contact (magic link / invite token holder), authenticated user. No anonymous actions.

## Why

A guest is a guest OF a tenant; the contact tier covers appointment checks and confirmations without forcing account creation.

## Consequences

Signed-token subsystem with expiry, revocation, and its own audit actor type. Guests get session cookies (CSRF token, rate-limit subject).
