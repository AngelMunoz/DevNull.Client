---
title: "Give Api.signUnder a contextual record"
kind: gap
status: pending
created: 2026-11-03
summary: "Api.signUnder has four parameters and is hidden from Api.fsi; the P19b scan finds it, but P19b forbids Core changes and no plan step owns it."
---

## Why

`src/DevNull.Client.Core/Api.fs:120` declares `signUnder<'env, 'e ...>(env, namespaceName, keyFile, message)` with four parameters. The P19b 4+-parameter scan names it. The P19b working parameters say no plan step owns it and Core must not change in P19b.

## What to do

Replace `namespaceName`, `keyFile`, and `message` with one contextual record, keep `env` as a parameter, and update the caller at `Api.fs:161`.

## Where

- `src/DevNull.Client.Core/Api.fs:120`
- `src/DevNull.Client.Core/Api.fs:161`
- `src/DevNull.Client.Core/Api.fsi` if the export shape changes
