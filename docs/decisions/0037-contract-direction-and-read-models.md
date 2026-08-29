---
title: "Contract direction and read models"
status: accepted
pinned: false
date: 2026-08-29
---

# 0037. Contract direction and read models

## Decision

Cross-module coupling is governed by an explicit ladder. Tenancy is the base
(org/site master data; consumes no other module's contracts). Identity sits
above it and reads org data ONLY through its own event-fed read model
(org_directory, fed by OrganizationUpserted) - never IOrganizationLookup.
Entitlements sits on top and may consume IOrganizationLookup and the usage
probes. Platform ports (IScopeResolver, IEntitlements) are exempt from the
ladder: the host wires them, and their hub-shaped runtime coupling is
accepted - centralized authorization was chosen over claims-stuffed tokens
(ADR 6/21). Every org-writing flow MUST publish OrganizationUpserted.

## Why

Identity consuming IOrganizationLookup while Tenancy consumed IScopeResolver
formed a contract-level cycle invisible to the assembly-reference arch tests -
neither module could be extracted without the other. The read model deletes
the cycle instead of excusing it, and establishes the replication pattern
extraction needs anyway: a module that becomes a service turns its contract
reads into event-fed projections exactly like org_directory.

## Consequences

An arch test enforces the Identity rule; extend it per interface as contracts
grow. Read models are eventually consistent - org master-data changes reach
login/me after the event round-trip. The plugin direction
(IEntitlementUsageProbe: defined above, implemented below) remains allowed.
